using Girder.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Girder.Infrastructure.Tests.Configuration;

// These config types must be public (not nested/private) so NSubstitute/Options can proxy IOptionsMonitor<T>

public class ValidatorTestPlainConfig
{
    public string? Name { get; set; }
    public int Value { get; set; }
}

[ValidateConfiguration("TestSection", Priority = 75)]
public class ValidatorTestAnnotatedConfig
{
    [RequiredConfiguration(ErrorMessage = "Name is required", Suggestion = "Provide a name")]
    public string? Name { get; set; }

    [RangeConfiguration(1, 100, ErrorMessage = "Value out of range")]
    public int Count { get; set; }
}

[ValidateConfiguration("SimpleSection")]
public class ValidatorTestSimpleConfig
{
    [RequiredConfiguration]
    public string? Host { get; set; }
}

[Trait("Category", "Unit")]
public class AttributeBasedConfigurationValidatorTests
{
    private static IOptionsMonitor<T> BuildOptions<T>(T value) where T : class, new()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.PostConfigure<T>(opts =>
        {
            foreach (var prop in typeof(T).GetProperties().Where(p => p.CanWrite))
            {
                prop.SetValue(opts, prop.GetValue(value));
            }
        });
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptionsMonitor<T>>();
    }

    #region SectionName and Priority

    [Fact]
    public void SectionName_WithAttribute_ReturnsAttributeSectionName()
    {
        var monitor = BuildOptions(new ValidatorTestAnnotatedConfig { Name = "test", Count = 5 });
        var validator = new AttributeBasedConfigurationValidator<ValidatorTestAnnotatedConfig>(monitor);

        validator.SectionName.Should().Be("TestSection");
    }

    [Fact]
    public void SectionName_WithoutAttribute_ReturnsTypeName()
    {
        var monitor = BuildOptions(new ValidatorTestPlainConfig { Name = "x", Value = 1 });
        var validator = new AttributeBasedConfigurationValidator<ValidatorTestPlainConfig>(monitor);

        validator.SectionName.Should().Be("ValidatorTestPlainConfig");
    }

    [Fact]
    public void Priority_WithAttribute_ReturnsAttributePriority()
    {
        var monitor = BuildOptions(new ValidatorTestAnnotatedConfig { Name = "test", Count = 5 });
        var validator = new AttributeBasedConfigurationValidator<ValidatorTestAnnotatedConfig>(monitor);

        validator.Priority.Should().Be(75);
    }

    [Fact]
    public void Priority_WithoutAttribute_ReturnsDefaultPriority()
    {
        var monitor = BuildOptions(new ValidatorTestPlainConfig { Name = "x" });
        var validator = new AttributeBasedConfigurationValidator<ValidatorTestPlainConfig>(monitor);

        validator.Priority.Should().Be(50);
    }

    #endregion

    #region Validate — required properties

    [Fact]
    public void Validate_RequiredProperty_NullValue_AddsError()
    {
        var monitor = BuildOptions(new ValidatorTestAnnotatedConfig { Name = null, Count = 5 });
        var validator = new AttributeBasedConfigurationValidator<ValidatorTestAnnotatedConfig>(monitor);

        var result = validator.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Key.Contains("Name") && e.Message.Contains("Name is required"));
    }

    [Fact]
    public void Validate_RequiredProperty_EmptyString_AddsError()
    {
        var monitor = BuildOptions(new ValidatorTestAnnotatedConfig { Name = "", Count = 5 });
        var validator = new AttributeBasedConfigurationValidator<ValidatorTestAnnotatedConfig>(monitor);

        var result = validator.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Key.Contains("Name"));
    }

    [Fact]
    public void Validate_RequiredProperty_ValidValue_NoError()
    {
        var monitor = BuildOptions(new ValidatorTestAnnotatedConfig { Name = "valid", Count = 5 });
        var validator = new AttributeBasedConfigurationValidator<ValidatorTestAnnotatedConfig>(monitor);

        var result = validator.Validate();

        result.Errors.Should().NotContain(e => e.Key.Contains("Name"));
    }

    [Fact]
    public void Validate_RequiredProperty_NoCustomErrorMessage_UsesDefault()
    {
        var monitor = BuildOptions(new ValidatorTestSimpleConfig { Host = null });
        var validator = new AttributeBasedConfigurationValidator<ValidatorTestSimpleConfig>(monitor);

        var result = validator.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Key.Contains("Host"));
    }

    #endregion

    #region Validate — range validation

    [Fact]
    public void Validate_RangeProperty_BelowMinimum_AddsError()
    {
        var monitor = BuildOptions(new ValidatorTestAnnotatedConfig { Name = "x", Count = 0 });
        var validator = new AttributeBasedConfigurationValidator<ValidatorTestAnnotatedConfig>(monitor);

        var result = validator.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Key.Contains("Count") && e.Message.Contains("out of range"));
    }

    [Fact]
    public void Validate_RangeProperty_AboveMaximum_AddsError()
    {
        var monitor = BuildOptions(new ValidatorTestAnnotatedConfig { Name = "x", Count = 101 });
        var validator = new AttributeBasedConfigurationValidator<ValidatorTestAnnotatedConfig>(monitor);

        var result = validator.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Key.Contains("Count"));
    }

    [Fact]
    public void Validate_RangeProperty_AtMinimum_NoError()
    {
        var monitor = BuildOptions(new ValidatorTestAnnotatedConfig { Name = "x", Count = 1 });
        var validator = new AttributeBasedConfigurationValidator<ValidatorTestAnnotatedConfig>(monitor);

        var result = validator.Validate();

        result.Errors.Should().NotContain(e => e.Key.Contains("Count"));
    }

    [Fact]
    public void Validate_RangeProperty_AtMaximum_NoError()
    {
        var monitor = BuildOptions(new ValidatorTestAnnotatedConfig { Name = "x", Count = 100 });
        var validator = new AttributeBasedConfigurationValidator<ValidatorTestAnnotatedConfig>(monitor);

        var result = validator.Validate();

        result.Errors.Should().NotContain(e => e.Key.Contains("Count"));
    }

    #endregion

    #region Validate — all valid

    [Fact]
    public void Validate_AllPropertiesValid_ReturnsValid()
    {
        var monitor = BuildOptions(new ValidatorTestAnnotatedConfig { Name = "valid", Count = 50 });
        var validator = new AttributeBasedConfigurationValidator<ValidatorTestAnnotatedConfig>(monitor);

        var result = validator.Validate();

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_PlainConfigNoAnnotations_ReturnsValid()
    {
        var monitor = BuildOptions(new ValidatorTestPlainConfig { Name = null, Value = -999 });
        var validator = new AttributeBasedConfigurationValidator<ValidatorTestPlainConfig>(monitor);

        var result = validator.Validate();

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    #endregion

    #region Validate — exception handling

    [Fact]
    public void Validate_OptionsMonitorThrows_ReturnsInvalidResult()
    {
        var monitor = Substitute.For<IOptionsMonitor<ValidatorTestPlainConfig>>();
        monitor.CurrentValue.Throws(new InvalidOperationException("Options not configured"));

        var validator = new AttributeBasedConfigurationValidator<ValidatorTestPlainConfig>(monitor);

        var result = validator.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Message.Contains("Failed to validate configuration"));
    }

    #endregion
}
