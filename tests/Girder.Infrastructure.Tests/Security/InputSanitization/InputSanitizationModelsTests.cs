using Infrastructure.Security.InputSanitization;

namespace Infrastructure.Tests.Security.InputSanitization;

[Trait("Category", "Unit")]
public class InputSanitizationModelsTests
{
    #region InputSanitizationOptions defaults

    [Fact]
    public void InputSanitizationOptions_Defaults()
    {
        var options = new InputSanitizationOptions();

        options.EnableInputSanitization.Should().BeTrue();
        options.BlockOnInjectionDetection.Should().BeTrue();
        options.BlockOnSanitizationError.Should().BeFalse();
        options.BlockOnInvalidInput.Should().BeFalse();
        options.LogInjectionAttempts.Should().BeTrue();
        options.LogSensitiveData.Should().BeFalse();
        options.IncludeInjectionDetailsInResponse.Should().BeFalse();
        options.AllowHtmlInTextFields.Should().BeFalse();
        options.MaxRequestBodySize.Should().Be(10 * 1024 * 1024);
        options.MaxTextFieldLength.Should().Be(10000);
        options.ExcludedPaths.Should().NotBeEmpty();
        options.ExcludedContentTypes.Should().NotBeEmpty();
    }

    [Fact]
    public void InputSanitizationOptions_Setters_Work()
    {
        var options = new InputSanitizationOptions
        {
            EnableInputSanitization = false,
            BlockOnInjectionDetection = false,
            BlockOnSanitizationError = true,
            MaxRequestBodySize = 1024,
            MaxTextFieldLength = 500
        };

        options.EnableInputSanitization.Should().BeFalse();
        options.BlockOnSanitizationError.Should().BeTrue();
        options.MaxRequestBodySize.Should().Be(1024);
        options.MaxTextFieldLength.Should().Be(500);
    }

    #endregion

    #region CustomFieldSanitizer

    [Fact]
    public void CustomFieldSanitizer_FieldName_StoresValue()
    {
        var sanitizer = new CustomFieldSanitizer("myField", s => s.ToUpper());

        sanitizer.FieldName.Should().Be("myField");
    }

    [Fact]
    public void CustomFieldSanitizer_Sanitize_InvokesDelegateWithInput()
    {
        var sanitizer = new CustomFieldSanitizer("field", s => s.Trim().ToLowerInvariant());

        var result = sanitizer.Sanitize("  Hello World  ");

        result.Should().Be("hello world");
    }

    [Fact]
    public void CustomFieldSanitizer_Sanitize_EmptyInput_ReturnsEmpty()
    {
        var sanitizer = new CustomFieldSanitizer("field", s => s);

        sanitizer.Sanitize("").Should().BeEmpty();
    }

    #endregion

    #region CustomInjectionPatterns

    [Fact]
    public void CustomInjectionPatterns_StoresTypeAndPatterns()
    {
        var patterns = new CustomInjectionPatterns(InjectionType.SqlInjection, new[] { @"\bUNION\b", @"\bDROP\b" });

        patterns.InjectionType.Should().Be(InjectionType.SqlInjection);
        patterns.Patterns.Should().HaveCount(2).And.Contain(@"\bUNION\b");
    }

    [Fact]
    public void CustomInjectionPatterns_EmptyPatterns_Allowed()
    {
        var patterns = new CustomInjectionPatterns(InjectionType.CommandInjection, Array.Empty<string>());

        patterns.Patterns.Should().BeEmpty();
    }

    #endregion

    #region InputValidatorBuilder

    [Fact]
    public void InputValidatorBuilder_AddValidator_RegistersService()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        var builder = new InputValidatorBuilder(services);

        builder.AddValidator<int>("age", input =>
        {
            return int.TryParse(input, out var val)
                ? ValidationResult<int>.Success(val)
                : ValidationResult<int>.Failure("Not an integer");
        });

        services.Should().Contain(sd => sd.ServiceType == typeof(ICustomInputValidator));
    }

    [Fact]
    public void InputValidatorBuilder_AddFieldSanitizer_RegistersService()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        var builder = new InputValidatorBuilder(services);

        builder.AddFieldSanitizer("phone", s => s.Replace("-", ""));

        services.Should().Contain(sd => sd.ServiceType == typeof(ICustomFieldSanitizer));
    }

    [Fact]
    public void InputValidatorBuilder_AddInjectionPatterns_RegistersService()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        var builder = new InputValidatorBuilder(services);

        builder.AddInjectionPatterns(InjectionType.XssInjection, @"<script>", @"</script>");

        services.Should().Contain(sd => sd.ServiceType == typeof(ICustomInjectionPatterns));
    }

    [Fact]
    public void InputValidatorBuilder_MethodChaining_Works()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        var builder = new InputValidatorBuilder(services);

        var returned = builder
            .AddFieldSanitizer("f1", s => s)
            .AddInjectionPatterns(InjectionType.SqlInjection, "pattern");

        returned.Should().BeSameAs(builder);
    }

    #endregion

    #region ValidationResult<T>

    [Fact]
    public void ValidationResultT_Success_IsValidAndHasValue()
    {
        var result = ValidationResult<string>.Success("hello");

        result.IsValid.Should().BeTrue();
        result.Value.Should().Be("hello");
    }

    [Fact]
    public void ValidationResultT_Failure_IsInvalidAndHasError()
    {
        var result = ValidationResult<string>.Failure("bad input");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("bad input");
    }

    #endregion

    #region CustomInputValidator<T>

    [Fact]
    public void CustomInputValidator_InputType_Stored()
    {
        var validator = new CustomInputValidator<int>("age", s =>
            int.TryParse(s, out var v)
                ? ValidationResult<int>.Success(v)
                : ValidationResult<int>.Failure("not int"));

        validator.InputType.Should().Be("age");
    }

    [Fact]
    public void CustomInputValidator_Validate_InvokesDelegate()
    {
        var validator = new CustomInputValidator<int>("age", s =>
            int.TryParse(s, out var v)
                ? ValidationResult<int>.Success(v)
                : ValidationResult<int>.Failure("not int"));

        var result = validator.Validate("42");

        result.IsValid.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void CustomInputValidator_Validate_InvalidInput_ReturnsFailure()
    {
        var validator = new CustomInputValidator<int>("age", s =>
            int.TryParse(s, out var v)
                ? ValidationResult<int>.Success(v)
                : ValidationResult<int>.Failure("not int"));

        var result = validator.Validate("abc");

        result.IsValid.Should().BeFalse();
    }

    #endregion
}
