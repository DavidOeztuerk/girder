using Girder.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Girder.Infrastructure.Tests.Configuration;

[Trait("Category", "Unit")]
public class ConfigurationValidationServiceTests
{
    private readonly ILogger<ConfigurationValidationService> _logger =
        Substitute.For<ILogger<ConfigurationValidationService>>();

    private ConfigurationValidationService CreateService(
        IEnumerable<IConfigurationValidator>? validators = null,
        ConfigurationValidationOptions? options = null)
    {
        var validatorList = validators ?? Enumerable.Empty<IConfigurationValidator>();
        var opts = Options.Create(options ?? new ConfigurationValidationOptions
        {
            LogValidationResults = true,
            ThrowOnValidationFailure = false
        });

        return new ConfigurationValidationService(validatorList, _logger, opts);
    }

    #region ValidateAll

    [Fact]
    public void ValidateAll_NoValidators_ReturnsValid()
    {
        var service = CreateService();

        var result = service.ValidateAll();

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateAll_AllValid_ReturnsValid()
    {
        var validator = Substitute.For<IConfigurationValidator>();
        validator.SectionName.Returns("TestSection");
        validator.Priority.Returns(50);
        validator.Validate().Returns(new ConfigurationValidationResult { SectionName = "TestSection", IsValid = true });

        var service = CreateService(new[] { validator });

        var result = service.ValidateAll();

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateAll_WithErrors_ReturnsInvalid()
    {
        var validator = Substitute.For<IConfigurationValidator>();
        validator.SectionName.Returns("BadSection");
        validator.Priority.Returns(50);
        var errorResult = new ConfigurationValidationResult { SectionName = "BadSection" };
        errorResult.AddError("key", "missing value");
        validator.Validate().Returns(errorResult);

        var service = CreateService(new[] { validator });

        var result = service.ValidateAll();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
    }

    [Fact]
    public void ValidateAll_ValidatorThrows_ReturnsInvalid()
    {
        var validator = Substitute.For<IConfigurationValidator>();
        validator.SectionName.Returns("ThrowingSection");
        validator.Priority.Returns(50);
        validator.Validate().Throws(new InvalidOperationException("validator error"));

        var service = CreateService(new[] { validator });

        var result = service.ValidateAll();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("exception"));
    }

    [Fact]
    public void ValidateAll_ThrowOnFailure_ThrowsException()
    {
        var validator = Substitute.For<IConfigurationValidator>();
        validator.SectionName.Returns("BadSection");
        validator.Priority.Returns(50);
        var errorResult = new ConfigurationValidationResult { SectionName = "BadSection" };
        errorResult.AddError("key", "bad value");
        validator.Validate().Returns(errorResult);

        var service = CreateService(new[] { validator }, new ConfigurationValidationOptions
        {
            ThrowOnValidationFailure = true,
            LogValidationResults = false
        });

        var act = () => service.ValidateAll();

        act.Should().Throw<ConfigurationValidationException>();
    }

    [Fact]
    public void ValidateAll_MultipleValidators_RunsInPriorityOrder()
    {
        var order = new List<string>();

        var highPriority = Substitute.For<IConfigurationValidator>();
        highPriority.SectionName.Returns("High");
        highPriority.Priority.Returns(100);
        highPriority.Validate().Returns(callInfo =>
        {
            order.Add("High");
            return new ConfigurationValidationResult { SectionName = "High", IsValid = true };
        });

        var lowPriority = Substitute.For<IConfigurationValidator>();
        lowPriority.SectionName.Returns("Low");
        lowPriority.Priority.Returns(10);
        lowPriority.Validate().Returns(callInfo =>
        {
            order.Add("Low");
            return new ConfigurationValidationResult { SectionName = "Low", IsValid = true };
        });

        var service = CreateService(new[] { lowPriority, highPriority });

        service.ValidateAll();

        order.Should().ContainInOrder("High", "Low");
    }

    [Fact]
    public void ValidateAll_WithWarnings_ReturnsValid()
    {
        var validator = Substitute.For<IConfigurationValidator>();
        validator.SectionName.Returns("WarnSection");
        validator.Priority.Returns(50);
        var warnResult = new ConfigurationValidationResult { SectionName = "WarnSection", IsValid = true };
        warnResult.AddWarning("key", "consider changing this");
        validator.Validate().Returns(warnResult);

        var service = CreateService(new[] { validator });

        var result = service.ValidateAll();

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().HaveCount(1);
    }

    #endregion

    #region ValidateSection

    [Fact]
    public void ValidateSection_UnknownSection_ReturnsValid()
    {
        var service = CreateService();

        var result = service.ValidateSection("NonExistent");

        result.IsValid.Should().BeTrue();
        result.SectionName.Should().Be("NonExistent");
    }

    [Fact]
    public void ValidateSection_KnownSection_RunsValidator()
    {
        var validator = Substitute.For<IConfigurationValidator>();
        validator.SectionName.Returns("KnownSection");
        validator.Priority.Returns(50);
        validator.Validate().Returns(new ConfigurationValidationResult { SectionName = "KnownSection", IsValid = true });

        var service = CreateService(new[] { validator });

        var result = service.ValidateSection("KnownSection");

        result.IsValid.Should().BeTrue();
        validator.Received(1).Validate();
    }

    [Fact]
    public void ValidateSection_ValidatorThrows_ReturnsInvalid()
    {
        var validator = Substitute.For<IConfigurationValidator>();
        validator.SectionName.Returns("ThrowingSection");
        validator.Priority.Returns(50);
        validator.Validate().Throws(new Exception("boom"));

        var service = CreateService(new[] { validator });

        var result = service.ValidateSection("ThrowingSection");

        result.IsValid.Should().BeFalse();
    }

    #endregion

    #region ConfigurationValidationResult DTO

    [Fact]
    public void AddError_SetsIsValidToFalse()
    {
        var result = new ConfigurationValidationResult();

        result.AddError("key", "message");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Key.Should().Be("key");
        result.Errors[0].Message.Should().Be("message");
    }

    [Fact]
    public void AddError_WithSuggestion_StoresSuggestion()
    {
        var result = new ConfigurationValidationResult();

        result.AddError("key", "message", "fix it");

        result.Errors[0].Suggestion.Should().Be("fix it");
    }

    [Fact]
    public void AddWarning_DoesNotChangeIsValid()
    {
        var result = new ConfigurationValidationResult();

        result.AddWarning("key", "might want to check");

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().HaveCount(1);
    }

    [Fact]
    public void Combine_AllValid_ReturnsValid()
    {
        var r1 = new ConfigurationValidationResult { IsValid = true };
        var r2 = new ConfigurationValidationResult { IsValid = true };

        var combined = ConfigurationValidationResult.Combine(r1, r2);

        combined.IsValid.Should().BeTrue();
        combined.SectionName.Should().Be("Combined");
    }

    [Fact]
    public void Combine_OneInvalid_ReturnsInvalid()
    {
        var r1 = new ConfigurationValidationResult { IsValid = true };
        var r2 = new ConfigurationValidationResult { IsValid = false };

        var combined = ConfigurationValidationResult.Combine(r1, r2);

        combined.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Combine_MergesErrorsAndWarnings()
    {
        var r1 = new ConfigurationValidationResult();
        r1.AddError("k1", "err1");
        r1.AddWarning("k1", "warn1");

        var r2 = new ConfigurationValidationResult();
        r2.AddError("k2", "err2");

        var combined = ConfigurationValidationResult.Combine(r1, r2);

        combined.Errors.Should().HaveCount(2);
        combined.Warnings.Should().HaveCount(1);
    }

    #endregion

    #region ConfigurationValidationException

    [Fact]
    public void ConfigurationValidationException_StoresResult()
    {
        var result = new ConfigurationValidationResult();
        result.AddError("key", "error");

        var ex = new ConfigurationValidationException("validation failed", result);

        ex.Message.Should().Be("validation failed");
        ex.ValidationResult.Should().BeSameAs(result);
    }

    [Fact]
    public void ConfigurationValidationException_WithInnerException()
    {
        var result = new ConfigurationValidationResult();
        var inner = new InvalidOperationException("inner error");

        var ex = new ConfigurationValidationException("validation failed", result, inner);

        ex.InnerException.Should().BeSameAs(inner);
        ex.ValidationResult.Should().BeSameAs(result);
    }

    #endregion

    #region ConfigurationValidationOptions Defaults

    [Fact]
    public void ConfigurationValidationOptions_DefaultValues()
    {
        var options = new ConfigurationValidationOptions();

        options.ThrowOnValidationFailure.Should().BeTrue();
        options.LogValidationResults.Should().BeTrue();
        options.ValidateOnStartup.Should().BeTrue();
        options.ValidateOnChange.Should().BeFalse();
        options.MinimumSeverity.Should().Be(ErrorSeverity.Warning);
        options.TreatWarningsAsErrors.Should().BeFalse();
    }

    #endregion

    #region ErrorSeverity Enum

    [Fact]
    public void ErrorSeverity_HasExpectedValues()
    {
        ErrorSeverity.Warning.Should().BeDefined();
        ErrorSeverity.Error.Should().BeDefined();
        ErrorSeverity.Critical.Should().BeDefined();
    }

    #endregion
}
