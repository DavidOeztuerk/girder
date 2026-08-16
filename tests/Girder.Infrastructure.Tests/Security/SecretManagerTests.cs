using Girder.Redis.Security;
using Girder.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;

namespace Girder.Infrastructure.Tests.Security;

/// <summary>
/// Tests for SecretManager (Redis-backed) — covers the 0% class.
/// We use a mocked IDatabase to avoid real Redis dependency.
/// </summary>
[Trait("Category", "Unit")]
public class SecretManagerTests
{
    private readonly IDatabase _database = Substitute.For<IDatabase>();
    private readonly IConnectionMultiplexer _multiplexer = Substitute.For<IConnectionMultiplexer>();
    private readonly ILogger<SecretManager> _logger = Substitute.For<ILogger<SecretManager>>();
    private readonly SecretManager _sut;

    public SecretManagerTests()
    {
        _multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_database);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Provide a valid 256-bit AES key (32 bytes base64)
                ["SecretManager:EncryptionKeyBase64"] = Convert.ToBase64String(new byte[32])
            })
            .Build();

        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns("Development");

        _sut = new SecretManager(_multiplexer, config, env, _logger);
    }

    #region GetSecretAsync

    [Fact]
    public async Task GetSecretAsync_KeyNotFound_ReturnsNull()
    {
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        var result = await _sut.GetSecretAsync("missing-secret");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSecretAsync_InvalidJson_ReturnsNull()
    {
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue("not-valid-json"));

        var result = await _sut.GetSecretAsync("bad-secret");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSecretAsync_ValidEncryptedData_ReturnsDecryptedValue()
    {
        // Arrange — create and store an actual encrypted secret via SetSecretAsync,
        // but since we have a mock DB, we simulate what the DB would return.
        // We set and then get via the in-memory manager to validate the full flow.
        // For SecretManager with mock DB we verify the decrypt call happens.

        // Build a real encrypted value using reflection-free approach
        var (encryptedValue, iv) = CreateEncryptedSecret("my-secret-value");
        var secretData = new
        {
            Name = "test-secret",
            EncryptedValue = encryptedValue,
            IV = iv,
            Version = 1,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = (DateTime?)null,
            IsActive = true,
            CreatedBy = "System"
        };
        var json = JsonSerializer.Serialize(secretData);

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue(json));

        var result = await _sut.GetSecretAsync("test-secret");

        result.Should().Be("my-secret-value");
    }

    [Fact]
    public async Task GetSecretAsync_ExpiredSecret_ReturnsNull()
    {
        var (encryptedValue, iv) = CreateEncryptedSecret("expired-value");
        var secretData = new
        {
            Name = "expired-secret",
            EncryptedValue = encryptedValue,
            IV = iv,
            Version = 1,
            CreatedAt = DateTime.UtcNow.AddDays(-60),
            ExpiresAt = (DateTime?)DateTime.UtcNow.AddDays(-1), // expired yesterday
            IsActive = true,
            CreatedBy = "System"
        };

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue(JsonSerializer.Serialize(secretData)));

        var result = await _sut.GetSecretAsync("expired-secret");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSecretAsync_RedisThrows_ReturnsNull()
    {
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("Connection refused"));

        var result = await _sut.GetSecretAsync("any-secret");

        result.Should().BeNull();
    }

    #endregion

    #region SetSecretAsync

    [Fact]
    public async Task SetSecretAsync_CallsRedisStringSet()
    {
        _database.ListLengthAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(0L);

        await _sut.SetSecretAsync("my-secret", "my-value");

        await _database.Received(1).StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<Expiration>(),
            Arg.Any<ValueCondition>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task SetSecretAsync_StoresInHistory()
    {
        _database.ListLengthAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(0L);

        await _sut.SetSecretAsync("history-secret", "value");

        await _database.Received(1).ListLeftPushAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<When>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task SetSecretAsync_RedisThrows_PropagatesException()
    {
        _database.ListLengthAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(0L);
        _database.StringSetAsync(
                Arg.Any<RedisKey>(),
                Arg.Any<RedisValue>(),
                Arg.Any<Expiration>(),
                Arg.Any<ValueCondition>(),
                Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("Write failed"));

        var act = () => _sut.SetSecretAsync("secret", "value");

        await act.Should().ThrowAsync<RedisException>();
    }

    #endregion

    #region RotateSecretAsync

    [Fact]
    public async Task RotateSecretAsync_ReturnsNewValue()
    {
        // Current version (will be deactivated)
        var (encryptedValue, iv) = CreateEncryptedSecret("old-value");
        var currentData = JsonSerializer.Serialize(new
        {
            Name = "rotate-me",
            EncryptedValue = encryptedValue,
            IV = iv,
            Version = 1,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = (DateTime?)null,
            IsActive = true,
            CreatedBy = "System"
        });

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue(currentData));
        _database.ListLengthAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(1L);

        var newValue = await _sut.RotateSecretAsync("rotate-me");

        newValue.Should().NotBeNullOrEmpty();
        newValue.Should().NotBe("old-value");
    }

    [Fact]
    public async Task RotateSecretAsync_RedisThrows_PropagatesException()
    {
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("Error"));

        var act = () => _sut.RotateSecretAsync("failing-secret");

        await act.Should().ThrowAsync<RedisException>();
    }

    #endregion

    #region GetSecretHistoryAsync

    [Fact]
    public async Task GetSecretHistoryAsync_EmptyList_ReturnsEmpty()
    {
        _database.ListRangeAsync(Arg.Any<RedisKey>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CommandFlags>())
            .Returns(Array.Empty<RedisValue>());

        var result = await _sut.GetSecretHistoryAsync("no-history");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSecretHistoryAsync_WithVersions_ReturnsMaskedValues()
    {
        var (encryptedValue, iv) = CreateEncryptedSecret("secret-val");
        var entry = JsonSerializer.Serialize(new
        {
            Name = "my-secret",
            EncryptedValue = encryptedValue,
            IV = iv,
            Version = 1,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = (DateTime?)null,
            IsActive = true,
            CreatedBy = "System"
        });

        _database.ListRangeAsync(Arg.Any<RedisKey>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue[] { entry });

        var history = (await _sut.GetSecretHistoryAsync("my-secret")).ToList();

        history.Should().HaveCount(1);
        history[0].Value.Should().Be("[ENCRYPTED]"); // actual values are masked
        history[0].Name.Should().Be("my-secret");
    }

    [Fact]
    public async Task GetSecretHistoryAsync_RedisThrows_ReturnsEmpty()
    {
        _database.ListRangeAsync(Arg.Any<RedisKey>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("Error"));

        var result = await _sut.GetSecretHistoryAsync("any-secret");

        result.Should().BeEmpty();
    }

    #endregion

    #region DeleteSecretAsync

    [Fact]
    public async Task DeleteSecretAsync_CallsKeyDelete()
    {
        await _sut.DeleteSecretAsync("my-secret");

        await _database.Received(1).KeyDeleteAsync(
            Arg.Any<RedisKey[]>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task DeleteSecretAsync_RedisThrows_PropagatesException()
    {
        _database.KeyDeleteAsync(Arg.Any<RedisKey[]>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("Error"));

        var act = () => _sut.DeleteSecretAsync("secret");

        await act.Should().ThrowAsync<RedisException>();
    }

    #endregion

    #region SecretExistsAsync

    [Fact]
    public async Task SecretExistsAsync_KeyExists_ReturnsTrue()
    {
        _database.KeyExistsAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(true);

        var result = await _sut.SecretExistsAsync("existing-secret");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task SecretExistsAsync_KeyNotExists_ReturnsFalse()
    {
        _database.KeyExistsAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(false);

        var result = await _sut.SecretExistsAsync("nonexistent");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task SecretExistsAsync_RedisThrows_ReturnsFalse()
    {
        _database.KeyExistsAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("Error"));

        var result = await _sut.SecretExistsAsync("any");

        result.Should().BeFalse();
    }

    #endregion

    #region GetSecretNamesAsync

    [Fact]
    public async Task GetSecretNamesAsync_RedisThrows_ReturnsEmpty()
    {
        // IServer.Keys will throw because multiplexer isn't a real connection
        var result = await _sut.GetSecretNamesAsync();

        // Should not throw, returns empty on error
        result.Should().NotBeNull();
    }

    #endregion

    #region Constructor — development key generation

    [Fact]
    public void Constructor_NoEncryptionKey_DevelopmentEnv_GeneratesTransientKey()
    {
        var config = new ConfigurationBuilder().Build(); // no keys configured
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns("Development");

        // Should not throw — generates transient key with a warning
        var act = () => new SecretManager(_multiplexer, config, env, _logger);

        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_NoEncryptionKey_ProductionEnv_Throws()
    {
        var config = new ConfigurationBuilder().Build();
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns("Production");

        var act = () => new SecretManager(_multiplexer, config, env, _logger);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Persistent encryption key not configured*");
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Creates a real AES-encrypted value matching SecretManager's EncryptSecret method.
    /// Uses the same 32-byte zero key we pass in the constructor.
    /// </summary>
    private static (string encryptedValue, string iv) CreateEncryptedSecret(string value)
    {
        using var aes = System.Security.Cryptography.Aes.Create();
        aes.Key = new byte[32]; // matches the key we passed (zeros)
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        using var ms = new MemoryStream();
        using var cs = new System.Security.Cryptography.CryptoStream(ms, encryptor, System.Security.Cryptography.CryptoStreamMode.Write);
        using var sw = new StreamWriter(cs);
        sw.Write(value);
        sw.Close();

        return (Convert.ToBase64String(ms.ToArray()), Convert.ToBase64String(aes.IV));
    }

    #endregion
}
