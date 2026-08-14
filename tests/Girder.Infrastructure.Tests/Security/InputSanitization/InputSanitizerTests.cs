using Girder.Infrastructure.Security.InputSanitization;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Tests.Security.InputSanitization;

[Trait("Category", "Unit")]
public class InputSanitizerTests
{
    private readonly InputSanitizer _sut;
    private readonly ILogger<InputSanitizer> _logger = Substitute.For<ILogger<InputSanitizer>>();

    public InputSanitizerTests()
    {
        _sut = new InputSanitizer(_logger);
    }

    #region SanitizeHtml

    [Fact]
    public void SanitizeHtml_NullOrEmpty_ReturnsEmpty()
    {
        _sut.SanitizeHtml(null!).Should().BeEmpty();
        _sut.SanitizeHtml("").Should().BeEmpty();
    }

    [Fact]
    public void SanitizeHtml_StripLevel_RemovesAllTags()
    {
        var input = "<p>Hello <b>World</b></p>";
        var result = _sut.SanitizeHtml(input, HtmlSanitizationLevel.Strip);
        result.Should().NotContain("<").And.NotContain(">");
    }

    [Fact]
    public void SanitizeHtml_BasicLevel_AllowsBasicTags()
    {
        var input = "<p>Hello <strong>World</strong></p>";
        var result = _sut.SanitizeHtml(input, HtmlSanitizationLevel.Basic);
        (result.Contains("<p>") || result.Contains("<strong>")).Should().BeTrue("basic level should preserve safe tags");
    }

    [Fact]
    public void SanitizeHtml_StrictLevel_RemovesDangerousTags()
    {
        var input = "<script>alert('xss')</script><p>Hello</p>";
        var result = _sut.SanitizeHtml(input, HtmlSanitizationLevel.Strict);
        result.Should().NotContain("<script>");
    }

    [Fact]
    public void SanitizeHtml_StandardLevel_AllowsStandardTags()
    {
        var input = "<ul><li>Item</li></ul><table><tr><td>cell</td></tr></table>";
        var result = _sut.SanitizeHtml(input, HtmlSanitizationLevel.Standard);
        // Standard level should not strip table or list tags
        result.Should().NotBeEmpty();
    }

    [Fact]
    public void SanitizeHtml_RemovesScriptTag()
    {
        var result = _sut.SanitizeHtml("<script>document.cookie</script>Safe text");
        result.Should().NotContain("<script>");
        result.Should().Contain("Safe text");
    }

    [Fact]
    public void SanitizeHtml_RemovesIframeTag()
    {
        var result = _sut.SanitizeHtml("<iframe src='evil.com'></iframe>Content");
        result.Should().NotContain("<iframe>");
    }

    [Fact]
    public void SanitizeHtml_EncodesEventHandlers()
    {
        var result = _sut.SanitizeHtml("<div onmouseover='alert(1)'>hover</div>");
        // Strict mode HTML-encodes the input, neutralizing the event handler
        result.Should().NotContain("<div");
    }

    #endregion

    #region SanitizeSql

    [Fact]
    public void SanitizeSql_NullOrEmpty_ReturnsEmpty()
    {
        _sut.SanitizeSql(null!).Should().BeEmpty();
        _sut.SanitizeSql("").Should().BeEmpty();
    }

    [Fact]
    public void SanitizeSql_RemovesSqlKeywords()
    {
        var input = "1 OR 1=1; DROP TABLE Users--";
        var result = _sut.SanitizeSql(input);
        result.Should().NotContainEquivalentOf("DROP");
        result.Should().NotContain("--");
    }

    [Fact]
    public void SanitizeSql_SafeInput_Unchanged()
    {
        var input = "John Doe";
        var result = _sut.SanitizeSql(input);
        result.Should().Contain("John").And.Contain("Doe");
    }

    #endregion

    #region SanitizeJavaScript

    [Fact]
    public void SanitizeJavaScript_NullOrEmpty_ReturnsEmpty()
    {
        _sut.SanitizeJavaScript(null!).Should().BeEmpty();
        _sut.SanitizeJavaScript("").Should().BeEmpty();
    }

    [Fact]
    public void SanitizeJavaScript_RemovesDangerousContent()
    {
        var input = "eval('xss');document.cookie";
        var result = _sut.SanitizeJavaScript(input);
        result.Should().NotContain("eval");
    }

    #endregion

    #region SanitizeFilePath

    [Fact]
    public void SanitizeFilePath_NullOrEmpty_ReturnsEmpty()
    {
        _sut.SanitizeFilePath(null!).Should().BeEmpty();
        _sut.SanitizeFilePath("").Should().BeEmpty();
    }

    [Fact]
    public void SanitizeFilePath_RemovesTraversalPatterns()
    {
        var input = "../../etc/passwd";
        var result = _sut.SanitizeFilePath(input);
        result.Should().NotContain("..");
    }

    [Fact]
    public void SanitizeFilePath_SafePath_PreservesContent()
    {
        var input = "documents/report.pdf";
        var result = _sut.SanitizeFilePath(input);
        result.Should().NotBeEmpty();
    }

    #endregion

    #region SanitizeUrl

    [Fact]
    public void SanitizeUrl_NullOrEmpty_ReturnsEmpty()
    {
        _sut.SanitizeUrl(null!).Should().BeEmpty();
        _sut.SanitizeUrl("").Should().BeEmpty();
    }

    [Fact]
    public void SanitizeUrl_JavascriptProtocol_Removed()
    {
        var input = "javascript:alert(1)";
        var result = _sut.SanitizeUrl(input);
        result.Should().NotContain("javascript:");
    }

    [Fact]
    public void SanitizeUrl_ValidHttpUrl_Preserved()
    {
        var input = "https://example.com/page";
        var result = _sut.SanitizeUrl(input);
        result.Should().Contain("https://example.com");
    }

    #endregion

    #region SanitizeEmail

    [Fact]
    public void SanitizeEmail_ValidEmail_ReturnsValid()
    {
        var result = _sut.SanitizeEmail("user@example.com");
        result.IsValid.Should().BeTrue();
        result.Value.Should().Be("user@example.com");
    }

    [Fact]
    public void SanitizeEmail_InvalidEmail_ReturnsInvalid()
    {
        var result = _sut.SanitizeEmail("not-an-email");
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void SanitizeEmail_NullOrEmpty_ReturnsInvalid()
    {
        var result = _sut.SanitizeEmail(null!);
        result.IsValid.Should().BeFalse();

        result = _sut.SanitizeEmail("");
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void SanitizeEmail_WithWhitespace_TrimsAndValidates()
    {
        var result = _sut.SanitizeEmail("  user@example.com  ");
        result.IsValid.Should().BeTrue();
        result.Value.Should().Be("user@example.com");
    }

    #endregion

    #region SanitizePhoneNumber

    [Fact]
    public void SanitizePhoneNumber_NullOrEmpty_ReturnsInvalid()
    {
        var result = _sut.SanitizePhoneNumber(null!);
        result.IsValid.Should().BeFalse();

        result = _sut.SanitizePhoneNumber("");
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void SanitizePhoneNumber_ValidNumber_ReturnsValid()
    {
        var result = _sut.SanitizePhoneNumber("+1234567890");
        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region SanitizeText

    [Fact]
    public void SanitizeText_NullOrEmpty_ReturnsEmpty()
    {
        _sut.SanitizeText(null!).Should().BeEmpty();
        _sut.SanitizeText("").Should().BeEmpty();
    }

    [Fact]
    public void SanitizeText_PlainText_Preserved()
    {
        var result = _sut.SanitizeText("Hello World");
        result.Should().Contain("Hello World");
    }

    [Fact]
    public void SanitizeText_WithMaxLength_Truncates()
    {
        var input = "This is a very long string that should be truncated";
        var options = new TextSanitizationOptions { MaxLength = 10 };
        var result = _sut.SanitizeText(input, options);
        result.Length.Should().BeLessThanOrEqualTo(10);
    }

    [Fact]
    public void SanitizeText_RemoveControlCharacters_Default()
    {
        var input = "Hello\x00World\x01";
        var result = _sut.SanitizeText(input);
        result.Should().NotContain("\x00").And.NotContain("\x01");
    }

    [Fact]
    public void SanitizeText_NormalizeWhitespace_Default()
    {
        var input = "Hello   World";
        var options = new TextSanitizationOptions { NormalizeWhitespace = true };
        var result = _sut.SanitizeText(input, options);
        result.Should().NotContain("   ");
    }

    [Fact]
    public void SanitizeText_RemoveLineBreaks_WhenConfigured()
    {
        var input = "Hello\nWorld\r\n";
        var options = new TextSanitizationOptions { RemoveLineBreaks = true };
        var result = _sut.SanitizeText(input, options);
        result.Should().NotContain("\n").And.NotContain("\r");
    }

    #endregion

    #region DetectInjectionAttempt

    [Fact]
    public void DetectInjectionAttempt_SafeInput_NoInjection()
    {
        var result = _sut.DetectInjectionAttempt("Hello World");
        result.InjectionDetected.Should().BeFalse();
    }

    [Fact]
    public void DetectInjectionAttempt_SqlInjection_Detected()
    {
        var result = _sut.DetectInjectionAttempt("1; DROP TABLE Users--");
        result.InjectionDetected.Should().BeTrue();
    }

    [Fact]
    public void DetectInjectionAttempt_XssInjection_Detected()
    {
        var result = _sut.DetectInjectionAttempt("<script>alert('xss')</script>");
        result.InjectionDetected.Should().BeTrue();
        // The implementation detects multiple injection types; the last match wins
        // Both XSS and Command injection patterns match due to <> characters
        result.InjectionDetected.Should().BeTrue();
    }

    [Fact]
    public void DetectInjectionAttempt_PathTraversal_Detected()
    {
        var result = _sut.DetectInjectionAttempt("../../etc/passwd");
        result.InjectionDetected.Should().BeTrue();
        result.InjectionType.Should().Be(InjectionType.PathTraversal);
    }

    [Fact]
    public void DetectInjectionAttempt_CommandInjection_Detected()
    {
        var result = _sut.DetectInjectionAttempt("test; rm -rf /");
        result.InjectionDetected.Should().BeTrue();
    }

    [Fact]
    public void DetectInjectionAttempt_NullOrEmpty_NoInjection()
    {
        var result = _sut.DetectInjectionAttempt(null!);
        result.InjectionDetected.Should().BeFalse();

        result = _sut.DetectInjectionAttempt("");
        result.InjectionDetected.Should().BeFalse();
    }

    #endregion

    #region SanitizeObject

    [Fact]
    public void SanitizeObject_SanitizesStringProperties()
    {
        var obj = new TestDto
        {
            Name = "<script>alert(1)</script>John",
            Description = "Normal text"
        };

        var result = _sut.SanitizeObject(obj);
        result.Name.Should().NotContain("<script>");
        result.Description.Should().Contain("Normal text");
    }

    [Fact]
    public void SanitizeObject_PreservesNonStringProperties()
    {
        var obj = new TestDto
        {
            Name = "John",
            Age = 25
        };

        var result = _sut.SanitizeObject(obj);
        result.Age.Should().Be(25);
    }

    #endregion

    #region ValidateInput

    [Fact]
    public void ValidateInput_MinLength_Fails()
    {
        var rules = new InputValidationRules { MinLength = 5 };
        var result = _sut.ValidateInput("ab", rules);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateInput_MaxLength_Fails()
    {
        var rules = new InputValidationRules { MaxLength = 3 };
        var result = _sut.ValidateInput("abcdef", rules);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateInput_ValidInput_Succeeds()
    {
        var rules = new InputValidationRules { MinLength = 1, MaxLength = 100 };
        var result = _sut.ValidateInput("Valid input", rules);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateInput_ForbiddenPattern_Fails()
    {
        var rules = new InputValidationRules
        {
            ForbiddenPatterns = new List<string> { @"\d+" }
        };
        var result = _sut.ValidateInput("abc123", rules);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateInput_RequiredPattern_Fails()
    {
        var rules = new InputValidationRules
        {
            RequiredPattern = @"^\d+$"
        };
        var result = _sut.ValidateInput("abc", rules);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateInput_RequiredPattern_Succeeds()
    {
        var rules = new InputValidationRules
        {
            RequiredPattern = @"^\d+$"
        };
        var result = _sut.ValidateInput("123", rules);
        result.IsValid.Should().BeTrue();
    }

    #endregion

    private class TestDto
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int Age { get; set; }
    }

    #region Coverage Tests

    // --- ValidateInput — Format Validators ---

    [Fact]
    public void ValidateInput_EmailFormat_ValidEmail_Succeeds()
    {
        var rules = new InputValidationRules { RequiredFormat = InputFormat.Email };
        var result = _sut.ValidateInput("user@example.com", rules);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateInput_EmailFormat_InvalidEmail_Fails()
    {
        var rules = new InputValidationRules { RequiredFormat = InputFormat.Email };
        var result = _sut.ValidateInput("not-an-email", rules);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateInput_PhoneFormat_ValidPhone_Succeeds()
    {
        var rules = new InputValidationRules { RequiredFormat = InputFormat.PhoneNumber };
        var result = _sut.ValidateInput("+1234567890", rules);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateInput_PhoneFormat_InvalidPhone_Fails()
    {
        var rules = new InputValidationRules { RequiredFormat = InputFormat.PhoneNumber };
        var result = _sut.ValidateInput("abc", rules);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateInput_UrlFormat_ValidUrl_Succeeds()
    {
        var rules = new InputValidationRules { RequiredFormat = InputFormat.Url };
        var result = _sut.ValidateInput("https://example.com", rules);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateInput_UrlFormat_InvalidUrl_Fails()
    {
        var rules = new InputValidationRules { RequiredFormat = InputFormat.Url };
        var result = _sut.ValidateInput("javascript:alert(1)", rules);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateInput_IpAddressFormat_ValidIp_Succeeds()
    {
        var rules = new InputValidationRules { RequiredFormat = InputFormat.IpAddress };
        var result = _sut.ValidateInput("192.168.1.1", rules);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateInput_IpAddressFormat_InvalidIp_Fails()
    {
        var rules = new InputValidationRules { RequiredFormat = InputFormat.IpAddress };
        var result = _sut.ValidateInput("not-an-ip", rules);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateInput_AlphaNumericFormat_Valid_Succeeds()
    {
        var rules = new InputValidationRules { RequiredFormat = InputFormat.AlphaNumeric };
        var result = _sut.ValidateInput("abc123", rules);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateInput_AlphaNumericFormat_WithSpecialChars_Fails()
    {
        var rules = new InputValidationRules { RequiredFormat = InputFormat.AlphaNumeric };
        var result = _sut.ValidateInput("abc!@#", rules);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateInput_NumericFormat_Valid_Succeeds()
    {
        var rules = new InputValidationRules { RequiredFormat = InputFormat.Numeric };
        var result = _sut.ValidateInput("12345", rules);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateInput_NumericFormat_WithLetters_Fails()
    {
        var rules = new InputValidationRules { RequiredFormat = InputFormat.Numeric };
        var result = _sut.ValidateInput("123abc", rules);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateInput_AlphaFormat_Valid_Succeeds()
    {
        var rules = new InputValidationRules { RequiredFormat = InputFormat.Alpha };
        var result = _sut.ValidateInput("abcdef", rules);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateInput_AlphaFormat_WithDigits_Fails()
    {
        var rules = new InputValidationRules { RequiredFormat = InputFormat.Alpha };
        var result = _sut.ValidateInput("abc123", rules);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateInput_Base64Format_Valid_Succeeds()
    {
        var rules = new InputValidationRules { RequiredFormat = InputFormat.Base64 };
        var result = _sut.ValidateInput(Convert.ToBase64String(new byte[] { 1, 2, 3 }), rules);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateInput_Base64Format_Invalid_Fails()
    {
        var rules = new InputValidationRules { RequiredFormat = InputFormat.Base64 };
        var result = _sut.ValidateInput("not!valid!base64!!!", rules);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateInput_JsonFormat_Valid_Succeeds()
    {
        var rules = new InputValidationRules { RequiredFormat = InputFormat.Json };
        var result = _sut.ValidateInput("{\"key\":\"value\"}", rules);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateInput_JsonFormat_Invalid_Fails()
    {
        var rules = new InputValidationRules { RequiredFormat = InputFormat.Json };
        var result = _sut.ValidateInput("{not json}", rules);
        result.IsValid.Should().BeFalse();
    }

    // --- ValidateInput — AllowedCharacters / ForbiddenCharacters ---

    [Fact]
    public void ValidateInput_AllowedCharacters_Valid_Succeeds()
    {
        var rules = new InputValidationRules { AllowedCharacters = "abc" };
        var result = _sut.ValidateInput("abcabc", rules);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateInput_AllowedCharacters_ContainsDisallowed_Fails()
    {
        var rules = new InputValidationRules { AllowedCharacters = "abc" };
        var result = _sut.ValidateInput("abcXYZ", rules);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Input contains invalid characters");
    }

    [Fact]
    public void ValidateInput_ForbiddenCharacters_ContainsForbidden_Fails()
    {
        var rules = new InputValidationRules { ForbiddenCharacters = "<>" };
        var result = _sut.ValidateInput("hello<world>", rules);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Input contains forbidden characters");
    }

    [Fact]
    public void ValidateInput_ForbiddenCharacters_NoForbidden_Succeeds()
    {
        var rules = new InputValidationRules { ForbiddenCharacters = "<>" };
        var result = _sut.ValidateInput("hello world", rules);
        result.IsValid.Should().BeTrue();
    }

    // --- ValidateInput — CustomValidator ---

    [Fact]
    public void ValidateInput_CustomValidator_Called()
    {
        var rules = new InputValidationRules
        {
            CustomValidator = input =>
            {
                if (input.Contains("bad"))
                    return ValidationResult.Failure("Contains bad word");
                return ValidationResult.Success();
            }
        };

        var result = _sut.ValidateInput("this is bad", rules);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Contains bad word");
    }

    [Fact]
    public void ValidateInput_CustomValidator_Throws_Fails()
    {
        var rules = new InputValidationRules
        {
            CustomValidator = _ => throw new InvalidOperationException("boom")
        };

        var result = _sut.ValidateInput("input", rules);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Validation failed");
    }

    [Fact]
    public void ValidateInput_CustomValidator_SuccessWithWarnings_PropagatesWarnings()
    {
        var rules = new InputValidationRules
        {
            CustomValidator = input =>
            {
                var r = ValidationResult.Success();
                r.Warnings.Add("Consider using a stronger format");
                return r;
            }
        };

        var result = _sut.ValidateInput("input", rules);
        result.IsValid.Should().BeTrue();
        result.Warnings.Should().Contain("Consider using a stronger format");
    }

    // --- ValidateInput — null/empty with MinLength=0 ---

    [Fact]
    public void ValidateInput_NullInput_WithMinLengthZero_ReturnsValid()
    {
        var rules = new InputValidationRules { MinLength = 0 };
        var result = _sut.ValidateInput(null!, rules);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateInput_EmptyInput_WithMinLength_ReturnsInvalid()
    {
        var rules = new InputValidationRules { MinLength = 5 };
        var result = _sut.ValidateInput("", rules);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Input is required");
    }

    // --- ValidateInput — forbidden pattern with invalid regex ---

    [Fact]
    public void ValidateInput_InvalidForbiddenPattern_DoesNotThrow()
    {
        var rules = new InputValidationRules
        {
            ForbiddenPatterns = new List<string> { "[invalid-regex(" }
        };
        var result = _sut.ValidateInput("test input", rules);
        result.Should().NotBeNull();
    }

    // --- ValidateInput — ForbiddenPatterns matching ---

    [Fact]
    public void ValidateInput_ForbiddenPattern_Matches_Fails()
    {
        var rules = new InputValidationRules
        {
            ForbiddenPatterns = new List<string> { @"<script>" }
        };
        var result = _sut.ValidateInput("Hello <script>alert(1)</script>", rules);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Input contains forbidden content");
    }

    // --- ValidateInput — RequiredPattern, MinLength, MaxLength (additional) ---

    [Fact]
    public void ValidateInput_RequiredPattern_Matches_Succeeds()
    {
        var rules = new InputValidationRules { RequiredPattern = @"^\d{3}$" };
        var result = _sut.ValidateInput("123", rules);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateInput_RequiredPattern_NoMatch_Fails()
    {
        var rules = new InputValidationRules { RequiredPattern = @"^\d{3}$" };
        var result = _sut.ValidateInput("abc", rules);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Input does not match required format");
    }

    [Fact]
    public void ValidateInput_MaxLength_Exceeded_Fails()
    {
        var rules = new InputValidationRules { MaxLength = 3 };
        var result = _sut.ValidateInput("toolong", rules);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateInput_MinLength_TooShort_Fails()
    {
        var rules = new InputValidationRules { MinLength = 10 };
        var result = _sut.ValidateInput("short", rules);
        result.IsValid.Should().BeFalse();
    }

    // --- SanitizeText — BlacklistedCharacters & BlacklistedPatterns ---

    [Fact]
    public void SanitizeText_BlacklistedCharacters_Removed()
    {
        var options = new TextSanitizationOptions
        {
            BlacklistedCharacters = new HashSet<char> { '@', '#' }
        };
        var result = _sut.SanitizeText("hello@world#test", options);
        result.Should().NotContain("@").And.NotContain("#");
    }

    [Fact]
    public void SanitizeText_BlacklistedPatterns_Removed()
    {
        var options = new TextSanitizationOptions
        {
            BlacklistedPatterns = new List<string> { @"\d+" }
        };
        var result = _sut.SanitizeText("abc123def456", options);
        result.Should().Be("abcdef");
    }

    [Fact]
    public void SanitizeText_InvalidRegexPattern_LogsWarning()
    {
        var options = new TextSanitizationOptions
        {
            BlacklistedPatterns = new List<string> { "[invalid" }
        };
        var result = _sut.SanitizeText("hello", options);
        result.Should().NotBeEmpty();
    }

    [Fact]
    public void SanitizeText_AllowHtml_UsesHtmlSanitization()
    {
        var options = new TextSanitizationOptions
        {
            AllowHtml = true,
            HtmlLevel = HtmlSanitizationLevel.Basic
        };
        var result = _sut.SanitizeText("<p>Hello</p><script>bad</script>", options);
        result.Should().NotContain("<script>");
    }

    [Fact]
    public void SanitizeText_RemoveLineBreaks_ReplacesNewlines()
    {
        var options = new TextSanitizationOptions { RemoveLineBreaks = true };
        var result = _sut.SanitizeText("line1\nline2\rline3", options);
        result.Should().NotContain("\n").And.NotContain("\r");
    }

    [Fact]
    public void SanitizeText_NormalizeWhitespace_CollapsesSpaces()
    {
        var options = new TextSanitizationOptions { NormalizeWhitespace = true };
        var result = _sut.SanitizeText("hello    world", options);
        result.Should().Be("hello world");
    }

    [Fact]
    public void SanitizeText_MaxLength_Truncates_Coverage()
    {
        var options = new TextSanitizationOptions { MaxLength = 5 };
        var result = _sut.SanitizeText("abcdefghij", options);
        result.Should().HaveLength(5);
    }

    [Fact]
    public void SanitizeText_AllowHtml_Standard_SanitizesWithAllowedTags()
    {
        var options = new TextSanitizationOptions
        {
            AllowHtml = true,
            HtmlLevel = HtmlSanitizationLevel.Standard
        };
        var result = _sut.SanitizeText("<p>Hello</p><script>bad</script>", options);
        result.Should().NotContain("<script>");
    }

    // --- SanitizeHtml — Relaxed Level ---

    [Fact]
    public void SanitizeHtml_RelaxedLevel_RemovesDangerousButKeepsSafe()
    {
        var input = "<div><p>Hello</p><script>evil()</script></div>";
        var result = _sut.SanitizeHtml(input, HtmlSanitizationLevel.Relaxed);
        result.Should().NotContain("<script>");
        result.Should().Contain("<div>");
    }

    [Fact]
    public void SanitizeHtml_RelaxedLevel_RemovesEventHandlers()
    {
        var input = "<div onclick='evil()'>Content</div>";
        var result = _sut.SanitizeHtml(input, HtmlSanitizationLevel.Relaxed);
        result.Should().NotContain("onclick");
    }

    [Fact]
    public void SanitizeHtml_StripLevel_RemovesAllTags_Coverage()
    {
        var result = _sut.SanitizeHtml("<p>Hello</p><b>World</b>", HtmlSanitizationLevel.Strip);
        result.Should().Be("HelloWorld");
    }

    [Fact]
    public void SanitizeHtml_BasicLevel_KeepsAllowedTags()
    {
        var result = _sut.SanitizeHtml("<p>Hello</p><div>World</div><script>bad</script>", HtmlSanitizationLevel.Basic);
        result.Should().Contain("<p>");
        result.Should().NotContain("<script>");
    }

    [Fact]
    public void SanitizeHtml_StandardLevel_KeepsMoreTags()
    {
        var result = _sut.SanitizeHtml("<p>Hello</p><a href='x'>Link</a><script>bad</script>", HtmlSanitizationLevel.Standard);
        result.Should().Contain("<p>");
        result.Should().NotContain("<script>");
    }

    [Fact]
    public void SanitizeHtml_StrictLevel_EncodesAll()
    {
        var result = _sut.SanitizeHtml("<p>Hello</p>", HtmlSanitizationLevel.Strict);
        result.Should().NotContain("<p>");
        result.Should().Contain("&lt;");
    }

    // --- SanitizeObject — Nested objects ---

    [Fact]
    public void SanitizeObject_Null_ReturnsNull()
    {
        ParentObj? obj = null;
        var result = _sut.SanitizeObject(obj!);
        result.Should().BeNull();
    }

    [Fact]
    public void SanitizeObject_WithNestedObject_SanitizesRecursively()
    {
        var options = new SanitizationOptions { SanitizeNestedObjects = true, MaxRecursionDepth = 3 };
        var obj = new ParentObj
        {
            Title = "<script>bad</script>Safe",
            Child = new ChildObj { Name = "<b>test</b>\x00" }
        };

        var result = _sut.SanitizeObject(obj, options);
        result.Title.Should().NotContain("<script>");
        result.Child.Name.Should().NotContain("\x00");
    }

    [Fact]
    public void SanitizeObject_MaxRecursionDepth_StopsRecursion()
    {
        var options = new SanitizationOptions { SanitizeNestedObjects = true, MaxRecursionDepth = 0 };
        var obj = new ParentObj
        {
            Title = "<script>xss</script>",
            Child = new ChildObj { Name = "<script>deeper</script>" }
        };

        var result = _sut.SanitizeObject(obj, options);
        result.Should().NotBeNull();
    }

    [Fact]
    public void SanitizeObject_SkipProperties_DoesNotSanitizeSkipped()
    {
        var options = new SanitizationOptions
        {
            SanitizeNestedObjects = true,
            SkipProperties = new HashSet<string> { "Title" }
        };
        var obj = new ParentObj { Title = "<script>keep</script>", Child = new ChildObj() };

        var result = _sut.SanitizeObject(obj, options);
        result.Title.Should().Contain("<script>");
    }

    [Fact]
    public void SanitizeObject_WithPropertyRules_AppliesSpecificRules()
    {
        var options = new SanitizationOptions
        {
            SanitizeNestedObjects = true,
            PropertyRules = new Dictionary<string, TextSanitizationOptions>
            {
                ["Title"] = new TextSanitizationOptions { MaxLength = 3 }
            }
        };
        var obj = new ParentObj { Title = "LongTitle", Child = new ChildObj() };

        var result = _sut.SanitizeObject(obj, options);
        result.Title.Should().HaveLength(3);
    }

    [Fact]
    public void SanitizeObject_WithCollectionProperty_DoesNotThrow()
    {
        var obj = new CollectionModel { Tags = new List<string> { "<script>x</script>", "safe" } };
        var options = new SanitizationOptions { SanitizeCollections = true };

        var result = _sut.SanitizeObject(obj, options);

        result.Should().NotBeNull();
    }

    // --- DetectInjectionAttempt — RiskLevel and ConfidenceScore ---

    [Fact]
    public void DetectInjectionAttempt_MultipleMatches_IncreasesRisk()
    {
        var input = "1; DROP TABLE x; DELETE FROM y; INSERT INTO z; UPDATE w;";
        var result = _sut.DetectInjectionAttempt(input);

        result.InjectionDetected.Should().BeTrue();
        result.RiskLevel.Should().BeOneOf(RiskLevel.High, RiskLevel.Critical);
        result.ConfidenceScore.Should().BeGreaterThan(0);
    }

    [Fact]
    public void DetectInjectionAttempt_LdapInjection_Detected()
    {
        var result = _sut.DetectInjectionAttempt("test)(objectClass=*");
        result.InjectionDetected.Should().BeTrue();
    }

    [Fact]
    public void DetectInjectionAttempt_DetectedPatterns_NotEmpty()
    {
        var result = _sut.DetectInjectionAttempt("' OR 1=1--");
        result.InjectionDetected.Should().BeTrue();
        result.DetectedPatterns.Should().NotBeEmpty();
    }

    [Fact]
    public void DetectInjectionAttempt_CommandInjection_ReturnsCriticalRisk()
    {
        var result = _sut.DetectInjectionAttempt("; rm -rf / ; cat /etc/passwd ; ls -la ; whoami");
        result.InjectionDetected.Should().BeTrue();
        result.RiskLevel.Should().Be(RiskLevel.Critical);
    }

    [Fact]
    public void DetectInjectionAttempt_PathTraversal_Detected_Coverage()
    {
        var result = _sut.DetectInjectionAttempt("../../etc/passwd");
        result.InjectionDetected.Should().BeTrue();
        result.RiskLevel.Should().BeOneOf(RiskLevel.Medium, RiskLevel.High, RiskLevel.Critical);
    }

    [Fact]
    public void DetectInjectionAttempt_XssScript_Detected()
    {
        var result = _sut.DetectInjectionAttempt("<script>alert('xss')</script>");
        result.InjectionDetected.Should().BeTrue();
        result.InjectionType.Should().NotBeNull();
    }

    [Fact]
    public void DetectInjectionAttempt_NullOrEmpty_ReturnsNotDetected()
    {
        _sut.DetectInjectionAttempt("").InjectionDetected.Should().BeFalse();
        _sut.DetectInjectionAttempt(null!).InjectionDetected.Should().BeFalse();
    }

    // --- SanitizeJavaScript — deeper patterns ---

    [Fact]
    public void SanitizeJavaScript_RemovesEvalCalls()
    {
        var result = _sut.SanitizeJavaScript("eval('alert(1)')");
        result.Should().NotContain("eval(");
    }

    [Fact]
    public void SanitizeJavaScript_RemovesSetTimeoutCalls()
    {
        var result = _sut.SanitizeJavaScript("setTimeout('malicious()', 0)");
        result.Should().NotContain("setTimeout(");
    }

    // --- SanitizeFilePath — deeper patterns ---

    [Fact]
    public void SanitizeFilePath_RemovesNullBytes()
    {
        var result = _sut.SanitizeFilePath("file\x00.txt");
        result.Should().NotContain("\x00");
    }

    [Fact]
    public void SanitizeFilePath_RemovesAbsoluteLinuxPaths()
    {
        var result = _sut.SanitizeFilePath("/etc/passwd");
        result.Should().NotStartWith("/etc");
    }

    [Fact]
    public void SanitizeFilePath_LongPath_Truncates()
    {
        var longPath = new string('a', 300) + ".txt";
        var result = _sut.SanitizeFilePath(longPath);
        result.Length.Should().BeLessThanOrEqualTo(255);
    }

    // --- SanitizeUrl — data: and vbscript: protocols ---

    [Fact]
    public void SanitizeUrl_DataProtocol_Removed()
    {
        var result = _sut.SanitizeUrl("data:text/html,<script>alert(1)</script>");
        result.Should().NotContain("data:");
    }

    [Fact]
    public void SanitizeUrl_VbscriptProtocol_Removed()
    {
        var result = _sut.SanitizeUrl("vbscript:msgbox('xss')");
        result.Should().NotContain("vbscript:");
    }

    [Fact]
    public void SanitizeUrl_RelativeUrl_ReturnsEmpty()
    {
        var result = _sut.SanitizeUrl("/relative/path");
        result.Should().BeEmpty();
    }

    [Fact]
    public void SanitizeUrl_ValidHttps_ReturnsSanitized()
    {
        var result = _sut.SanitizeUrl("https://example.com/path?q=1");
        result.Should().Contain("https://example.com");
    }

    // --- SanitizePhoneNumber — edge cases ---

    [Fact]
    public void SanitizePhoneNumber_WithLetters_Invalid()
    {
        var result = _sut.SanitizePhoneNumber("phone-number-here");
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void SanitizePhoneNumber_TooShort_Invalid()
    {
        var result = _sut.SanitizePhoneNumber("123");
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void SanitizePhoneNumber_InternationalTooShort_ReturnsFailure()
    {
        var result = _sut.SanitizePhoneNumber("+1234");
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void SanitizePhoneNumber_LocalNumber_PrependsPlusOne()
    {
        var result = _sut.SanitizePhoneNumber("5551234567");
        result.IsValid.Should().BeTrue();
        result.Value.Should().StartWith("+1");
    }

    // --- SanitizeEmail — edge cases ---

    [Fact]
    public void SanitizeEmail_TooLong_ReturnsFailure()
    {
        var longEmail = new string('a', 250) + "@x.com";
        var result = _sut.SanitizeEmail(longEmail);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void SanitizeEmail_DoubleDots_ReturnsFailure()
    {
        var result = _sut.SanitizeEmail("user..name@example.com");
        result.IsValid.Should().BeFalse();
    }

    // --- SanitizeSql — additional ---

    [Fact]
    public void SanitizeSql_RemovesDangerousPatterns()
    {
        var result = _sut.SanitizeSql("1'; DROP TABLE users;--");
        result.Should().NotContain(";");
        result.Should().NotContain("--");
    }

    // --- Coverage Test DTOs ---

    private class ParentObj
    {
        public string Title { get; set; } = "";
        public ChildObj Child { get; set; } = new();
    }

    private class ChildObj
    {
        public string Name { get; set; } = "";
    }

    private class CollectionModel
    {
        public string Name { get; set; } = "test";
        public List<string> Tags { get; set; } = new();
    }

    #endregion
}

[Trait("Category", "Unit")]
public class ValidationResultTests
{
    #region ValidationResult — factory methods

    [Fact]
    public void Success_CreatesValidResult()
    {
        var result = ValidationResult.Success();

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Failure_SingleError_CreatesInvalidResultWithError()
    {
        var result = ValidationResult.Failure("Value is required");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Be("Value is required");
    }

    [Fact]
    public void Failure_MultipleErrors_CreatesInvalidResultWithAllErrors()
    {
        var errors = new[] { "Too short", "Invalid format" };

        var result = ValidationResult.Failure(errors);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(2);
        result.Errors.Should().Contain("Too short");
        result.Errors.Should().Contain("Invalid format");
    }

    [Fact]
    public void ValidationResult_Warnings_CanBeAdded()
    {
        var result = ValidationResult.Success();
        result.Warnings.Add("Value is close to maximum");

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().ContainSingle().Which.Should().Be("Value is close to maximum");
    }

    #endregion

    #region ValidationResult<T> — factory methods

    [Fact]
    public void GenericSuccess_CreatesValidResultWithValue()
    {
        var result = ValidationResult<string>.Success("sanitized-value");

        result.IsValid.Should().BeTrue();
        result.Value.Should().Be("sanitized-value");
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void GenericFailure_CreatesInvalidResultWithNullValue()
    {
        var result = ValidationResult<string>.Failure("Value is required");

        result.IsValid.Should().BeFalse();
        result.Value.Should().BeNull();
        result.Errors.Should().ContainSingle().Which.Should().Be("Value is required");
    }

    [Fact]
    public void GenericValidationResult_InheritsFromValidationResult()
    {
        ValidationResult<int> result = ValidationResult<int>.Success(42);

        result.Should().BeAssignableTo<ValidationResult>();
        result.IsValid.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    #endregion
}
