using Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Tests.Configuration;

[Trait("Category", "Unit")]
public class ConfigurationValidationExtensionsTests
{
    [Fact]
    public void AddConfigurationValidation_RegistersValidationService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());

        services.AddConfigurationValidation();

        var provider = services.BuildServiceProvider();
        var service = provider.GetService<IConfigurationValidationService>();

        service.Should().NotBeNull();
        service.Should().BeOfType<ConfigurationValidationService>();
    }

    [Fact]
    public void AddConfigurationValidation_RegistersDefaultValidators()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());

        services.AddConfigurationValidation();

        var provider = services.BuildServiceProvider();
        var validators = provider.GetServices<IConfigurationValidator>();

        validators.Should().Contain(v => v is JwtConfigurationValidator);
        validators.Should().Contain(v => v is DatabaseConfigurationValidator);
        validators.Should().Contain(v => v is SmtpConfigurationValidator);
    }

    [Fact]
    public void AddConfigurationValidation_RegistersHostedService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());

        services.AddConfigurationValidation();

        var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>();

        hostedServices.Should().Contain(s => s is ConfigurationValidationHostedService);
    }

    [Fact]
    public void AddConfigurationValidation_WithConfigure_ConfiguresOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());

        services.AddConfigurationValidation(opts =>
        {
            opts.ThrowOnValidationFailure = false;
        });

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ConfigurationValidationOptions>>();
        options.Value.ThrowOnValidationFailure.Should().BeFalse();
    }

    [Fact]
    public void AddConfigurationValidator_RegistersCustomValidator()
    {
        var services = new ServiceCollection();
        services.AddConfigurationValidator<TestValidator>();

        var provider = services.BuildServiceProvider();
        var validators = provider.GetServices<IConfigurationValidator>();

        validators.Should().Contain(v => v is TestValidator);
    }
}

[Trait("Category", "Unit")]
public class ConfigurationValidationHostedServiceTests
{
    private readonly ILogger<ConfigurationValidationHostedService> _logger =
        Substitute.For<ILogger<ConfigurationValidationHostedService>>();

    [Fact]
    public async Task StartAsync_ValidConfig_CompletesSuccessfully()
    {
        var validationService = Substitute.For<IConfigurationValidationService>();
        validationService.ValidateAll().Returns(new ConfigurationValidationResult { IsValid = true });

        var hostedService = new ConfigurationValidationHostedService(validationService, _logger);

        var act = () => hostedService.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartAsync_InvalidConfig_CompletesWithoutThrowing()
    {
        var validationService = Substitute.For<IConfigurationValidationService>();
        var result = new ConfigurationValidationResult();
        result.AddError("key", "bad value");
        validationService.ValidateAll().Returns(result);

        var hostedService = new ConfigurationValidationHostedService(validationService, _logger);

        // Does not throw because the hosted service catches non-ConfigurationValidationException failures
        var act = () => hostedService.StartAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartAsync_ValidationThrowsConfigurationException_Rethrows()
    {
        var validationService = Substitute.For<IConfigurationValidationService>();
        validationService.ValidateAll().Throws(
            new ConfigurationValidationException("critical", new ConfigurationValidationResult()));

        var hostedService = new ConfigurationValidationHostedService(validationService, _logger);

        var act = () => hostedService.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<ConfigurationValidationException>();
    }

    [Fact]
    public async Task StartAsync_ValidationThrowsGenericException_Rethrows()
    {
        var validationService = Substitute.For<IConfigurationValidationService>();
        validationService.ValidateAll().Throws(new Exception("unexpected"));

        var hostedService = new ConfigurationValidationHostedService(validationService, _logger);

        var act = () => hostedService.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task StopAsync_CompletesSuccessfully()
    {
        var validationService = Substitute.For<IConfigurationValidationService>();
        var hostedService = new ConfigurationValidationHostedService(validationService, _logger);

        var act = () => hostedService.StopAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}

[Trait("Category", "Unit")]
public class ValidateConfigurationAttributeTests
{
    [Fact]
    public void Constructor_SetsSectionName()
    {
        var attr = new ValidateConfigurationAttribute("MySection");

        attr.SectionName.Should().Be("MySection");
        attr.Priority.Should().Be(50); // default
        attr.Required.Should().BeTrue(); // default
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        var attr = new ValidateConfigurationAttribute("Section")
        {
            Priority = 100,
            Required = false
        };

        attr.Priority.Should().Be(100);
        attr.Required.Should().BeFalse();
    }
}

[Trait("Category", "Unit")]
public class RequiredConfigurationAttributeTests
{
    [Fact]
    public void DefaultValues_AreNull()
    {
        var attr = new RequiredConfigurationAttribute();

        attr.ErrorMessage.Should().BeNull();
        attr.Suggestion.Should().BeNull();
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        var attr = new RequiredConfigurationAttribute
        {
            ErrorMessage = "Field is required",
            Suggestion = "Set a value"
        };

        attr.ErrorMessage.Should().Be("Field is required");
        attr.Suggestion.Should().Be("Set a value");
    }
}

[Trait("Category", "Unit")]
public class RangeConfigurationAttributeTests
{
    [Fact]
    public void Constructor_SetsMinimumAndMaximum()
    {
        var attr = new RangeConfigurationAttribute(1, 100);

        attr.Minimum.Should().Be(1);
        attr.Maximum.Should().Be(100);
        attr.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void ErrorMessage_CanBeSet()
    {
        var attr = new RangeConfigurationAttribute(0, 10)
        {
            ErrorMessage = "Value out of range"
        };

        attr.ErrorMessage.Should().Be("Value out of range");
    }
}

public class TestValidator : IConfigurationValidator
{
    public string SectionName => "Test";
    public int Priority => 50;

    public ConfigurationValidationResult Validate()
    {
        return new ConfigurationValidationResult { SectionName = SectionName, IsValid = true };
    }
}
