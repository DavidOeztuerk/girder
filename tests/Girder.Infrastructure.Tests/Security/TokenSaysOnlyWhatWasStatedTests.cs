using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json;
using Girder.Abstractions.Security;
using Girder.Infrastructure.Models;
using Girder.Infrastructure.Security;
using Girder.Infrastructure.Security.Keys;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Girder.Infrastructure.Tests.Security;

/// <summary>
/// A claim the caller did not state is not written.
/// </summary>
/// <remarks>
/// <para><c>email_verified</c> and <c>account_status</c> used to be written on
/// every token from defaults nobody chose — <c>false</c> and <c>"Active"</c>.
/// Both are read by Girder's own policies, so with those defaults
/// <c>EmailVerified</c> refused everyone and <c>ActiveAccount</c> admitted
/// everyone: a constant in the shape of a check, at the gate meant to stop a
/// suspended account.</para>
/// <para>Nobody noticed because a default does not go missing, it answers.
/// <c>false</c> and <c>"Active"</c> look like results.</para>
/// </remarks>
[Trait("Category", "Unit")]
public class TokenSaysOnlyWhatWasStatedTests
{
    [Fact]
    public async Task What_was_not_stated_is_not_in_the_token()
    {
        var payload = await Payload(new UserClaims
        {
            UserId = "user-123",
            Email = "anna@example.com"
            // EmailVerified and AccountStatus deliberately unset — exactly as
            // every caller who does not know the fields exist leaves them.
        });

        payload.TryGetProperty("email_verified", out _).Should().BeFalse(
            "a missing statement may lead to no; it must never lead to yes");
        payload.TryGetProperty("account_status", out _).Should().BeFalse();
    }

    [Fact]
    public async Task What_was_stated_is_in_the_token()
    {
        var payload = await Payload(new UserClaims
        {
            UserId = "user-123",
            Email = "anna@example.com",
            EmailVerified = true,
            AccountStatus = "Suspended"
        });

        // The claim carries ClaimValueTypes.Boolean, so the payload holds a real
        // JSON true rather than a string.
        payload.GetProperty("email_verified").GetBoolean().Should().BeTrue();
        payload.GetProperty("account_status").GetString().Should().Be("Suspended");
    }

    /// <summary>
    /// The counter-probe on the dangerous half: a suspended account must not
    /// carry <c>"Active"</c>.
    /// </summary>
    [Fact]
    public async Task A_suspended_account_does_not_carry_Active()
    {
        var payload = await Payload(new UserClaims
        {
            UserId = "user-123",
            Email = "anna@example.com",
            AccountStatus = "Suspended"
        });

        payload.GetProperty("account_status").GetString().Should().NotBe("Active");
    }

    /// <summary>
    /// A list-valued claim arrives as a JSON array.
    /// </summary>
    /// <remarks>
    /// <c>CustomClaims</c> is a dictionary of strings and has one value per name,
    /// so <c>["user", "admin"]</c> came out as the string <c>"user,admin"</c> or
    /// not at all — and a consumer validating the shape rejected the token.
    /// Reproducing one claim in the form an existing consumer expects is the whole
    /// purpose of a free claim field.
    /// </remarks>
    [Fact]
    public async Task A_list_valued_claim_arrives_as_an_array()
    {
        var payload = await Payload(new UserClaims
        {
            UserId = "user-123",
            Email = "anna@example.com",
            CustomClaimArrays = new() { ["groups"] = ["user", "admin"] }
        });

        var groups = payload.GetProperty("groups");

        groups.ValueKind.Should().Be(JsonValueKind.Array);
        groups.EnumerateArray().Select(entry => entry.GetString()).Should().Equal("user", "admin");
    }

    /// <summary>One value stays one value, and not an array of one.</summary>
    [Fact]
    public async Task A_single_valued_claim_stays_a_string()
    {
        var payload = await Payload(new UserClaims
        {
            UserId = "user-123",
            Email = "anna@example.com",
            CustomClaims = new() { ["plan"] = "pro" }
        });

        payload.GetProperty("plan").ValueKind.Should().Be(JsonValueKind.String);
    }

    private static async Task<JsonElement> Payload(UserClaims claims)
    {
        var settings = new JwtSettings
        {
            Secret = "ThisIsATestSecretKeyThatIsAtLeast64CharactersLongForHmacSha256Signing!!",
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            ExpireMinutes = 60
        };

        var signing = SigningKey.FromSharedSecret(settings.Secret, kid: null);

        var service = new JwtService(
            Options.Create(settings),
            new KeyRing([signing], signing),
            NullLogger<JwtService>.Instance,
            permissions: null,
            Substitute.For<ITokenRevocationEvaluator>(),
            Substitute.For<ITokenRevocationWriter>());

        var token = await service.GenerateTokenAsync(claims);

        var encoded = new JwtSecurityToken(token.AccessToken).EncodedPayload
            .Replace('-', '+')
            .Replace('_', '/');
        encoded = encoded.PadRight(encoded.Length + ((4 - (encoded.Length % 4)) % 4), '=');

        return JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(encoded))).RootElement;
    }
}
