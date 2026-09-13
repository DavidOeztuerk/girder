using System.Text;
using System.Text.Json;
using Girder.Abstractions.Security.Encryption;
using Girder.Redis.Security.Encryption;
using Girder.Infrastructure.Security.Encryption;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Girder.Infrastructure.Tests.Security.Encryption;

/// <summary>
/// What the ciphertext itself has to look like.
///
/// The suite that existed before 4.4.1 asked <c>Success</c>, the error strings
/// and a round trip, and the only thing it ever said about the ciphertext was
/// <c>EncryptedData.Should().NotBeNullOrEmpty()</c>. All of that stayed green
/// while <see cref="DataEncryptionService"/> copied the plaintext into the
/// result buffer and wrote an all-zero authentication tag: a round trip is
/// trivially green when nothing happens to the data on the way there and back.
///
/// Every test here fails against that implementation. They are written against
/// the ciphertext and against a wrong key — the two things a round trip cannot
/// see.
/// </summary>
[Trait("Category", "Unit")]
public class DataEncryptionServiceCipherTests
{
    private readonly DataEncryptionService _sut;
    private readonly IKeyManagementService _keyManagement = Substitute.For<IKeyManagementService>();
    private readonly ILogger<DataEncryptionService> _logger = Substitute.For<ILogger<DataEncryptionService>>();
    private readonly IConnectionMultiplexer _connectionMultiplexer = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _database = Substitute.For<IDatabase>();

    public DataEncryptionServiceCipherTests()
    {
        _connectionMultiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_database);
        var options = Options.Create(new DataEncryptionOptions());
        _sut = new DataEncryptionService(_keyManagement, _logger, options, _connectionMultiplexer);
    }

    /// <summary>
    /// Counter-check 1 — a foreign key must not decrypt, and it must never
    /// report <c>IntegrityVerified</c> while doing so.
    ///
    /// Before 4.4.1 this returned <c>Success=true IntegrityVerified=true</c>
    /// with the plaintext, because the key was never used for anything and the
    /// integrity hash it checked was a hash of the plaintext — which a foreign
    /// key reproduces exactly.
    /// </summary>
    [Fact]
    public async Task DecryptWithKeyAsync_ForeignKey_Fails_AndNeverClaimsIntegrity()
    {
        const string secret = "sk-ant-GEHEIMNIS-DAS-NIEMAND-SEHEN-DARF";

        var ours = CreateKey("key-ours");
        var theirs = CreateKey("key-theirs");
        _keyManagement.GetKeyAsync("key-ours", Arg.Any<CancellationToken>()).Returns(ours);
        _keyManagement.GetKeyAsync("key-theirs", Arg.Any<CancellationToken>()).Returns(theirs);

        var encrypted = await _sut.EncryptWithKeyAsync(secret, "key-ours");
        encrypted.Success.Should().BeTrue();

        var decrypted = await _sut.DecryptWithKeyAsync(encrypted.EncryptedData, "key-theirs");

        decrypted.Success.Should().BeFalse();
        decrypted.Data.Should().NotContain(secret);
        decrypted.IntegrityVerified.Should().BeFalse();
    }

    /// <summary>
    /// Counter-check 2 — one flipped bit in one ciphertext byte has to be
    /// noticed. That is what the GCM tag is for; an all-zero tag notices
    /// nothing.
    /// </summary>
    [Fact]
    public async Task DecryptWithKeyAsync_OneFlippedBit_Fails()
    {
        var key = CreateKey("key-bit");
        _keyManagement.GetKeyAsync("key-bit", Arg.Any<CancellationToken>()).Returns(key);

        var encrypted = await _sut.EncryptWithKeyAsync("a payload worth protecting", "key-bit");
        encrypted.Success.Should().BeTrue();

        var tampered = FlipOneBitInCiphertext(encrypted.EncryptedData);

        var decrypted = await _sut.DecryptWithKeyAsync(tampered, "key-bit");

        decrypted.Success.Should().BeFalse();
        decrypted.IntegrityVerified.Should().BeFalse();
    }

    /// <summary>
    /// Counter-check 3 — the same plaintext twice must produce two different
    /// ciphertexts, because the IV is drawn fresh per operation. This one
    /// falls the moment somebody pins the IV to a constant, which is the usual
    /// next mistake after this one.
    /// </summary>
    [Fact]
    public async Task EncryptWithKeyAsync_SamePlaintextTwice_ProducesDifferentCiphertexts()
    {
        var key = CreateKey("key-iv");
        _keyManagement.GetKeyAsync("key-iv", Arg.Any<CancellationToken>()).Returns(key);

        var first = await _sut.EncryptWithKeyAsync("the same thing", "key-iv");
        var second = await _sut.EncryptWithKeyAsync("the same thing", "key-iv");

        first.Success.Should().BeTrue();
        second.Success.Should().BeTrue();

        CipherBytesOf(first.EncryptedData).Should().NotEqual(CipherBytesOf(second.EncryptedData));
        first.InitializationVector.Should().NotBe(second.InitializationVector);
    }

    /// <summary>
    /// Counter-check 4 — THE ONE THAT WOULD HAVE FOUND THE ORIGINAL DEFECT.
    ///
    /// The plaintext must not occur anywhere in the result, and the search is
    /// for a byte subsequence in the raw bytes — not only in the Base64-decoded
    /// envelope. A test that decodes first and compares strings can be fooled
    /// by an encoding; a subsequence search over the raw bytes cannot.
    ///
    /// Three places are searched, because the defect put the plaintext in the
    /// second one: the Base64 text as handed to the caller, the JSON envelope
    /// it decodes to, and the Data field inside that envelope.
    /// </summary>
    [Fact]
    public async Task EncryptWithKeyAsync_PlaintextDoesNotAppearAnywhereInTheResult()
    {
        const string secret = "sk-ant-GEHEIMNIS-DAS-NIEMAND-SEHEN-DARF";
        var needle = Encoding.UTF8.GetBytes(secret);

        var key = CreateKey("key-leak");
        _keyManagement.GetKeyAsync("key-leak", Arg.Any<CancellationToken>()).Returns(key);

        var encrypted = await _sut.EncryptWithKeyAsync(secret, "key-leak");
        encrypted.Success.Should().BeTrue();

        ContainsSubsequence(Encoding.UTF8.GetBytes(encrypted.EncryptedData), needle)
            .Should().BeFalse("the Base64 the caller stores must not carry the plaintext");

        var envelope = Convert.FromBase64String(encrypted.EncryptedData);
        ContainsSubsequence(envelope, needle)
            .Should().BeFalse("the JSON envelope must not carry the plaintext");

        ContainsSubsequence(CipherBytesOf(encrypted.EncryptedData), needle)
            .Should().BeFalse("the Data field must not carry the plaintext");
    }

    /// <summary>
    /// Counter-check 4b — nor may a hash of the plaintext be stored beside the
    /// ciphertext. A SHA-256 of the plaintext is an oracle: whoever reads the
    /// store can try candidates against it without ever touching the key. This
    /// is a second, separate defect from the missing encryption, and it
    /// survives any fix that only replaces the cipher.
    /// </summary>
    [Fact]
    public async Task EncryptWithKeyAsync_StoresNoHashOfThePlaintext()
    {
        const string secret = "sk-ant-GEHEIMNIS-DAS-NIEMAND-SEHEN-DARF";

        var key = CreateKey("key-oracle");
        _keyManagement.GetKeyAsync("key-oracle", Arg.Any<CancellationToken>()).Returns(key);

        var encrypted = await _sut.EncryptWithKeyAsync(secret, "key-oracle");
        encrypted.Success.Should().BeTrue();

        var plaintextHash = Convert.ToBase64String(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

        encrypted.IntegrityHash.Should().NotBe(plaintextHash);

        var envelope = Encoding.UTF8.GetString(Convert.FromBase64String(encrypted.EncryptedData));
        envelope.Should().NotContain(plaintextHash);
    }

    /// <summary>
    /// Counter-check 5a — an empty plaintext must not crash. GCM is defined for
    /// it: the ciphertext is empty and the tag still authenticates.
    /// </summary>
    [Fact]
    public async Task EncryptWithKeyAsync_EmptyPlaintext_RoundTrips()
    {
        var key = CreateKey("key-empty");
        _keyManagement.GetKeyAsync("key-empty", Arg.Any<CancellationToken>()).Returns(key);

        var encrypted = await _sut.EncryptWithKeyAsync(string.Empty, "key-empty");
        encrypted.Success.Should().BeTrue(encrypted.ErrorMessage);

        var decrypted = await _sut.DecryptWithKeyAsync(encrypted.EncryptedData, "key-empty");

        decrypted.Success.Should().BeTrue(decrypted.ErrorMessage);
        decrypted.Data.Should().BeEmpty();
    }

    /// <summary>
    /// Counter-check 5b — more than a megabyte must not crash either, and must
    /// come back unchanged.
    /// </summary>
    [Fact]
    public async Task EncryptWithKeyAsync_LargePlaintext_RoundTrips()
    {
        var key = CreateKey("key-large");
        _keyManagement.GetKeyAsync("key-large", Arg.Any<CancellationToken>()).Returns(key);

        var large = new string('x', 1_500_000);

        var encrypted = await _sut.EncryptWithKeyAsync(large, "key-large");
        encrypted.Success.Should().BeTrue(encrypted.ErrorMessage);

        var decrypted = await _sut.DecryptWithKeyAsync(encrypted.EncryptedData, "key-large");

        decrypted.Success.Should().BeTrue(decrypted.ErrorMessage);
        decrypted.Data.Should().Be(large);
    }

    /// <summary>
    /// The envelope must not name an algorithm other than the one that was
    /// actually applied. Before 4.4.1, asking for ChaCha20-Poly1305 silently
    /// took the AES branch and stamped the envelope "AES256GCM"; asking for
    /// AES-256-CBC did the same through the default arm of the switch.
    /// Substituting a construction the caller chose against is the same class
    /// of defect as not encrypting at all.
    /// </summary>
    [Theory]
    [InlineData(EncryptionAlgorithm.ChaCha20Poly1305)]
    [InlineData(EncryptionAlgorithm.XChaCha20Poly1305)]
    [InlineData(EncryptionAlgorithm.AES256CBC)]
    [InlineData(EncryptionAlgorithm.AES128CBC)]
    public async Task EncryptWithKeyAsync_UnimplementedAlgorithm_IsRefusedNotSubstituted(
        EncryptionAlgorithm algorithm)
    {
        var key = CreateKey("key-alg");
        _keyManagement.GetKeyAsync("key-alg", Arg.Any<CancellationToken>()).Returns(key);

        var result = await _sut.EncryptWithKeyAsync(
            "data", "key-alg", new EncryptionOptions { Algorithm = algorithm });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain(algorithm.ToString());
    }

    /// <summary>
    /// Data written by Girder 4.4.0 and earlier is not ciphertext. 4.4.1 must
    /// refuse it rather than hand back what looks like a successful
    /// decryption, and it must say why — otherwise the operator reads
    /// "decryption failed" and goes looking for a key problem.
    /// </summary>
    [Fact]
    public async Task DecryptWithKeyAsync_PreFixEnvelope_IsRefusedWithAnExplanation()
    {
        var key = CreateKey("key-legacy");
        _keyManagement.GetKeyAsync("key-legacy", Arg.Any<CancellationToken>()).Returns(key);

        // Exactly what 4.4.0 wrote: the plaintext Base64-encoded in Data, an
        // all-zero tag, and a SHA-256 of the plaintext beside it.
        const string secret = "sk-ant-GEHEIMNIS";
        var legacy = JsonSerializer.Serialize(new
        {
            Version = "1.0",
            KeyId = "key-legacy",
            Algorithm = "AES256GCM",
            IV = Convert.ToBase64String(new byte[12]),
            AuthTag = Convert.ToBase64String(new byte[16]),
            Data = Convert.ToBase64String(Encoding.UTF8.GetBytes(secret)),
            Timestamp = DateTime.UtcNow,
            IntegrityHash = Convert.ToBase64String(
                System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(secret))),
            Metadata = new Dictionary<string, string>()
        });

        var decrypted = await _sut.DecryptWithKeyAsync(
            Convert.ToBase64String(Encoding.UTF8.GetBytes(legacy)), "key-legacy");

        decrypted.Success.Should().BeFalse();
        decrypted.Data.Should().NotContain(secret);
        decrypted.ErrorMessage.Should().Contain("4.4.0");
    }

    /// <summary>
    /// <see cref="EncryptionOptions.AdditionalData"/> has to bind the
    /// ciphertext to the context it was given. Up to 4.4.0 it was read by
    /// nothing — setting it changed neither ciphertext nor tag — so a caller
    /// who believed a value was tied to a tenant or a column name was wrong.
    /// </summary>
    [Fact]
    public async Task EncryptWithKeyAsync_AdditionalData_BindsTheCiphertextToIt()
    {
        var key = CreateKey("key-aad");
        _keyManagement.GetKeyAsync("key-aad", Arg.Any<CancellationToken>()).Returns(key);

        var options = new EncryptionOptions
        {
            AdditionalData = Encoding.UTF8.GetBytes("tenant:acme;column:ai_key")
        };

        var encrypted = await _sut.EncryptWithKeyAsync("bound to a context", "key-aad", options);
        encrypted.Success.Should().BeTrue(encrypted.ErrorMessage);

        // Unaltered, it round-trips.
        var ok = await _sut.DecryptWithKeyAsync(encrypted.EncryptedData, "key-aad");
        ok.Success.Should().BeTrue(ok.ErrorMessage);
        ok.Data.Should().Be("bound to a context");

        // Moved to another context, it does not.
        var moved = ReplaceAad(encrypted.EncryptedData, "tenant:other;column:ai_key");
        var bad = await _sut.DecryptWithKeyAsync(moved, "key-aad");
        bad.Success.Should().BeFalse();
        bad.IntegrityVerified.Should().BeFalse();
    }

    #region helpers

    private static EncryptionKey CreateKey(string keyId, int keySize = 256)
    {
        var keyMaterial = System.Security.Cryptography.RandomNumberGenerator.GetBytes(keySize / 8);

        return new EncryptionKey
        {
            Id = keyId,
            Status = KeyStatus.Active,
            KeySize = keySize,
            KeyMaterial = keyMaterial,
            KeyType = KeyType.Symmetric,
            Purpose = KeyPurpose.DataEncryption,
            Version = 1,
            ExpiresAt = DateTime.UtcNow.AddYears(1),
            UsageStatistics = new KeyUsageStatistics()
        };
    }

    /// <summary>The raw bytes of the Data field inside the envelope.</summary>
    private static byte[] CipherBytesOf(string encryptedData)
    {
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(encryptedData));
        using var doc = JsonDocument.Parse(json);
        return Convert.FromBase64String(doc.RootElement.GetProperty("Data").GetString()!);
    }

    /// <summary>Flips the low bit of the first ciphertext byte and repacks.</summary>
    private static string FlipOneBitInCiphertext(string encryptedData)
    {
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(encryptedData));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var cipher = Convert.FromBase64String(root.GetProperty("Data").GetString()!);
        cipher.Should().NotBeEmpty("there has to be a ciphertext byte to flip");
        cipher[0] ^= 0x01;

        var rebuilt = JsonSerializer.Serialize(new
        {
            Version = root.GetProperty("Version").GetString(),
            KeyId = root.GetProperty("KeyId").GetString(),
            Algorithm = root.GetProperty("Algorithm").GetString(),
            IV = root.GetProperty("IV").GetString(),
            AuthTag = root.GetProperty("AuthTag").GetString(),
            Data = Convert.ToBase64String(cipher),
            Timestamp = root.GetProperty("Timestamp").GetDateTime(),
            IntegrityHash = root.TryGetProperty("IntegrityHash", out var h) && h.ValueKind == JsonValueKind.String
                ? h.GetString()
                : null,
            Metadata = root.GetProperty("Metadata").Deserialize<Dictionary<string, string>>()
                       ?? new Dictionary<string, string>()
        });

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(rebuilt));
    }

    /// <summary>Rewrites the Aad field of an envelope, leaving all else.</summary>
    private static string ReplaceAad(string encryptedData, string newAad)
    {
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(encryptedData));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var rebuilt = JsonSerializer.Serialize(new
        {
            Version = root.GetProperty("Version").GetString(),
            KeyId = root.GetProperty("KeyId").GetString(),
            Algorithm = root.GetProperty("Algorithm").GetString(),
            IV = root.GetProperty("IV").GetString(),
            AuthTag = root.GetProperty("AuthTag").GetString(),
            Data = root.GetProperty("Data").GetString(),
            Aad = Convert.ToBase64String(Encoding.UTF8.GetBytes(newAad)),
            Timestamp = root.GetProperty("Timestamp").GetDateTime(),
            IntegrityHash = (string?)null,
            Metadata = root.GetProperty("Metadata").Deserialize<Dictionary<string, string>>()
                       ?? new Dictionary<string, string>()
        });

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(rebuilt));
    }

    /// <summary>Plain byte-subsequence search — no decoding, no interpretation.</summary>
    private static bool ContainsSubsequence(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length)
            return false;

        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
                return true;
        }

        return false;
    }

    #endregion
}
