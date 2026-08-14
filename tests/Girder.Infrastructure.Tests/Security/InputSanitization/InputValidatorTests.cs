using Infrastructure.Security.InputSanitization;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Tests.Security.InputSanitization;

[Trait("Category", "Unit")]
public class InputValidatorTests
{
    private readonly InputValidator _sut;
    private readonly ILogger<InputSanitizer> _sanitizerLogger = Substitute.For<ILogger<InputSanitizer>>();

    public InputValidatorTests()
    {
        var sanitizer = new InputSanitizer(_sanitizerLogger);
        _sut = new InputValidator(sanitizer, Enumerable.Empty<ICustomInputValidator>());
    }

    #region ValidateAsync — built-in rules

    [Fact]
    public async Task ValidateAsync_ValidInput_WithinLengthBounds_Succeeds()
    {
        var rules = new InputValidationRules { MinLength = 3, MaxLength = 20 };

        var result = await _sut.ValidateAsync("Hello", rules);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_TooShort_Fails()
    {
        var rules = new InputValidationRules { MinLength = 10 };

        var result = await _sut.ValidateAsync("Hi", rules);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_TooLong_Fails()
    {
        var rules = new InputValidationRules { MaxLength = 3 };

        var result = await _sut.ValidateAsync("TooLong", rules);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_ForbiddenPattern_Fails()
    {
        var rules = new InputValidationRules { ForbiddenPatterns = new List<string> { @"\d+" } };

        var result = await _sut.ValidateAsync("abc123", rules);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_RequiredPattern_Matches_Succeeds()
    {
        var rules = new InputValidationRules { RequiredPattern = @"^\d+$" };

        var result = await _sut.ValidateAsync("12345", rules);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_RequiredPattern_NoMatch_Fails()
    {
        var rules = new InputValidationRules { RequiredPattern = @"^\d+$" };

        var result = await _sut.ValidateAsync("abc", rules);

        result.IsValid.Should().BeFalse();
    }

    #endregion

    #region ValidateAsync<T> — custom validators

    [Fact]
    public async Task ValidateAsyncT_NoMatchingValidator_ReturnsFailure()
    {
        var result = await _sut.ValidateAsync<int>("42", "unknown-type");

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsyncT_WithMatchingValidator_InvokesItAndReturnsResult()
    {
        var customValidator = new CustomInputValidator<int>("positiveInt", input =>
            int.TryParse(input, out var v) && v > 0
                ? ValidationResult<int>.Success(v)
                : ValidationResult<int>.Failure("not a positive int"));

        var sanitizer = new InputSanitizer(_sanitizerLogger);
        var validator = new InputValidator(sanitizer, new[] { customValidator });

        var result = await validator.ValidateAsync<int>("5", "positiveInt");

        result.IsValid.Should().BeTrue();
        result.Value.Should().Be(5);
    }

    [Fact]
    public async Task ValidateAsyncT_WithMatchingValidator_InvalidInput_ReturnsFailure()
    {
        var customValidator = new CustomInputValidator<int>("positiveInt", input =>
            int.TryParse(input, out var v) && v > 0
                ? ValidationResult<int>.Success(v)
                : ValidationResult<int>.Failure("not a positive int"));

        var sanitizer = new InputSanitizer(_sanitizerLogger);
        var validator = new InputValidator(sanitizer, new[] { customValidator });

        var result = await validator.ValidateAsync<int>("-1", "positiveInt");

        result.IsValid.Should().BeFalse();
    }

    #endregion

    #region GetFieldValidationRules

    [Theory]
    [InlineData("email")]
    [InlineData("phone")]
    [InlineData("url")]
    [InlineData("password")]
    [InlineData("username")]
    [InlineData("name")]
    [InlineData("description")]
    public void GetFieldValidationRules_KnownField_ReturnsNonDefaultRules(string fieldName)
    {
        var rules = _sut.GetFieldValidationRules(fieldName);

        // At least one non-default constraint should be set
        var hasConstraint = rules.MaxLength.HasValue
            || rules.MinLength.HasValue
            || !string.IsNullOrEmpty(rules.RequiredPattern)
            || (rules.ForbiddenPatterns?.Count > 0);

        hasConstraint.Should().BeTrue($"field '{fieldName}' should have at least one validation rule");
    }

    [Fact]
    public void GetFieldValidationRules_FieldNameCaseInsensitive()
    {
        var lower = _sut.GetFieldValidationRules("email");
        var upper = _sut.GetFieldValidationRules("EMAIL");

        lower.MaxLength.Should().Be(upper.MaxLength);
    }

    [Fact]
    public void GetFieldValidationRules_UnknownField_ReturnsDefaultRules()
    {
        var rules = _sut.GetFieldValidationRules("unknownfield");

        // Returns default (no constraints): should not throw
        rules.Should().NotBeNull();
    }

    #endregion
}
