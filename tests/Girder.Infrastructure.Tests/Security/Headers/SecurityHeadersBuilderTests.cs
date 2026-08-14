using Infrastructure.Security.Headers;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Tests.Security.Headers;

[Trait("Category", "Unit")]
public class SecurityHeadersBuilderTests
{
    private IServiceCollection CreateServices() => new ServiceCollection();

    #region Constructor / Registration

    [Fact]
    public void Constructor_RegistersSecurityHeadersService()
    {
        var services = CreateServices();

        var builder = new SecurityHeadersBuilder(services);

        services.Any(sd => sd.ServiceType == typeof(ISecurityHeadersService)).Should().BeTrue();
    }

    #endregion

    #region ConfigureContentSecurityPolicy

    [Fact]
    public void ConfigureContentSecurityPolicy_EnablesDefaultCsp_ReturnsBuilder()
    {
        var services = CreateServices();
        var builder = new SecurityHeadersBuilder(services);

        var result = builder.ConfigureContentSecurityPolicy(csp =>
            csp.DefaultSource(CspSources.Self));

        result.Should().BeSameAs(builder);
    }

    #endregion

    #region ConfigureHsts

    [Fact]
    public void ConfigureHsts_DefaultValues_ReturnsBuilder()
    {
        var services = CreateServices();
        var builder = new SecurityHeadersBuilder(services);

        var result = builder.ConfigureHsts();

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void ConfigureHsts_CustomValues_ReturnsBuilder()
    {
        var services = CreateServices();
        var builder = new SecurityHeadersBuilder(services);

        var result = builder.ConfigureHsts(maxAge: 63072000, includeSubDomains: false, preload: true);

        result.Should().BeSameAs(builder);
    }

    #endregion

    #region AddAllowedDomains / AddCdnDomains

    [Fact]
    public void AddAllowedDomains_AddsDomainsAndReturnsBuilder()
    {
        var services = CreateServices();
        var builder = new SecurityHeadersBuilder(services);

        var result = builder.AddAllowedDomains("example.com", "cdn.example.com");

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void AddCdnDomains_AddsDomainsAndReturnsBuilder()
    {
        var services = CreateServices();
        var builder = new SecurityHeadersBuilder(services);

        var result = builder.AddCdnDomains("cdn1.example.com", "cdn2.example.com");

        result.Should().BeSameAs(builder);
    }

    #endregion

    #region Enable* methods

    [Fact]
    public void EnablePermissionsPolicy_ReturnsBuilder()
    {
        var services = CreateServices();
        var builder = new SecurityHeadersBuilder(services);

        var result = builder.EnablePermissionsPolicy(true);

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void EnablePermissionsPolicy_Disabled_ReturnsBuilder()
    {
        var services = CreateServices();
        var builder = new SecurityHeadersBuilder(services);

        var result = builder.EnablePermissionsPolicy(false);

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void EnableCrossOriginEmbedderPolicy_ReturnsBuilder()
    {
        var services = CreateServices();
        var builder = new SecurityHeadersBuilder(services);

        var result = builder.EnableCrossOriginEmbedderPolicy(true);

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void EnableCrossOriginOpenerPolicy_ReturnsBuilder()
    {
        var services = CreateServices();
        var builder = new SecurityHeadersBuilder(services);

        var result = builder.EnableCrossOriginOpenerPolicy(true);

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void EnableCrossOriginResourcePolicy_ReturnsBuilder()
    {
        var services = CreateServices();
        var builder = new SecurityHeadersBuilder(services);

        var result = builder.EnableCrossOriginResourcePolicy(true);

        result.Should().BeSameAs(builder);
    }

    #endregion

    #region ConfigureReporting

    [Fact]
    public void ConfigureReporting_SetsReportUri_ReturnsBuilder()
    {
        var services = CreateServices();
        var builder = new SecurityHeadersBuilder(services);

        var result = builder.ConfigureReporting("https://example.com/csp-report");

        result.Should().BeSameAs(builder);
    }

    #endregion

    #region ConfigureMiddleware

    [Fact]
    public void ConfigureMiddleware_CallsConfigureAction_ReturnsBuilder()
    {
        var services = CreateServices();
        var builder = new SecurityHeadersBuilder(services);
        var called = false;

        var result = builder.ConfigureMiddleware(opts =>
        {
            called = true;
            opts.LogSecurityHeaders = true;
        });

        result.Should().BeSameAs(builder);
        called.Should().BeTrue();
    }

    #endregion

    #region AddCustomRequirement

    [Fact]
    public void AddCustomRequirement_AddsRequirementAndReturnsBuilder()
    {
        var services = CreateServices();
        var builder = new SecurityHeadersBuilder(services);

        var result = builder.AddCustomRequirement("X-My-Header", "value");

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void AddCustomRequirement_NullValue_ReturnsBuilder()
    {
        var services = CreateServices();
        var builder = new SecurityHeadersBuilder(services);

        var result = builder.AddCustomRequirement("X-Null-Header", null);

        result.Should().BeSameAs(builder);
    }

    #endregion

    #region ForDevelopment

    [Fact]
    public void ForDevelopment_ReturnsBuilder()
    {
        var services = CreateServices();
        var builder = new SecurityHeadersBuilder(services);

        var result = builder.ForDevelopment();

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void ForDevelopment_ChainedWithOtherMethods_Works()
    {
        var services = CreateServices();
        var builder = new SecurityHeadersBuilder(services);

        var result = builder
            .ForDevelopment()
            .AddAllowedDomains("localhost:3000");

        result.Should().BeSameAs(builder);
    }

    #endregion

    #region ForProduction

    [Fact]
    public void ForProduction_ReturnsBuilder()
    {
        var services = CreateServices();
        var builder = new SecurityHeadersBuilder(services);

        var result = builder.ForProduction();

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void ForProduction_ChainedWithOtherMethods_Works()
    {
        var services = CreateServices();
        var builder = new SecurityHeadersBuilder(services);

        var result = builder
            .ForProduction()
            .ConfigureReporting("https://csp.example.com/report");

        result.Should().BeSameAs(builder);
    }

    #endregion

    #region Fluent chaining

    [Fact]
    public void FluentChaining_AllMethods_WorksTogether()
    {
        var services = CreateServices();
        var builder = new SecurityHeadersBuilder(services);

        var result = builder
            .ConfigureHsts(31536000, true, false)
            .AddAllowedDomains("api.example.com")
            .AddCdnDomains("cdn.example.com")
            .EnablePermissionsPolicy()
            .EnableCrossOriginEmbedderPolicy()
            .EnableCrossOriginOpenerPolicy()
            .EnableCrossOriginResourcePolicy()
            .ConfigureReporting("https://csp.example.com/report")
            .AddCustomRequirement("X-Extra", "value")
            .ConfigureMiddleware(opts => opts.MinimumSecurityScore = 70);

        result.Should().BeSameAs(builder);
    }

    #endregion
}

[Trait("Category", "Unit")]
public class SecurityHeadersExtensionsTests
{
    [Fact]
    public void AddSecurityHeaders_WithConfiguration_RegistersService()
    {
        var services = new ServiceCollection();
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();

        services.AddSecurityHeaders(config);

        services.Any(sd => sd.ServiceType == typeof(ISecurityHeadersService)).Should().BeTrue();
    }

    [Fact]
    public void AddSecurityHeaders_WithHeadersAction_RegistersService()
    {
        var services = new ServiceCollection();

        services.AddSecurityHeaders(opts => opts.EnableHsts = false);

        services.Any(sd => sd.ServiceType == typeof(ISecurityHeadersService)).Should().BeTrue();
    }

    [Fact]
    public void AddSecurityHeaders_WithHeadersAndMiddlewareAction_RegistersService()
    {
        var services = new ServiceCollection();

        services.AddSecurityHeaders(
            opts => opts.EnableHsts = false,
            mw => mw.LogSecurityHeaders = true);

        services.Any(sd => sd.ServiceType == typeof(ISecurityHeadersService)).Should().BeTrue();
    }

    [Fact]
    public void AddSecurityHeaders_WithBuilderAction_RegistersService()
    {
        var services = new ServiceCollection();

        services.AddSecurityHeaders(builder =>
            builder.ConfigureHsts().EnablePermissionsPolicy());

        services.Any(sd => sd.ServiceType == typeof(ISecurityHeadersService)).Should().BeTrue();
    }

    [Fact]
    public void AddSecurityHeaders_NullMiddlewareAction_RegistersService()
    {
        var services = new ServiceCollection();

        services.AddSecurityHeaders(
            opts => opts.EnableDefaultCsp = true,
            null);

        services.Any(sd => sd.ServiceType == typeof(ISecurityHeadersService)).Should().BeTrue();
    }
}
