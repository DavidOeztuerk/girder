using System.ComponentModel.DataAnnotations;
using Infrastructure.Security.InputSanitization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using ValidationResult = System.ComponentModel.DataAnnotations.ValidationResult;

namespace Infrastructure.Tests.Security.InputSanitization;

[Trait("Category", "Unit")]
public class InputSanitizationAttributeTests
{
    #region SanitizeInputAttribute

    [Fact]
    public void SanitizeInputAttribute_DefaultProperties_HaveExpectedValues()
    {
        var attr = new SanitizeInputAttribute();

        attr.HtmlLevel.Should().Be(HtmlSanitizationLevel.Strict);
        attr.MaxLength.Should().Be(1000);
        attr.AllowHtml.Should().BeFalse();
        attr.RemoveLineBreaks.Should().BeFalse();
        attr.NormalizeWhitespace.Should().BeTrue();
        attr.SanitizationPattern.Should().BeNull();
    }

    [Fact]
    public void SanitizeInputAttribute_CanSetProperties()
    {
        var attr = new SanitizeInputAttribute
        {
            HtmlLevel = HtmlSanitizationLevel.Relaxed,
            MaxLength = 500,
            AllowHtml = true,
            RemoveLineBreaks = true,
            NormalizeWhitespace = false,
            SanitizationPattern = "custom"
        };

        attr.HtmlLevel.Should().Be(HtmlSanitizationLevel.Relaxed);
        attr.MaxLength.Should().Be(500);
        attr.AllowHtml.Should().BeTrue();
        attr.RemoveLineBreaks.Should().BeTrue();
        attr.NormalizeWhitespace.Should().BeFalse();
        attr.SanitizationPattern.Should().Be("custom");
    }

    [Fact]
    public void SanitizeInputAttribute_HasCorrectAttributeUsage()
    {
        var usage = typeof(SanitizeInputAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        usage.ValidOn.Should().HaveFlag(AttributeTargets.Parameter);
        usage.ValidOn.Should().HaveFlag(AttributeTargets.Property);
    }

    #endregion

    #region ValidateNoInjectionAttribute

    [Fact]
    public void ValidateNoInjection_NullValue_ReturnsSuccess()
    {
        var attr = new ValidateNoInjectionAttribute();
        var context = CreateValidationContext(null);

        var result = InvokeIsValid(attr, null, context);

        result.Should().Be(ValidationResult.Success);
    }

    [Fact]
    public void ValidateNoInjection_EmptyString_ReturnsSuccess()
    {
        var attr = new ValidateNoInjectionAttribute();
        var context = CreateValidationContext(string.Empty);

        var result = InvokeIsValid(attr, string.Empty, context);

        result.Should().Be(ValidationResult.Success);
    }

    [Fact]
    public void ValidateNoInjection_NonStringValue_ReturnsSuccess()
    {
        var attr = new ValidateNoInjectionAttribute();
        var context = CreateValidationContext(42);

        var result = InvokeIsValid(attr, 42, context);

        result.Should().Be(ValidationResult.Success);
    }

    [Fact]
    public void ValidateNoInjection_NoDetectorService_ReturnsSuccess()
    {
        var attr = new ValidateNoInjectionAttribute();
        // No IInjectionDetector registered in the service provider
        var context = CreateValidationContext("some input");

        var result = InvokeIsValid(attr, "some input", context);

        result.Should().Be(ValidationResult.Success);
    }

    [Fact]
    public void ValidateNoInjection_InjectionDetected_AboveMinRiskLevel_ReturnsError()
    {
        var detector = Substitute.For<IInjectionDetector>();
        detector.DetectInjection(Arg.Any<string>()).Returns(new InjectionDetectionResult
        {
            InjectionDetected = true,
            InjectionType = InjectionType.SqlInjection,
            RiskLevel = RiskLevel.High
        });

        var attr = new ValidateNoInjectionAttribute { MinimumRiskLevel = RiskLevel.Medium };
        var context = CreateValidationContext("DROP TABLE", injectionDetector: detector);

        var result = InvokeIsValid(attr, "DROP TABLE", context);

        result.Should().NotBe(ValidationResult.Success);
        result!.ErrorMessage.Should().Contain("Potentially malicious input detected");
        result.ErrorMessage.Should().Contain("SqlInjection");
    }

    [Fact]
    public void ValidateNoInjection_InjectionDetected_BelowMinRiskLevel_ReturnsSuccess()
    {
        var detector = Substitute.For<IInjectionDetector>();
        detector.DetectInjection(Arg.Any<string>()).Returns(new InjectionDetectionResult
        {
            InjectionDetected = true,
            InjectionType = InjectionType.SqlInjection,
            RiskLevel = RiskLevel.Low
        });

        var attr = new ValidateNoInjectionAttribute { MinimumRiskLevel = RiskLevel.Medium };
        var context = CreateValidationContext("some input", injectionDetector: detector);

        var result = InvokeIsValid(attr, "some input", context);

        result.Should().Be(ValidationResult.Success);
    }

    [Fact]
    public void ValidateNoInjection_InjectionDetected_BlockOnDetectionFalse_ReturnsSuccess()
    {
        var detector = Substitute.For<IInjectionDetector>();
        detector.DetectInjection(Arg.Any<string>()).Returns(new InjectionDetectionResult
        {
            InjectionDetected = true,
            InjectionType = InjectionType.XssInjection,
            RiskLevel = RiskLevel.Critical
        });

        var attr = new ValidateNoInjectionAttribute { BlockOnDetection = false };
        var context = CreateValidationContext("<script>alert(1)</script>", injectionDetector: detector);

        var result = InvokeIsValid(attr, "<script>alert(1)</script>", context);

        result.Should().Be(ValidationResult.Success);
    }

    [Fact]
    public void ValidateNoInjection_NoInjectionDetected_ReturnsSuccess()
    {
        var detector = Substitute.For<IInjectionDetector>();
        detector.DetectInjection(Arg.Any<string>()).Returns(new InjectionDetectionResult
        {
            InjectionDetected = false,
            RiskLevel = RiskLevel.None
        });

        var attr = new ValidateNoInjectionAttribute();
        var context = CreateValidationContext("safe input", injectionDetector: detector);

        var result = InvokeIsValid(attr, "safe input", context);

        result.Should().Be(ValidationResult.Success);
    }

    [Fact]
    public void ValidateNoInjection_DefaultProperties_HaveExpectedValues()
    {
        var attr = new ValidateNoInjectionAttribute();

        attr.BlockOnDetection.Should().BeTrue();
        attr.MinimumRiskLevel.Should().Be(RiskLevel.Medium);
    }

    [Fact]
    public void ValidateNoInjection_ErrorResult_IncludesMemberName()
    {
        var detector = Substitute.For<IInjectionDetector>();
        detector.DetectInjection(Arg.Any<string>()).Returns(new InjectionDetectionResult
        {
            InjectionDetected = true,
            InjectionType = InjectionType.CommandInjection,
            RiskLevel = RiskLevel.Critical
        });

        var attr = new ValidateNoInjectionAttribute();
        var context = CreateValidationContext("malicious", memberName: "Username", injectionDetector: detector);

        var result = InvokeIsValid(attr, "malicious", context);

        result.Should().NotBe(ValidationResult.Success);
        result!.MemberNames.Should().Contain("Username");
    }

    #endregion

    #region ValidateEmailAttribute

    [Fact]
    public void ValidateEmail_NullValue_ReturnsError()
    {
        var attr = new ValidateEmailAttribute();
        var context = CreateValidationContext(null);

        var result = InvokeIsValid(attr, null, context);

        result.Should().NotBe(ValidationResult.Success);
        result!.ErrorMessage.Should().Contain("Email address is required");
    }

    [Fact]
    public void ValidateEmail_EmptyString_ReturnsError()
    {
        var attr = new ValidateEmailAttribute();
        var context = CreateValidationContext(string.Empty);

        var result = InvokeIsValid(attr, string.Empty, context);

        result.Should().NotBe(ValidationResult.Success);
        result!.ErrorMessage.Should().Contain("Email address is required");
    }

    [Fact]
    public void ValidateEmail_NoSanitizerService_ReturnsError()
    {
        var attr = new ValidateEmailAttribute();
        var context = CreateValidationContext("test@example.com");

        var result = InvokeIsValid(attr, "test@example.com", context);

        result.Should().NotBe(ValidationResult.Success);
        result!.ErrorMessage.Should().Contain("Email validation service not available");
    }

    [Fact]
    public void ValidateEmail_ValidEmail_ReturnsSuccess()
    {
        var sanitizer = Substitute.For<IInputSanitizer>();
        sanitizer.SanitizeEmail(Arg.Any<string>()).Returns(
            new ValidationResult<string> { IsValid = true, Value = "test@example.com" });

        var attr = new ValidateEmailAttribute();
        var context = CreateValidationContext("test@example.com", inputSanitizer: sanitizer);

        var result = InvokeIsValid(attr, "test@example.com", context);

        result.Should().Be(ValidationResult.Success);
    }

    [Fact]
    public void ValidateEmail_InvalidEmail_ReturnsError()
    {
        var sanitizer = Substitute.For<IInputSanitizer>();
        sanitizer.SanitizeEmail(Arg.Any<string>()).Returns(
            new ValidationResult<string>
            {
                IsValid = false,
                Errors = new List<string> { "Invalid email format" }
            });

        var attr = new ValidateEmailAttribute();
        var context = CreateValidationContext("not-an-email", inputSanitizer: sanitizer);

        var result = InvokeIsValid(attr, "not-an-email", context);

        result.Should().NotBe(ValidationResult.Success);
        result!.ErrorMessage.Should().Contain("Invalid email format");
    }

    [Fact]
    public void ValidateEmail_InvalidEmail_IncludesMemberName()
    {
        var sanitizer = Substitute.For<IInputSanitizer>();
        sanitizer.SanitizeEmail(Arg.Any<string>()).Returns(
            new ValidationResult<string>
            {
                IsValid = false,
                Errors = new List<string> { "Bad email" }
            });

        var attr = new ValidateEmailAttribute();
        var context = CreateValidationContext("bad", memberName: "Email", inputSanitizer: sanitizer);

        var result = InvokeIsValid(attr, "bad", context);

        result.Should().NotBe(ValidationResult.Success);
        result!.MemberNames.Should().Contain("Email");
    }

    [Fact]
    public void ValidateEmail_NonStringValue_ReturnsError()
    {
        var attr = new ValidateEmailAttribute();
        var context = CreateValidationContext(42);

        var result = InvokeIsValid(attr, 42, context);

        result.Should().NotBe(ValidationResult.Success);
        result!.ErrorMessage.Should().Contain("Email address is required");
    }

    #endregion

    #region ValidatePhoneNumberAttribute

    [Fact]
    public void ValidatePhoneNumber_NullValue_ReturnsError()
    {
        var attr = new ValidatePhoneNumberAttribute();
        var context = CreateValidationContext(null);

        var result = InvokeIsValid(attr, null, context);

        result.Should().NotBe(ValidationResult.Success);
        result!.ErrorMessage.Should().Contain("Phone number is required");
    }

    [Fact]
    public void ValidatePhoneNumber_EmptyString_ReturnsError()
    {
        var attr = new ValidatePhoneNumberAttribute();
        var context = CreateValidationContext(string.Empty);

        var result = InvokeIsValid(attr, string.Empty, context);

        result.Should().NotBe(ValidationResult.Success);
        result!.ErrorMessage.Should().Contain("Phone number is required");
    }

    [Fact]
    public void ValidatePhoneNumber_NoSanitizerService_ReturnsError()
    {
        var attr = new ValidatePhoneNumberAttribute();
        var context = CreateValidationContext("+1234567890");

        var result = InvokeIsValid(attr, "+1234567890", context);

        result.Should().NotBe(ValidationResult.Success);
        result!.ErrorMessage.Should().Contain("Phone validation service not available");
    }

    [Fact]
    public void ValidatePhoneNumber_ValidPhone_ReturnsSuccess()
    {
        var sanitizer = Substitute.For<IInputSanitizer>();
        sanitizer.SanitizePhoneNumber(Arg.Any<string>()).Returns(
            new ValidationResult<string> { IsValid = true, Value = "+1234567890" });

        var attr = new ValidatePhoneNumberAttribute();
        var context = CreateValidationContext("+1234567890", inputSanitizer: sanitizer);

        var result = InvokeIsValid(attr, "+1234567890", context);

        result.Should().Be(ValidationResult.Success);
    }

    [Fact]
    public void ValidatePhoneNumber_InvalidPhone_ReturnsError()
    {
        var sanitizer = Substitute.For<IInputSanitizer>();
        sanitizer.SanitizePhoneNumber(Arg.Any<string>()).Returns(
            new ValidationResult<string>
            {
                IsValid = false,
                Errors = new List<string> { "Invalid phone number" }
            });

        var attr = new ValidatePhoneNumberAttribute();
        var context = CreateValidationContext("not-a-phone", inputSanitizer: sanitizer);

        var result = InvokeIsValid(attr, "not-a-phone", context);

        result.Should().NotBe(ValidationResult.Success);
        result!.ErrorMessage.Should().Contain("Invalid phone number");
    }

    [Fact]
    public void ValidatePhoneNumber_InvalidPhone_IncludesMemberName()
    {
        var sanitizer = Substitute.For<IInputSanitizer>();
        sanitizer.SanitizePhoneNumber(Arg.Any<string>()).Returns(
            new ValidationResult<string>
            {
                IsValid = false,
                Errors = new List<string> { "Bad phone" }
            });

        var attr = new ValidatePhoneNumberAttribute();
        var context = CreateValidationContext("bad", memberName: "Phone", inputSanitizer: sanitizer);

        var result = InvokeIsValid(attr, "bad", context);

        result.Should().NotBe(ValidationResult.Success);
        result!.MemberNames.Should().Contain("Phone");
    }

    [Fact]
    public void ValidatePhoneNumber_NonStringValue_ReturnsError()
    {
        var attr = new ValidatePhoneNumberAttribute();
        var context = CreateValidationContext(12345);

        var result = InvokeIsValid(attr, 12345, context);

        result.Should().NotBe(ValidationResult.Success);
        result!.ErrorMessage.Should().Contain("Phone number is required");
    }

    #endregion

    #region ValidateUrlAttribute

    [Fact]
    public void ValidateUrl_NullValue_ReturnsSuccess()
    {
        var attr = new ValidateUrlAttribute();
        var context = CreateValidationContext(null);

        var result = InvokeIsValid(attr, null, context);

        result.Should().Be(ValidationResult.Success);
    }

    [Fact]
    public void ValidateUrl_EmptyString_ReturnsSuccess()
    {
        var attr = new ValidateUrlAttribute();
        var context = CreateValidationContext(string.Empty);

        var result = InvokeIsValid(attr, string.Empty, context);

        result.Should().Be(ValidationResult.Success);
    }

    [Fact]
    public void ValidateUrl_NonStringValue_ReturnsSuccess()
    {
        var attr = new ValidateUrlAttribute();
        var context = CreateValidationContext(42);

        var result = InvokeIsValid(attr, 42, context);

        result.Should().Be(ValidationResult.Success);
    }

    [Fact]
    public void ValidateUrl_NoSanitizerService_ReturnsError()
    {
        var attr = new ValidateUrlAttribute();
        var context = CreateValidationContext("https://example.com");

        var result = InvokeIsValid(attr, "https://example.com", context);

        result.Should().NotBe(ValidationResult.Success);
        result!.ErrorMessage.Should().Contain("URL validation service not available");
    }

    [Fact]
    public void ValidateUrl_ValidHttpsUrl_ReturnsSuccess()
    {
        var sanitizer = Substitute.For<IInputSanitizer>();
        sanitizer.SanitizeUrl(Arg.Any<string>()).Returns("https://example.com");

        var attr = new ValidateUrlAttribute();
        var context = CreateValidationContext("https://example.com", inputSanitizer: sanitizer);

        var result = InvokeIsValid(attr, "https://example.com", context);

        result.Should().Be(ValidationResult.Success);
    }

    [Fact]
    public void ValidateUrl_ValidHttpUrl_ReturnsSuccess()
    {
        var sanitizer = Substitute.For<IInputSanitizer>();
        sanitizer.SanitizeUrl(Arg.Any<string>()).Returns("http://example.com");

        var attr = new ValidateUrlAttribute();
        var context = CreateValidationContext("http://example.com", inputSanitizer: sanitizer);

        var result = InvokeIsValid(attr, "http://example.com", context);

        result.Should().Be(ValidationResult.Success);
    }

    [Fact]
    public void ValidateUrl_SanitizerReturnsEmpty_ReturnsError()
    {
        var sanitizer = Substitute.For<IInputSanitizer>();
        sanitizer.SanitizeUrl(Arg.Any<string>()).Returns(string.Empty);

        var attr = new ValidateUrlAttribute();
        var context = CreateValidationContext("bad-url", inputSanitizer: sanitizer);

        var result = InvokeIsValid(attr, "bad-url", context);

        result.Should().NotBe(ValidationResult.Success);
        result!.ErrorMessage.Should().Contain("Invalid URL format");
    }

    [Fact]
    public void ValidateUrl_SanitizerReturnsNull_ReturnsError()
    {
        var sanitizer = Substitute.For<IInputSanitizer>();
        sanitizer.SanitizeUrl(Arg.Any<string>()).Returns((string)null!);

        var attr = new ValidateUrlAttribute();
        var context = CreateValidationContext("bad-url", inputSanitizer: sanitizer);

        var result = InvokeIsValid(attr, "bad-url", context);

        result.Should().NotBe(ValidationResult.Success);
        result!.ErrorMessage.Should().Contain("Invalid URL format");
    }

    [Fact]
    public void ValidateUrl_DisallowedScheme_ReturnsError()
    {
        var sanitizer = Substitute.For<IInputSanitizer>();
        sanitizer.SanitizeUrl(Arg.Any<string>()).Returns("ftp://example.com/file.txt");

        var attr = new ValidateUrlAttribute { AllowedSchemes = new[] { "http", "https" } };
        var context = CreateValidationContext("ftp://example.com/file.txt", inputSanitizer: sanitizer);

        var result = InvokeIsValid(attr, "ftp://example.com/file.txt", context);

        result.Should().NotBe(ValidationResult.Success);
        result!.ErrorMessage.Should().Contain("not allowed");
        result.ErrorMessage.Should().Contain("ftp");
    }

    [Fact]
    public void ValidateUrl_CustomAllowedSchemes_AcceptsAllowed()
    {
        var sanitizer = Substitute.For<IInputSanitizer>();
        sanitizer.SanitizeUrl(Arg.Any<string>()).Returns("ftp://example.com/file.txt");

        var attr = new ValidateUrlAttribute { AllowedSchemes = new[] { "ftp", "https" } };
        var context = CreateValidationContext("ftp://example.com/file.txt", inputSanitizer: sanitizer);

        var result = InvokeIsValid(attr, "ftp://example.com/file.txt", context);

        result.Should().Be(ValidationResult.Success);
    }

    [Fact]
    public void ValidateUrl_DefaultAllowedSchemes_AreHttpAndHttps()
    {
        var attr = new ValidateUrlAttribute();

        attr.AllowedSchemes.Should().BeEquivalentTo(new[] { "http", "https" });
    }

    [Fact]
    public void ValidateUrl_InvalidUrlFormat_ErrorIncludesMemberName()
    {
        var sanitizer = Substitute.For<IInputSanitizer>();
        sanitizer.SanitizeUrl(Arg.Any<string>()).Returns(string.Empty);

        var attr = new ValidateUrlAttribute();
        var context = CreateValidationContext("bad", memberName: "Website", inputSanitizer: sanitizer);

        var result = InvokeIsValid(attr, "bad", context);

        result.Should().NotBe(ValidationResult.Success);
        result!.MemberNames.Should().Contain("Website");
    }

    #endregion

    #region ValidateSafeHtmlAttribute

    [Fact]
    public void ValidateSafeHtml_NullValue_ReturnsSuccess()
    {
        var attr = new ValidateSafeHtmlAttribute();
        var context = CreateValidationContext(null);

        var result = InvokeIsValid(attr, null, context);

        result.Should().Be(ValidationResult.Success);
    }

    [Fact]
    public void ValidateSafeHtml_EmptyString_ReturnsSuccess()
    {
        var attr = new ValidateSafeHtmlAttribute();
        var context = CreateValidationContext(string.Empty);

        var result = InvokeIsValid(attr, string.Empty, context);

        result.Should().Be(ValidationResult.Success);
    }

    [Fact]
    public void ValidateSafeHtml_NonStringValue_ReturnsSuccess()
    {
        var attr = new ValidateSafeHtmlAttribute();
        var context = CreateValidationContext(42);

        var result = InvokeIsValid(attr, 42, context);

        result.Should().Be(ValidationResult.Success);
    }

    [Fact]
    public void ValidateSafeHtml_NoSanitizerService_ReturnsError()
    {
        var attr = new ValidateSafeHtmlAttribute();
        var context = CreateValidationContext("<p>Hello</p>");

        var result = InvokeIsValid(attr, "<p>Hello</p>", context);

        result.Should().NotBe(ValidationResult.Success);
        result!.ErrorMessage.Should().Contain("HTML validation service not available");
    }

    [Fact]
    public void ValidateSafeHtml_SafeHtml_ReturnsSuccess()
    {
        var sanitizer = Substitute.For<IInputSanitizer>();
        sanitizer.DetectInjectionAttempt(Arg.Any<string>()).Returns(new InjectionDetectionResult
        {
            InjectionDetected = false,
            RiskLevel = RiskLevel.None
        });
        sanitizer.SanitizeHtml(Arg.Any<string>(), Arg.Any<HtmlSanitizationLevel>()).Returns("<p>Hello</p>");

        var attr = new ValidateSafeHtmlAttribute();
        var context = CreateValidationContext("<p>Hello</p>", inputSanitizer: sanitizer);

        var result = InvokeIsValid(attr, "<p>Hello</p>", context);

        result.Should().Be(ValidationResult.Success);
    }

    [Fact]
    public void ValidateSafeHtml_InjectionDetected_MediumOrHigher_ReturnsError()
    {
        var sanitizer = Substitute.For<IInputSanitizer>();
        sanitizer.DetectInjectionAttempt(Arg.Any<string>()).Returns(new InjectionDetectionResult
        {
            InjectionDetected = true,
            InjectionType = InjectionType.XssInjection,
            RiskLevel = RiskLevel.High
        });

        var attr = new ValidateSafeHtmlAttribute();
        var context = CreateValidationContext("<script>alert(1)</script>", inputSanitizer: sanitizer);

        var result = InvokeIsValid(attr, "<script>alert(1)</script>", context);

        result.Should().NotBe(ValidationResult.Success);
        result!.ErrorMessage.Should().Contain("Potentially malicious HTML detected");
        result.ErrorMessage.Should().Contain("XssInjection");
    }

    [Fact]
    public void ValidateSafeHtml_InjectionDetected_BelowMedium_ContinuesValidation()
    {
        var sanitizer = Substitute.For<IInputSanitizer>();
        sanitizer.DetectInjectionAttempt(Arg.Any<string>()).Returns(new InjectionDetectionResult
        {
            InjectionDetected = true,
            InjectionType = InjectionType.XssInjection,
            RiskLevel = RiskLevel.Low
        });
        sanitizer.SanitizeHtml(Arg.Any<string>(), Arg.Any<HtmlSanitizationLevel>()).Returns("<p>ok</p>");

        var attr = new ValidateSafeHtmlAttribute();
        var context = CreateValidationContext("<p>ok</p>", inputSanitizer: sanitizer);

        var result = InvokeIsValid(attr, "<p>ok</p>", context);

        result.Should().Be(ValidationResult.Success);
    }

    [Fact]
    public void ValidateSafeHtml_ContentExceedsMaxLength_ReturnsError()
    {
        var longHtml = new string('a', 200);
        var sanitizer = Substitute.For<IInputSanitizer>();
        sanitizer.DetectInjectionAttempt(Arg.Any<string>()).Returns(new InjectionDetectionResult
        {
            InjectionDetected = false,
            RiskLevel = RiskLevel.None
        });
        sanitizer.SanitizeHtml(Arg.Any<string>(), Arg.Any<HtmlSanitizationLevel>()).Returns(longHtml);

        var attr = new ValidateSafeHtmlAttribute { MaxLength = 100 };
        var context = CreateValidationContext(longHtml, inputSanitizer: sanitizer);

        var result = InvokeIsValid(attr, longHtml, context);

        result.Should().NotBe(ValidationResult.Success);
        result!.ErrorMessage.Should().Contain("HTML content too long");
        result.ErrorMessage.Should().Contain("100");
    }

    [Fact]
    public void ValidateSafeHtml_ContentWithinMaxLength_ReturnsSuccess()
    {
        var html = "<p>Short</p>";
        var sanitizer = Substitute.For<IInputSanitizer>();
        sanitizer.DetectInjectionAttempt(Arg.Any<string>()).Returns(new InjectionDetectionResult
        {
            InjectionDetected = false,
            RiskLevel = RiskLevel.None
        });
        sanitizer.SanitizeHtml(Arg.Any<string>(), Arg.Any<HtmlSanitizationLevel>()).Returns(html);

        var attr = new ValidateSafeHtmlAttribute { MaxLength = 10000 };
        var context = CreateValidationContext(html, inputSanitizer: sanitizer);

        var result = InvokeIsValid(attr, html, context);

        result.Should().Be(ValidationResult.Success);
    }

    [Fact]
    public void ValidateSafeHtml_DefaultProperties_HaveExpectedValues()
    {
        var attr = new ValidateSafeHtmlAttribute();

        attr.Level.Should().Be(HtmlSanitizationLevel.Basic);
        attr.MaxLength.Should().Be(10000);
    }

    [Fact]
    public void ValidateSafeHtml_InjectionError_IncludesMemberName()
    {
        var sanitizer = Substitute.For<IInputSanitizer>();
        sanitizer.DetectInjectionAttempt(Arg.Any<string>()).Returns(new InjectionDetectionResult
        {
            InjectionDetected = true,
            InjectionType = InjectionType.ScriptInjection,
            RiskLevel = RiskLevel.Critical
        });

        var attr = new ValidateSafeHtmlAttribute();
        var context = CreateValidationContext("<script>x</script>", memberName: "Content", inputSanitizer: sanitizer);

        var result = InvokeIsValid(attr, "<script>x</script>", context);

        result.Should().NotBe(ValidationResult.Success);
        result!.MemberNames.Should().Contain("Content");
    }

    #endregion

    #region ValidateFilePathAttribute

    [Fact]
    public void ValidateFilePath_NullValue_ReturnsSuccess()
    {
        var attr = new ValidateFilePathAttribute();
        var context = CreateValidationContext(null);

        var result = InvokeIsValid(attr, null, context);

        result.Should().Be(ValidationResult.Success);
    }

    [Fact]
    public void ValidateFilePath_EmptyString_ReturnsSuccess()
    {
        var attr = new ValidateFilePathAttribute();
        var context = CreateValidationContext(string.Empty);

        var result = InvokeIsValid(attr, string.Empty, context);

        result.Should().Be(ValidationResult.Success);
    }

    [Fact]
    public void ValidateFilePath_NonStringValue_ReturnsSuccess()
    {
        var attr = new ValidateFilePathAttribute();
        var context = CreateValidationContext(42);

        var result = InvokeIsValid(attr, 42, context);

        result.Should().Be(ValidationResult.Success);
    }

    [Fact]
    public void ValidateFilePath_NoSanitizerService_ReturnsError()
    {
        var attr = new ValidateFilePathAttribute();
        var context = CreateValidationContext("/some/path.txt");

        var result = InvokeIsValid(attr, "/some/path.txt", context);

        result.Should().NotBe(ValidationResult.Success);
        result!.ErrorMessage.Should().Contain("File path validation service not available");
    }

    [Fact]
    public void ValidateFilePath_PathTraversalDetected_ReturnsError()
    {
        var sanitizer = Substitute.For<IInputSanitizer>();
        sanitizer.DetectInjectionAttempt(Arg.Any<string>()).Returns(new InjectionDetectionResult
        {
            InjectionDetected = true,
            InjectionType = InjectionType.PathTraversal,
            RiskLevel = RiskLevel.High
        });

        var attr = new ValidateFilePathAttribute();
        var context = CreateValidationContext("../../etc/passwd", inputSanitizer: sanitizer);

        var result = InvokeIsValid(attr, "../../etc/passwd", context);

        result.Should().NotBe(ValidationResult.Success);
        result!.ErrorMessage.Should().Contain("Path traversal attempt detected");
    }

    [Fact]
    public void ValidateFilePath_NonPathTraversalInjection_ContinuesValidation()
    {
        var sanitizer = Substitute.For<IInputSanitizer>();
        sanitizer.DetectInjectionAttempt(Arg.Any<string>()).Returns(new InjectionDetectionResult
        {
            InjectionDetected = true,
            InjectionType = InjectionType.SqlInjection,
            RiskLevel = RiskLevel.High
        });
        sanitizer.SanitizeFilePath(Arg.Any<string>()).Returns("/safe/path.txt");

        var attr = new ValidateFilePathAttribute();
        var context = CreateValidationContext("/safe/path.txt", inputSanitizer: sanitizer);

        var result = InvokeIsValid(attr, "/safe/path.txt", context);

        result.Should().Be(ValidationResult.Success);
    }

    [Fact]
    public void ValidateFilePath_SanitizedPathEmpty_ReturnsError()
    {
        var sanitizer = Substitute.For<IInputSanitizer>();
        sanitizer.DetectInjectionAttempt(Arg.Any<string>()).Returns(new InjectionDetectionResult
        {
            InjectionDetected = false
        });
        sanitizer.SanitizeFilePath(Arg.Any<string>()).Returns(string.Empty);

        var attr = new ValidateFilePathAttribute();
        var context = CreateValidationContext("invalid", inputSanitizer: sanitizer);

        var result = InvokeIsValid(attr, "invalid", context);

        result.Should().NotBe(ValidationResult.Success);
        result!.ErrorMessage.Should().Contain("Invalid file path");
    }

    [Fact]
    public void ValidateFilePath_RelativePathWhenNotAllowed_ReturnsError()
    {
        var sanitizer = Substitute.For<IInputSanitizer>();
        sanitizer.DetectInjectionAttempt(Arg.Any<string>()).Returns(new InjectionDetectionResult
        {
            InjectionDetected = false
        });
        sanitizer.SanitizeFilePath(Arg.Any<string>()).Returns("relative/path.txt");

        var attr = new ValidateFilePathAttribute { AllowRelativePaths = false };
        var context = CreateValidationContext("relative/path.txt", inputSanitizer: sanitizer);

        var result = InvokeIsValid(attr, "relative/path.txt", context);

        result.Should().NotBe(ValidationResult.Success);
        result!.ErrorMessage.Should().Contain("Relative paths are not allowed");
    }

    [Fact]
    public void ValidateFilePath_RelativePathWhenAllowed_ReturnsSuccess()
    {
        var sanitizer = Substitute.For<IInputSanitizer>();
        sanitizer.DetectInjectionAttempt(Arg.Any<string>()).Returns(new InjectionDetectionResult
        {
            InjectionDetected = false
        });
        sanitizer.SanitizeFilePath(Arg.Any<string>()).Returns("relative/path.txt");

        var attr = new ValidateFilePathAttribute { AllowRelativePaths = true };
        var context = CreateValidationContext("relative/path.txt", inputSanitizer: sanitizer);

        var result = InvokeIsValid(attr, "relative/path.txt", context);

        result.Should().Be(ValidationResult.Success);
    }

    [Fact]
    public void ValidateFilePath_ForbiddenExtension_ReturnsError()
    {
        var sanitizer = Substitute.For<IInputSanitizer>();
        sanitizer.DetectInjectionAttempt(Arg.Any<string>()).Returns(new InjectionDetectionResult
        {
            InjectionDetected = false
        });
        sanitizer.SanitizeFilePath(Arg.Any<string>()).Returns("/path/to/file.exe");

        var attr = new ValidateFilePathAttribute();
        var context = CreateValidationContext("/path/to/file.exe", inputSanitizer: sanitizer);

        var result = InvokeIsValid(attr, "/path/to/file.exe", context);

        result.Should().NotBe(ValidationResult.Success);
        result!.ErrorMessage.Should().Contain(".exe");
        result.ErrorMessage.Should().Contain("not allowed");
    }

    [Theory]
    [InlineData(".bat")]
    [InlineData(".cmd")]
    [InlineData(".com")]
    [InlineData(".scr")]
    [InlineData(".vbs")]
    [InlineData(".js")]
    public void ValidateFilePath_AllDefaultForbiddenExtensions_ReturnsError(string extension)
    {
        var sanitizer = Substitute.For<IInputSanitizer>();
        sanitizer.DetectInjectionAttempt(Arg.Any<string>()).Returns(new InjectionDetectionResult
        {
            InjectionDetected = false
        });
        var filePath = $"/path/to/file{extension}";
        sanitizer.SanitizeFilePath(Arg.Any<string>()).Returns(filePath);

        var attr = new ValidateFilePathAttribute();
        var context = CreateValidationContext(filePath, inputSanitizer: sanitizer);

        var result = InvokeIsValid(attr, filePath, context);

        result.Should().NotBe(ValidationResult.Success);
        result!.ErrorMessage.Should().Contain(extension);
    }

    [Fact]
    public void ValidateFilePath_AllowedExtensionsSet_RejectsOtherExtensions()
    {
        var sanitizer = Substitute.For<IInputSanitizer>();
        sanitizer.DetectInjectionAttempt(Arg.Any<string>()).Returns(new InjectionDetectionResult
        {
            InjectionDetected = false
        });
        sanitizer.SanitizeFilePath(Arg.Any<string>()).Returns("/path/to/file.pdf");

        var attr = new ValidateFilePathAttribute
        {
            AllowedExtensions = new[] { ".jpg", ".png" },
            ForbiddenExtensions = Array.Empty<string>()
        };
        var context = CreateValidationContext("/path/to/file.pdf", inputSanitizer: sanitizer);

        var result = InvokeIsValid(attr, "/path/to/file.pdf", context);

        result.Should().NotBe(ValidationResult.Success);
        result!.ErrorMessage.Should().Contain(".pdf");
        result.ErrorMessage.Should().Contain("not in the allowed list");
    }

    [Fact]
    public void ValidateFilePath_AllowedExtensionsSet_AcceptsAllowed()
    {
        var sanitizer = Substitute.For<IInputSanitizer>();
        sanitizer.DetectInjectionAttempt(Arg.Any<string>()).Returns(new InjectionDetectionResult
        {
            InjectionDetected = false
        });
        sanitizer.SanitizeFilePath(Arg.Any<string>()).Returns("/path/to/image.jpg");

        var attr = new ValidateFilePathAttribute
        {
            AllowedExtensions = new[] { ".jpg", ".png" },
            ForbiddenExtensions = Array.Empty<string>()
        };
        var context = CreateValidationContext("/path/to/image.jpg", inputSanitizer: sanitizer);

        var result = InvokeIsValid(attr, "/path/to/image.jpg", context);

        result.Should().Be(ValidationResult.Success);
    }

    [Fact]
    public void ValidateFilePath_ValidPath_NoRestrictions_ReturnsSuccess()
    {
        var sanitizer = Substitute.For<IInputSanitizer>();
        sanitizer.DetectInjectionAttempt(Arg.Any<string>()).Returns(new InjectionDetectionResult
        {
            InjectionDetected = false
        });
        sanitizer.SanitizeFilePath(Arg.Any<string>()).Returns("/safe/document.txt");

        var attr = new ValidateFilePathAttribute();
        var context = CreateValidationContext("/safe/document.txt", inputSanitizer: sanitizer);

        var result = InvokeIsValid(attr, "/safe/document.txt", context);

        result.Should().Be(ValidationResult.Success);
    }

    [Fact]
    public void ValidateFilePath_DefaultProperties_HaveExpectedValues()
    {
        var attr = new ValidateFilePathAttribute();

        attr.AllowRelativePaths.Should().BeTrue();
        attr.AllowedExtensions.Should().BeNull();
        attr.ForbiddenExtensions.Should().BeEquivalentTo(
            new[] { ".exe", ".bat", ".cmd", ".com", ".scr", ".vbs", ".js" });
    }

    [Fact]
    public void ValidateFilePath_PathTraversalError_IncludesMemberName()
    {
        var sanitizer = Substitute.For<IInputSanitizer>();
        sanitizer.DetectInjectionAttempt(Arg.Any<string>()).Returns(new InjectionDetectionResult
        {
            InjectionDetected = true,
            InjectionType = InjectionType.PathTraversal,
            RiskLevel = RiskLevel.High
        });

        var attr = new ValidateFilePathAttribute();
        var context = CreateValidationContext("../../etc/passwd", memberName: "FilePath", inputSanitizer: sanitizer);

        var result = InvokeIsValid(attr, "../../etc/passwd", context);

        result.Should().NotBe(ValidationResult.Success);
        result!.MemberNames.Should().Contain("FilePath");
    }

    #endregion

    #region SanitizingModelBinder

    [Fact]
    public void SanitizingModelBinder_ImplementsIModelBinder()
    {
        var sanitizer = Substitute.For<IInputSanitizer>();
        var binder = new SanitizingModelBinder(sanitizer);

        binder.Should().BeAssignableTo<IModelBinder>();
    }

    #endregion

    #region SanitizingModelBinderProvider

    [Fact]
    public void SanitizingModelBinderProvider_ImplementsIModelBinderProvider()
    {
        var provider = new SanitizingModelBinderProvider();

        provider.Should().BeAssignableTo<IModelBinderProvider>();
    }

    #endregion

    #region SanitizeInputActionFilter

    [Fact]
    public void SanitizeInputActionFilter_IsActionFilterAttribute()
    {
        var attribute = new SanitizeInputAttribute();
        var filter = new SanitizeInputActionFilter(attribute);

        filter.Should().BeAssignableTo<Microsoft.AspNetCore.Mvc.Filters.ActionFilterAttribute>();
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Creates a ValidationContext with optional service registrations.
    /// </summary>
    private static System.ComponentModel.DataAnnotations.ValidationContext CreateValidationContext(
        object? instance,
        string? memberName = null,
        IInputSanitizer? inputSanitizer = null,
        IInjectionDetector? injectionDetector = null)
    {
        var serviceCollection = new Microsoft.Extensions.DependencyInjection.ServiceCollection();

        if (inputSanitizer != null)
        {
            serviceCollection.AddSingleton(inputSanitizer);
        }

        if (injectionDetector != null)
        {
            serviceCollection.AddSingleton(injectionDetector);
        }

        var serviceProvider = serviceCollection.BuildServiceProvider();

        var context = new System.ComponentModel.DataAnnotations.ValidationContext(
            instance ?? new object(),
            serviceProvider,
            items: null);

        if (memberName != null)
        {
            context.MemberName = memberName;
        }

        return context;
    }

    /// <summary>
    /// Invokes the protected IsValid method on a validation attribute via the public Validate API.
    /// Returns ValidationResult.Success if valid, or the error ValidationResult if invalid.
    /// </summary>
    private static ValidationResult? InvokeIsValid(
        System.ComponentModel.DataAnnotations.ValidationAttribute attribute,
        object? value,
        System.ComponentModel.DataAnnotations.ValidationContext context)
    {
        try
        {
            var result = attribute.GetValidationResult(value, context);
            return result;
        }
        catch (System.ComponentModel.DataAnnotations.ValidationException ex)
        {
            return ex.ValidationResult;
        }
    }

    #endregion
}
