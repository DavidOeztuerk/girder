using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Girder.Infrastructure.Security.Keys;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Girder.Infrastructure.Tests.Security.Keys;

/// <summary>
/// The algorithm is decided by the configuration, never by the token.
/// </summary>
/// <remarks>
/// The classic attack against a service that opened up its algorithms: take the
/// public key everyone knows, mark the token <c>HS256</c>, and sign it with
/// those key bytes as the HMAC secret. A verifier that reads the algorithm from
/// the header accepts it and hands the attacker any identity they typed.
/// <para>
/// These tests pin the behaviour of the composed ring, not of one line in it.
/// Verified by removing pieces: <c>RequireSignedTokens</c> is what refuses
/// <c>alg: none</c>. Pinning the algorithm list changes nothing here, because
/// the handler already refuses to use an asymmetric key as an HMAC secret — it
/// is kept as a decision of ours rather than a property of the library, and
/// <see cref="The_ring_pins_the_algorithms_it_accepts"/> is what states it.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class AlgorithmConfusionTests
{
    private const string Issuer = "girder-tests";
    private const string Audience = "girder-tests";

    [Fact]
    public async Task A_token_marked_HS256_but_signed_with_the_public_key_is_refused()
    {
        var pair = TestKeys.NewEcdsaPair(kid: "current");
        var ring = new KeyRing([pair.Public], signingKey: null);

        // The attacker knows the public key: it is public.
        var publicKeyBytes = ((ECDsaSecurityKey)pair.Public.Key).ECDsa.ExportSubjectPublicKeyInfo();
        var forged = ForgeHs256(publicKeyBytes, kid: "current", subject: "attacker");

        var result = await new JsonWebTokenHandler()
            .ValidateTokenAsync(forged, ring.ValidationParameters(Issuer, Audience));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task A_token_with_alg_none_is_refused()
    {
        var pair = TestKeys.NewEcdsaPair(kid: "current");
        var ring = new KeyRing([pair.Public], signingKey: null);

        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(
            new Dictionary<string, string> { ["alg"] = "none", ["typ"] = "JWT" }));
        var payload = Base64Url(PayloadBytes("attacker"));
        var unsigned = $"{header}.{payload}.";

        var result = await new JsonWebTokenHandler()
            .ValidateTokenAsync(unsigned, ring.ValidationParameters(Issuer, Audience));

        result.IsValid.Should().BeFalse();
    }

    /// <summary>
    /// A ring holding only asymmetric keys must refuse a genuine HS256 token,
    /// however well signed — the algorithm is not on the list.
    /// </summary>
    [Fact]
    public async Task Only_the_configured_algorithms_are_accepted()
    {
        var pair = TestKeys.NewEcdsaPair(kid: "current");
        var ring = new KeyRing([pair.Public], signingKey: null);

        var secret = SigningKey.FromSharedSecret(new string('s', 48), kid: null);
        var genuineHs256 = Issue(secret);

        var result = await new JsonWebTokenHandler()
            .ValidateTokenAsync(genuineHs256, ring.ValidationParameters(Issuer, Audience));

        result.IsValid.Should().BeFalse();
    }

    /// <summary>
    /// Rotation has to overlap. Without it, the moment a new key goes live every
    /// token in circulation becomes invalid at once.
    /// </summary>
    [Fact]
    public async Task A_token_under_the_previous_kid_stays_valid_while_the_new_kid_issues()
    {
        var previous = TestKeys.NewEcdsaPair(kid: "2026-07");
        var current = TestKeys.NewEcdsaPair(kid: "2026-08");

        var issuedBefore = Issue(previous.Private);
        var issuedAfter = Issue(current.Private);

        var ring = new KeyRing([current.Public, previous.Public], signingKey: current.Private);
        var parameters = ring.ValidationParameters(Issuer, Audience);
        var handler = new JsonWebTokenHandler();

        (await handler.ValidateTokenAsync(issuedBefore, parameters)).IsValid.Should().BeTrue();
        (await handler.ValidateTokenAsync(issuedAfter, parameters)).IsValid.Should().BeTrue();
    }

    /// <summary>
    /// The migration window: tokens of both algorithms are in circulation, and
    /// dropping either one would sign a population out.
    /// </summary>
    [Fact]
    public async Task Legacy_HS256_and_new_ES256_are_accepted_side_by_side()
    {
        var legacy = SigningKey.FromSharedSecret(new string('s', 48), kid: null);
        var current = TestKeys.NewEcdsaPair(kid: "2026-08");

        var ring = new KeyRing([current.Public, legacy], signingKey: current.Private);
        var parameters = ring.ValidationParameters(Issuer, Audience);
        var handler = new JsonWebTokenHandler();

        (await handler.ValidateTokenAsync(Issue(legacy), parameters)).IsValid.Should().BeTrue();
        (await handler.ValidateTokenAsync(Issue(current.Private), parameters)).IsValid.Should().BeTrue();
    }

    /// <summary>
    /// And once the window closes, removing the secret refuses the old tokens —
    /// otherwise the migration never actually ends.
    /// </summary>
    [Fact]
    public async Task Removing_the_legacy_secret_refuses_the_old_tokens()
    {
        var legacy = SigningKey.FromSharedSecret(new string('s', 48), kid: null);
        var current = TestKeys.NewEcdsaPair(kid: "2026-08");
        var oldToken = Issue(legacy);

        var ring = new KeyRing([current.Public], signingKey: current.Private);

        var result = await new JsonWebTokenHandler()
            .ValidateTokenAsync(oldToken, ring.ValidationParameters(Issuer, Audience));

        result.IsValid.Should().BeFalse();
    }

    /// <summary>
    /// The accepted algorithms come from the configured keys, so the set is
    /// visible and cannot be widened by anything a caller sends.
    /// </summary>
    [Fact]
    public void The_ring_pins_the_algorithms_it_accepts()
    {
        var pair = TestKeys.NewEcdsaPair(kid: "current");
        var legacy = SigningKey.FromSharedSecret(new string('s', 48), kid: null);

        new KeyRing([pair.Public], null).ValidationParameters(Issuer, Audience)
            .ValidAlgorithms.Should().BeEquivalentTo([SecurityAlgorithms.EcdsaSha256]);

        new KeyRing([pair.Public, legacy], null).ValidationParameters(Issuer, Audience)
            .ValidAlgorithms.Should().BeEquivalentTo(
                [SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.HmacSha256]);
    }

    private static string Issue(SigningKey key) => new JsonWebTokenHandler().CreateToken(
        new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Expires = DateTime.UtcNow.AddMinutes(15),
            Claims = new Dictionary<string, object> { ["sub"] = "legitimate-subject" },
            SigningCredentials = key.SigningCredentials()
        });

    private static string ForgeHs256(byte[] hmacSecret, string kid, string subject)
    {
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(
            new Dictionary<string, string> { ["alg"] = "HS256", ["typ"] = "JWT", ["kid"] = kid }));
        var payload = Base64Url(PayloadBytes(subject));

        var signed = $"{header}.{payload}";
        var signature = HMACSHA256.HashData(hmacSecret, Encoding.ASCII.GetBytes(signed));

        return $"{signed}.{Base64Url(signature)}";
    }

    private static byte[] PayloadBytes(string subject) => JsonSerializer.SerializeToUtf8Bytes(
        new Dictionary<string, object>
        {
            ["sub"] = subject,
            ["iss"] = Issuer,
            ["aud"] = Audience,
            ["exp"] = DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds()
        });

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
