using Girder.Infrastructure.Security.InputSanitization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Girder.Infrastructure.Tests.Security.InputSanitization;

[Trait("Category", "Unit")]
public class InputSanitizationExtensionsTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    [Fact]
    public void AddInputSanitization_RegistersInputSanitizerAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = BuildConfig(new Dictionary<string, string?>());

        services.AddInputSanitization(config);

        services.Should().Contain(d =>
            d.ServiceType == typeof(IInputSanitizer) &&
            d.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddInputSanitization_RegistersInputValidatorAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = BuildConfig(new Dictionary<string, string?>());

        services.AddInputSanitization(config);

        services.Should().Contain(d =>
            d.ServiceType == typeof(IInputValidator) &&
            d.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddInputSanitization_RegistersInjectionDetectorAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = BuildConfig(new Dictionary<string, string?>());

        services.AddInputSanitization(config);

        services.Should().Contain(d =>
            d.ServiceType == typeof(IInjectionDetector) &&
            d.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddInputSanitization_CanResolveInputSanitizer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = BuildConfig(new Dictionary<string, string?>());

        services.AddInputSanitization(config);
        var provider = services.BuildServiceProvider();

        var sanitizer = provider.GetService<IInputSanitizer>();
        sanitizer.Should().NotBeNull();
    }

    [Fact]
    public void AddInputSanitization_CanResolveInjectionDetector()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = BuildConfig(new Dictionary<string, string?>());

        services.AddInputSanitization(config);
        var provider = services.BuildServiceProvider();

        var detector = provider.GetService<IInjectionDetector>();
        detector.Should().NotBeNull();
    }

    [Fact]
    public void AddInputSanitization_CanResolveInputValidator()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = BuildConfig(new Dictionary<string, string?>());

        services.AddInputSanitization(config);
        var provider = services.BuildServiceProvider();

        var validator = provider.GetService<IInputValidator>();
        validator.Should().NotBeNull();
    }

    [Fact]
    public void AddInputSanitizationMiddleware_ReturnsServiceCollection()
    {
        var services = new ServiceCollection();

        var result = services.AddInputSanitizationMiddleware();

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddCustomInputValidators_RegistersCustomValidator()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = BuildConfig(new Dictionary<string, string?>());
        services.AddInputSanitization(config);

        services.AddCustomInputValidators(builder =>
        {
            builder.AddValidator<string>("test-type", input =>
                string.IsNullOrEmpty(input)
                    ? ValidationResult<string>.Failure("Empty")
                    : ValidationResult<string>.Success(input));
        });

        services.Should().Contain(d => d.ServiceType == typeof(ICustomInputValidator));
    }

    [Fact]
    public void AddCustomInputValidators_RegistersCustomFieldSanitizer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = BuildConfig(new Dictionary<string, string?>());
        services.AddInputSanitization(config);

        services.AddCustomInputValidators(builder =>
        {
            builder.AddFieldSanitizer("customField", input => input.Trim().ToUpper());
        });

        services.Should().Contain(d => d.ServiceType == typeof(ICustomFieldSanitizer));
    }

    [Fact]
    public void AddCustomInputValidators_RegistersCustomInjectionPatterns()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = BuildConfig(new Dictionary<string, string?>());
        services.AddInputSanitization(config);

        services.AddCustomInputValidators(builder =>
        {
            builder.AddInjectionPatterns(InjectionType.SqlInjection, @"\bexec\b");
        });

        services.Should().Contain(d => d.ServiceType == typeof(ICustomInjectionPatterns));
    }

    [Fact]
    public void AddCustomInputValidators_BuilderMethodsAreChainable()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = BuildConfig(new Dictionary<string, string?>());
        services.AddInputSanitization(config);

        var act = () => services.AddCustomInputValidators(builder =>
        {
            builder
                .AddValidator<string>("type1", input => ValidationResult<string>.Success(input))
                .AddFieldSanitizer("field1", input => input)
                .AddInjectionPatterns(InjectionType.XssInjection, @"<script>");
        });

        act.Should().NotThrow();
    }

    [Fact]
    public void AddInputSanitization_WithOptions_ConfiguresSection()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["InputSanitization:Enabled"] = "true"
        });

        var act = () => services.AddInputSanitization(config);

        act.Should().NotThrow();
    }
}
