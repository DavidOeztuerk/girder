using System.Security.Cryptography;
using Girder.Infrastructure.Security.Keys;

namespace Girder.Infrastructure.Tests.Security.Keys;

/// <summary>
/// Verifying a token and issuing one must be separable.
/// </summary>
/// <remarks>
/// With HS256 the verification key <b>is</b> the signing key, so every service
/// that checks a token can also mint one — for any subject, with any role. A
/// service that only consumes tokens must be able to hold something that
/// cannot sign, rather than something it is merely asked not to use.
/// </remarks>
[Trait("Category", "Unit")]
public class KeySeparationTests
{
    [Fact]
    public void A_public_key_cannot_sign()
    {
        var pair = TestKeys.NewEcdsaPair(kid: "test-1");

        pair.Public.CanSign.Should().BeFalse();
    }

    [Fact]
    public void A_private_key_can()
    {
        var pair = TestKeys.NewEcdsaPair(kid: "test-1");

        pair.Private.CanSign.Should().BeTrue();
    }

    /// <summary>
    /// The distinction has to be enforced, not documented: asking a public key
    /// for signing credentials is a wiring mistake and must fail there.
    /// </summary>
    [Fact]
    public void Asking_a_public_key_for_signing_credentials_fails()
    {
        var pair = TestKeys.NewEcdsaPair(kid: "test-1");

        var sign = () => pair.Public.SigningCredentials();

        sign.Should().Throw<InvalidOperationException>().WithMessage("*public*");
    }

    /// <summary>
    /// A shared secret can do both, and that is exactly why it cannot separate
    /// the two roles. Saying so in the type keeps the property discoverable.
    /// </summary>
    [Fact]
    public void A_shared_secret_signs_and_verifies_with_the_same_material()
    {
        var key = SigningKey.FromSharedSecret(new string('s', 48), kid: null);

        key.CanSign.Should().BeTrue();
        key.SeparatesIssuingFromVerifying.Should().BeFalse();
    }

    [Fact]
    public void An_asymmetric_pair_does_separate_them()
    {
        var pair = TestKeys.NewEcdsaPair(kid: "test-1");

        pair.Public.SeparatesIssuingFromVerifying.Should().BeTrue();
    }
}

/// <summary>Key pairs for tests, in the form a deployment would supply them.</summary>
internal static class TestKeys
{
    internal readonly record struct Pair(SigningKey Private, SigningKey Public);

    internal static Pair NewEcdsaPair(string kid)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var privateKey = Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey());
        var publicKey = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());

        return new Pair(
            SigningKey.FromEcdsaPrivateKey(privateKey, kid),
            SigningKey.FromEcdsaPublicKey(publicKey, kid));
    }
}
