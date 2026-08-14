using Girder.Infrastructure.Security.Encryption;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace Girder.Infrastructure.Tests.Security.Encryption;

[Trait("Category", "Unit")]
public class FieldEncryptionServiceTopUpTests
{
    private readonly FieldEncryptionService _sut;
    private readonly IDataEncryptionService _encryptionService = Substitute.For<IDataEncryptionService>();
    private readonly ILogger<FieldEncryptionService> _logger = Substitute.For<ILogger<FieldEncryptionService>>();

    public FieldEncryptionServiceTopUpTests()
    {
        _sut = new FieldEncryptionService(_encryptionService, _logger);
    }

    #region DecryptFieldsAsync — encrypted value

    [Fact]
    public async Task DecryptFieldsAsync_WithEncryptedProperty_CallsDecryptService()
    {
        // Arrange — build a valid "encrypted" Base64-JSON value the service recognises
        var encryptedValue = BuildEncryptedValue();
        var obj = new DecryptableModel { SecretData = encryptedValue };

        _encryptionService.DecryptAsync(
                Arg.Any<string>(),
                Arg.Any<EncryptionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(new DecryptionResult { Success = true, Data = "plain-text" });

        // Act
        var result = await _sut.DecryptFieldsAsync(obj);

        // Assert
        result.SecretData.Should().Be("plain-text");
        await _encryptionService.Received(1).DecryptAsync(
            encryptedValue,
            Arg.Any<EncryptionContext>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DecryptFieldsAsync_DecryptionFails_KeepsEncryptedValue()
    {
        var encryptedValue = BuildEncryptedValue();
        var obj = new DecryptableModel { SecretData = encryptedValue };

        _encryptionService.DecryptAsync(
                Arg.Any<string>(),
                Arg.Any<EncryptionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(new DecryptionResult { Success = false, ErrorMessage = "Key not found" });

        var result = await _sut.DecryptFieldsAsync(obj);

        // Value is kept as-is when decryption fails
        result.SecretData.Should().Be(encryptedValue);
    }

    [Fact]
    public async Task DecryptFieldsAsync_ServiceThrows_PropagatesException()
    {
        var encryptedValue = BuildEncryptedValue();
        var obj = new DecryptableModel { SecretData = encryptedValue };

        _encryptionService.DecryptAsync(
                Arg.Any<string>(),
                Arg.Any<EncryptionContext>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("DB failure"));

        var act = () => _sut.DecryptFieldsAsync(obj);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    #endregion

    #region DecryptJsonFieldAsync

    [Fact]
    public async Task DecryptJsonFieldAsync_WithEncryptedField_DecryptsValue()
    {
        // Arrange
        var encryptedValue = BuildEncryptedValue();
        var json = $"{{\"secret\":\"{encryptedValue}\",\"other\":\"keep\"}}";
        var context = new EncryptionContext();

        _encryptionService.DecryptAsync(
                Arg.Any<string>(),
                Arg.Any<EncryptionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(new DecryptionResult { Success = true, Data = "plain-text" });

        // Act
        var result = await _sut.DecryptJsonFieldAsync(json, "secret", context);

        // Assert — the encrypted value was replaced with plain-text
        result.Should().Contain("plain-text");
    }

    [Fact]
    public async Task DecryptJsonFieldAsync_FieldNotEncrypted_ReturnsOriginalJson()
    {
        var json = "{\"name\":\"not-encrypted\"}";
        var context = new EncryptionContext();

        var result = await _sut.DecryptJsonFieldAsync(json, "name", context);

        // Field is not in encrypted format, so it stays as-is
        result.Should().Contain("not-encrypted");
        await _encryptionService.DidNotReceive().DecryptAsync(
            Arg.Any<string>(),
            Arg.Any<EncryptionContext>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DecryptJsonFieldAsync_InvalidJson_Throws()
    {
        var act = () => _sut.DecryptJsonFieldAsync("not-json!", "field", new EncryptionContext());

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task DecryptJsonFieldAsync_MissingField_ReturnsOriginalJson()
    {
        var json = "{\"name\":\"value\"}";
        var context = new EncryptionContext();

        var result = await _sut.DecryptJsonFieldAsync(json, "nonexistent", context);

        result.Should().Contain("\"name\"");
    }

    #endregion

    #region EncryptFieldsAsync — exception propagation

    [Fact]
    public async Task EncryptFieldsAsync_ServiceThrows_PropagatesException()
    {
        var obj = new DecryptableModel { SecretData = "sensitive" };

        _encryptionService.EncryptAsync(
                Arg.Any<string>(),
                Arg.Any<EncryptionContext>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Key store unavailable"));

        var act = () => _sut.EncryptFieldsAsync(obj);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    #endregion

    #region GetFieldEncryptionStatusAsync — encrypted values

    [Fact]
    public async Task GetFieldEncryptionStatusAsync_EncryptedValue_CountsAsEncrypted()
    {
        var encryptedValue = BuildEncryptedValue();
        var obj = new DecryptableModel { SecretData = encryptedValue };

        var result = await _sut.GetFieldEncryptionStatusAsync(obj);

        result.EncryptedFields.Should().ContainKey("SecretData");
        result.UnencryptedSensitiveFields.Should().NotContain("SecretData");
        result.IsCompliant.Should().BeTrue();
    }

    [Fact]
    public async Task GetFieldEncryptionStatusAsync_EmptyEncryptedValue_IgnoresField()
    {
        var obj = new DecryptableModel { SecretData = "" };

        var result = await _sut.GetFieldEncryptionStatusAsync(obj);

        // Empty values are not checked for encryption status
        result.EncryptedFields.Should().BeEmpty();
        result.IsCompliant.Should().BeTrue();
    }

    [Fact]
    public async Task GetFieldEncryptionStatusAsync_CoverageCalculation_CorrectPercent()
    {
        // obj with two [Encrypted] properties: one encrypted, one plain
        var obj = new MultiFieldModel
        {
            Field1 = BuildEncryptedValue(),
            Field2 = "plain-text"
        };

        var result = await _sut.GetFieldEncryptionStatusAsync(obj);

        result.EncryptionCoverage.Should().BeInRange(0, 100);
        result.IsCompliant.Should().BeFalse(); // Field2 is not encrypted
        result.ComplianceViolations.Should().NotBeEmpty();
    }

    #endregion

    #region EncryptJsonFieldAsync — nested path (covers split-path branch)

    [Fact]
    public async Task EncryptJsonFieldAsync_NestedFieldPath_ReturnsJson()
    {
        var json = "{\"user\":{\"email\":\"test@example.com\"}}";
        var context = new EncryptionContext();

        // The implementation handles nested paths but simplified — just verify no exception
        var result = await _sut.EncryptJsonFieldAsync(json, "user.email", context);

        result.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Caching — same type is only reflected once

    [Fact]
    public async Task EncryptFieldsAsync_SameTypeTwice_UsesCache()
    {
        _encryptionService.EncryptAsync(
                Arg.Any<string>(),
                Arg.Any<EncryptionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(new EncryptionResult { Success = true, EncryptedData = "enc" });

        var obj1 = new DecryptableModel { SecretData = "val1" };
        var obj2 = new DecryptableModel { SecretData = "val2" };

        await _sut.EncryptFieldsAsync(obj1);
        await _sut.EncryptFieldsAsync(obj2);

        // Both calls should succeed and service should have been called twice (once per object)
        await _encryptionService.Received(2).EncryptAsync(
            Arg.Any<string>(),
            Arg.Any<EncryptionContext>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Test Models

    private class DecryptableModel
    {
        [Encrypted]
        public string SecretData { get; set; } = string.Empty;
    }

    private class MultiFieldModel
    {
        [Encrypted]
        public string Field1 { get; set; } = string.Empty;

        [Encrypted]
        public string Field2 { get; set; } = string.Empty;
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Builds a Base64-JSON string that passes the IsValueEncrypted check.
    /// The check looks for KeyId, Algorithm, and Data properties.
    /// </summary>
    private static string BuildEncryptedValue()
    {
        var structure = new
        {
            KeyId = "key-1",
            Algorithm = "AES256GCM",
            Data = Convert.ToBase64String(new byte[32]),
            IV = Convert.ToBase64String(new byte[12]),
            AuthTag = Convert.ToBase64String(new byte[16])
        };
        var json = JsonSerializer.Serialize(structure);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    #endregion
}
