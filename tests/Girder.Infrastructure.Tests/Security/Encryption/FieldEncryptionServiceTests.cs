using System.Text;
using System.Text.Json;
using Girder.Infrastructure.Security.Encryption;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Tests.Security.Encryption;

[Trait("Category", "Unit")]
public class FieldEncryptionServiceTests
{
    private readonly FieldEncryptionService _sut;
    private readonly IDataEncryptionService _encryptionService = Substitute.For<IDataEncryptionService>();
    private readonly ILogger<FieldEncryptionService> _logger = Substitute.For<ILogger<FieldEncryptionService>>();

    public FieldEncryptionServiceTests()
    {
        _sut = new FieldEncryptionService(_encryptionService, _logger);
    }

    #region EncryptFieldsAsync

    [Fact]
    public async Task EncryptFieldsAsync_NullObject_ReturnsNull()
    {
        var result = await _sut.EncryptFieldsAsync<TestModelWithEncrypted>(null!);

        // Returns null (via null! pattern)
        result.Should().BeNull();
    }

    [Fact]
    public async Task EncryptFieldsAsync_NoEncryptedAttributes_ReturnsUnchanged()
    {
        var obj = new TestModelPlain { Name = "plain", Age = 25 };

        var result = await _sut.EncryptFieldsAsync(obj);

        result.Name.Should().Be("plain");
        result.Age.Should().Be(25);
    }

    [Fact]
    public async Task EncryptFieldsAsync_WithEncryptedProperty_CallsEncryptService()
    {
        var obj = new TestModelWithEncrypted { SecretData = "sensitive-data" };

        _encryptionService.EncryptAsync(
            Arg.Any<string>(),
            Arg.Any<EncryptionContext>(),
            Arg.Any<CancellationToken>())
            .Returns(new EncryptionResult { Success = true, EncryptedData = "encrypted-base64" });

        var result = await _sut.EncryptFieldsAsync(obj);

        result.SecretData.Should().Be("encrypted-base64");
    }

    [Fact]
    public async Task EncryptFieldsAsync_EncryptionFails_KeepsOriginalValue()
    {
        var obj = new TestModelWithEncrypted { SecretData = "sensitive-data" };

        _encryptionService.EncryptAsync(
            Arg.Any<string>(),
            Arg.Any<EncryptionContext>(),
            Arg.Any<CancellationToken>())
            .Returns(new EncryptionResult { Success = false, ErrorMessage = "Key not found" });

        var result = await _sut.EncryptFieldsAsync(obj);

        result.SecretData.Should().Be("sensitive-data");
    }

    [Fact]
    public async Task EncryptFieldsAsync_WithExcludedFields_SkipsExcluded()
    {
        var obj = new TestModelWithEncrypted { SecretData = "sensitive-data" };
        var options = new FieldEncryptionOptions
        {
            ExcludedFields = new List<string> { "SecretData" }
        };

        var result = await _sut.EncryptFieldsAsync(obj, options);

        await _encryptionService.DidNotReceive().EncryptAsync(
            Arg.Any<string>(),
            Arg.Any<EncryptionContext>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EncryptFieldsAsync_WithFieldsToEncrypt_OnlyEncryptsSpecified()
    {
        var obj = new TestModelMultiEncrypted { Field1 = "val1", Field2 = "val2" };
        var options = new FieldEncryptionOptions
        {
            FieldsToEncrypt = new List<string> { "Field1" }
        };

        _encryptionService.EncryptAsync(
            Arg.Any<string>(),
            Arg.Any<EncryptionContext>(),
            Arg.Any<CancellationToken>())
            .Returns(new EncryptionResult { Success = true, EncryptedData = "enc" });

        await _sut.EncryptFieldsAsync(obj, options);

        // Only Field1 should be encrypted
        await _encryptionService.Received(1).EncryptAsync(
            "val1",
            Arg.Any<EncryptionContext>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EncryptFieldsAsync_EmptyStringProperty_SkipsEncryption()
    {
        var obj = new TestModelWithEncrypted { SecretData = "" };

        await _sut.EncryptFieldsAsync(obj);

        await _encryptionService.DidNotReceive().EncryptAsync(
            Arg.Any<string>(),
            Arg.Any<EncryptionContext>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region DecryptFieldsAsync

    [Fact]
    public async Task DecryptFieldsAsync_NullObject_ReturnsNull()
    {
        var result = await _sut.DecryptFieldsAsync<TestModelWithEncrypted>(null!);

        result.Should().BeNull();
    }

    [Fact]
    public async Task DecryptFieldsAsync_NonEncryptedValue_SkipsDecryption()
    {
        var obj = new TestModelWithEncrypted { SecretData = "plain-text" };

        await _sut.DecryptFieldsAsync(obj);

        await _encryptionService.DidNotReceive().DecryptAsync(
            Arg.Any<string>(),
            Arg.Any<EncryptionContext>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region GetFieldEncryptionStatusAsync

    [Fact]
    public async Task GetFieldEncryptionStatusAsync_NullObject_ReturnsEmptyStatus()
    {
        var result = await _sut.GetFieldEncryptionStatusAsync<TestModelWithEncrypted>(null!);

        result.Should().NotBeNull();
        result.EncryptedFields.Should().BeEmpty();
    }

    [Fact]
    public async Task GetFieldEncryptionStatusAsync_PlainValues_ReportsUnencrypted()
    {
        var obj = new TestModelWithEncrypted { SecretData = "plain-text" };

        var result = await _sut.GetFieldEncryptionStatusAsync(obj);

        result.UnencryptedSensitiveFields.Should().Contain("SecretData");
        result.IsCompliant.Should().BeFalse();
    }

    [Fact]
    public async Task GetFieldEncryptionStatusAsync_NoSensitiveFields_IsCompliant()
    {
        var obj = new TestModelPlain { Name = "test", Age = 10 };

        var result = await _sut.GetFieldEncryptionStatusAsync(obj);

        result.IsCompliant.Should().BeTrue();
        result.EncryptionCoverage.Should().Be(100);
    }

    #endregion

    #region EncryptJsonFieldAsync

    [Fact]
    public async Task EncryptJsonFieldAsync_ValidJson_EncryptsField()
    {
        var json = "{\"name\":\"secret-value\",\"age\":25}";
        var context = new EncryptionContext();

        _encryptionService.EncryptAsync("secret-value", Arg.Any<EncryptionContext>(), Arg.Any<CancellationToken>())
            .Returns(new EncryptionResult { Success = true, EncryptedData = "encrypted-value" });

        var result = await _sut.EncryptJsonFieldAsync(json, "name", context);

        result.Should().Contain("encrypted-value");
    }

    [Fact]
    public async Task EncryptJsonFieldAsync_InvalidJson_Throws()
    {
        var act = () => _sut.EncryptJsonFieldAsync("not-json", "field", new EncryptionContext());

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task EncryptJsonFieldAsync_NonExistentField_ReturnsOriginal()
    {
        var json = "{\"name\":\"value\"}";

        var result = await _sut.EncryptJsonFieldAsync(json, "missing", new EncryptionContext());

        result.Should().Contain("\"name\"");
    }

    #endregion

    #region Test Models

    private class TestModelPlain
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
    }

    private class TestModelWithEncrypted
    {
        [Encrypted]
        public string SecretData { get; set; } = string.Empty;

        public string PublicData { get; set; } = string.Empty;
    }

    private class TestModelMultiEncrypted
    {
        [Encrypted]
        public string Field1 { get; set; } = string.Empty;

        [Encrypted]
        public string Field2 { get; set; } = string.Empty;
    }

    #endregion

    #region Coverage Tests

    [Fact]
    public async Task EncryptFieldsAsync_NullPropertyValue_SkipsEncryption()
    {
        var obj = new TestModelWithEncrypted { SecretData = null! };

        var result = await _sut.EncryptFieldsAsync(obj);

        result.SecretData.Should().BeNull();
        await _encryptionService.DidNotReceive().EncryptAsync(
            Arg.Any<string>(),
            Arg.Any<EncryptionContext>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EncryptFieldsAsync_AlreadyEncrypted_SkipsReEncryption()
    {
        var encryptedValue = BuildEncryptedValue();
        var obj = new TestModelWithEncrypted { SecretData = encryptedValue };

        var result = await _sut.EncryptFieldsAsync(obj);

        result.SecretData.Should().Be(encryptedValue);
        await _encryptionService.DidNotReceive().EncryptAsync(
            Arg.Any<string>(),
            Arg.Any<EncryptionContext>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EncryptJsonFieldAsync_SimpleField_EncryptsValue()
    {
        var json = "{\"email\":\"user@example.com\",\"name\":\"John\"}";
        var context = new EncryptionContext();

        _encryptionService.EncryptAsync(
                "user@example.com",
                Arg.Any<EncryptionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(new EncryptionResult { Success = true, EncryptedData = "enc-email" });

        var result = await _sut.EncryptJsonFieldAsync(json, "email", context);

        result.Should().Contain("enc-email");
        result.Should().Contain("John");
    }

    [Fact]
    public async Task EncryptJsonFieldAsync_FieldNotFound_ReturnsOriginalJson()
    {
        var json = "{\"name\":\"John\"}";
        var context = new EncryptionContext();

        var result = await _sut.EncryptJsonFieldAsync(json, "nonexistent", context);

        result.Should().Contain("John");
        await _encryptionService.DidNotReceive().EncryptAsync(
            Arg.Any<string>(),
            Arg.Any<EncryptionContext>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetFieldEncryptionStatusAsync_PlainTextSensitiveField_MarksNonCompliant()
    {
        var obj = new TestModelWithEncrypted { SecretData = "plain-text-not-encrypted" };

        var result = await _sut.GetFieldEncryptionStatusAsync(obj);

        result.IsCompliant.Should().BeFalse();
        result.UnencryptedSensitiveFields.Should().Contain("SecretData");
        result.ComplianceViolations.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetFieldEncryptionStatusAsync_NoAnnotatedFields_ReturnsCompliant()
    {
        var obj = new TestModelUnannotated { Data = "plain" };

        var result = await _sut.GetFieldEncryptionStatusAsync(obj);

        result.IsCompliant.Should().BeTrue();
        result.EncryptedFields.Should().BeEmpty();
        result.UnencryptedSensitiveFields.Should().BeEmpty();
    }

    [Fact]
    public async Task DecryptFieldsAsync_MultipleEncryptedFields_DecryptsAll()
    {
        var enc1 = BuildEncryptedValue();
        var enc2 = BuildEncryptedValue();
        var obj = new TestModelMultiEncrypted { Field1 = enc1, Field2 = enc2 };

        _encryptionService.DecryptAsync(
                Arg.Any<string>(),
                Arg.Any<EncryptionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(new DecryptionResult { Success = true, Data = "decrypted" });

        var result = await _sut.DecryptFieldsAsync(obj);

        result.Field1.Should().Be("decrypted");
        result.Field2.Should().Be("decrypted");
        await _encryptionService.Received(2).DecryptAsync(
            Arg.Any<string>(),
            Arg.Any<EncryptionContext>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EncryptFieldsAsync_WithAnnotatedField_CallsEncryptService()
    {
        var obj = new TestModelFieldAnnotated();
        obj.SetSecret("field-value");

        _encryptionService.EncryptAsync(
                Arg.Any<string>(),
                Arg.Any<EncryptionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(new EncryptionResult { Success = true, EncryptedData = "enc-field" });

        var result = await _sut.EncryptFieldsAsync(obj);

        result.GetSecret().Should().Be("enc-field");
    }

    [Fact]
    public async Task DecryptFieldsAsync_WithAnnotatedField_CallsDecryptService()
    {
        var encVal = BuildEncryptedValue();
        var obj = new TestModelFieldAnnotated();
        obj.SetSecret(encVal);

        _encryptionService.DecryptAsync(
                Arg.Any<string>(),
                Arg.Any<EncryptionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(new DecryptionResult { Success = true, Data = "decrypted-field" });

        var result = await _sut.DecryptFieldsAsync(obj);

        result.GetSecret().Should().Be("decrypted-field");
    }

    [Fact]
    public async Task GetFieldEncryptionStatusAsync_FieldWithPlainText_MarksNonCompliant()
    {
        var obj = new TestModelFieldAnnotated();
        obj.SetSecret("plain-text");

        var result = await _sut.GetFieldEncryptionStatusAsync(obj);

        result.IsCompliant.Should().BeFalse();
    }

    [Fact]
    public async Task GetFieldEncryptionStatusAsync_FieldWithEncryptedValue_MarksCompliant()
    {
        var obj = new TestModelFieldAnnotated();
        obj.SetSecret(BuildEncryptedValue());

        var result = await _sut.GetFieldEncryptionStatusAsync(obj);

        result.IsCompliant.Should().BeTrue();
        result.EncryptedFields.Should().NotBeEmpty();
    }

    [Fact]
    public async Task EncryptFieldsAsync_WithComplianceAnnotation_PassesContextToEncryptService()
    {
        var obj = new TestModelCompliance { Data = "sensitive" };

        _encryptionService.EncryptAsync(
                Arg.Any<string>(),
                Arg.Any<EncryptionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(new EncryptionResult { Success = true, EncryptedData = "enc" });

        await _sut.EncryptFieldsAsync(obj);

        await _encryptionService.Received(1).EncryptAsync(
            "sensitive",
            Arg.Is<EncryptionContext>(c =>
                c.Classification == DataClassification.Restricted &&
                c.GeographicRestriction == "EU"),
            Arg.Any<CancellationToken>());
    }

    private class TestModelUnannotated
    {
        public string Data { get; set; } = string.Empty;
    }

    private class TestModelFieldAnnotated
    {
        [Encrypted]
        private string _secret = string.Empty;

        public string GetSecret() => _secret;
        public void SetSecret(string value) => _secret = value;
    }

    private class TestModelCompliance
    {
        [Encrypted(
            Classification = DataClassification.Restricted,
            GeographicRestriction = "EU",
            RetentionPeriod = "365.00:00:00")]
        public string Data { get; set; } = string.Empty;
    }

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
