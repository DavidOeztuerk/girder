using Girder.Infrastructure.Builder;
using Girder.Infrastructure.Builder.Modules;
using Girder.Infrastructure.Security;
using Girder.Infrastructure.Security.Audit;
using Girder.Infrastructure.Security.Headers;
using Girder.Infrastructure.Security.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelemetry.Metrics;
using AuditSecurityAuditMiddleware = Girder.Infrastructure.Security.Audit.SecurityAuditMiddleware;
using RootSecurityAuditMiddleware = Girder.Infrastructure.Security.SecurityAuditMiddleware;
using RootSecurityAuditEvent = Girder.Infrastructure.Security.SecurityAuditEvent;

namespace Girder.Infrastructure.Tests.Builder;

[Trait("Category", "Unit")]
public class BuilderModuleCoverageTests
{
    private readonly IServiceCollection _services = new ServiceCollection();
    private readonly IHostEnvironment _environment = Substitute.For<IHostEnvironment>();

    public BuilderModuleCoverageTests()
    {
        _environment.EnvironmentName.Returns("Development");
    }

    #region JwtModule

    [Fact]
    public void AddJwtAuthentication_SetsJwtEnabledFlag()
    {
        var configData = new Dictionary<string, string?>
        {
            ["JwtSettings:Secret"] = "TestSecret-AtLeast32Characters-Long-Enough!",
            ["JwtSettings:Issuer"] = "test-issuer",
            ["JwtSettings:Audience"] = "test-audience"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(configData).Build();
        var builder = new InfrastructureBuilder(_services, config, _environment, "TestService");

        var result = builder.AddJwtAuthentication();

        result.Should().BeSameAs(builder);
        builder.JwtEnabled.Should().BeTrue();
    }

    [Fact]
    public void AddJwtAuthentication_RegistersAuthenticationServices()
    {
        var configData = new Dictionary<string, string?>
        {
            ["JwtSettings:Secret"] = "TestSecret-AtLeast32Characters-Long-Enough!",
            ["JwtSettings:Issuer"] = "test-issuer",
            ["JwtSettings:Audience"] = "test-audience"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(configData).Build();
        var builder = new InfrastructureBuilder(_services, config, _environment, "TestService");

        builder.AddJwtAuthentication();

        // Authentication services should be registered
        _services.Should().Contain(sd => sd.ServiceType.FullName!.Contains("IAuthenticationService")
            || sd.ServiceType.FullName!.Contains("IAuthentication"));
    }

    #endregion

    #region MiddlewarePipelineModule

    [Fact]
    public void UseSecurityHeaders_RegistersMiddleware_ReturnsBuilder()
    {
        var (mwBuilder, app) = CreateMiddlewareBuilder();

        var result = mwBuilder.UseSecurityHeaders();

        result.Should().BeSameAs(mwBuilder);
        // UseMiddleware internally calls IApplicationBuilder.Use()
        app.Received().Use(Arg.Any<Func<RequestDelegate, RequestDelegate>>());
    }

    [Fact]
    public void UseCorrelationId_RegistersMiddleware_ReturnsBuilder()
    {
        var (mwBuilder, app) = CreateMiddlewareBuilder();

        var result = mwBuilder.UseCorrelationId();

        result.Should().BeSameAs(mwBuilder);
        app.Received().Use(Arg.Any<Func<RequestDelegate, RequestDelegate>>());
    }

    [Fact]
    public void UseRequestLogging_RegistersMiddleware_ReturnsBuilder()
    {
        var (mwBuilder, app) = CreateMiddlewareBuilder();

        var result = mwBuilder.UseRequestLogging();

        result.Should().BeSameAs(mwBuilder);
        app.Received().Use(Arg.Any<Func<RequestDelegate, RequestDelegate>>());
    }

    [Fact]
    public void UseExceptionHandling_RegistersMiddleware_ReturnsBuilder()
    {
        var (mwBuilder, app) = CreateMiddlewareBuilder();

        var result = mwBuilder.UseExceptionHandling();

        result.Should().BeSameAs(mwBuilder);
        app.Received().Use(Arg.Any<Func<RequestDelegate, RequestDelegate>>());
    }

    [Fact]
    public void UseInputSanitization_RegistersMiddleware_ReturnsBuilder()
    {
        var (mwBuilder, app) = CreateMiddlewareBuilder();

        var result = mwBuilder.UseInputSanitization();

        result.Should().BeSameAs(mwBuilder);
        app.Received().Use(Arg.Any<Func<RequestDelegate, RequestDelegate>>());
    }

    [Fact]
    public void UseCors_RegistersMiddleware_ReturnsBuilder()
    {
        var (mwBuilder, _) = CreateMiddlewareBuilder();

        var result = mwBuilder.UseCors();

        result.Should().BeSameAs(mwBuilder);
    }

    [Fact]
    public void UseAuth_RegistersAuthMiddleware_ReturnsBuilder()
    {
        var (mwBuilder, _) = CreateMiddlewareBuilderWithServices(services =>
        {
            services.AddAuthorization();
        });

        var result = mwBuilder.UseAuth();

        result.Should().BeSameAs(mwBuilder);
    }

    [Fact]
    public void UseSecurityAudit_RegistersMiddleware_ReturnsBuilder()
    {
        var (mwBuilder, app) = CreateMiddlewareBuilder();

        var result = mwBuilder.UseSecurityAudit();

        result.Should().BeSameAs(mwBuilder);
        app.Received().Use(Arg.Any<Func<RequestDelegate, RequestDelegate>>());
    }

    [Fact]
    public void UseSwagger_InDevelopment_ReturnsBuilder()
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns("Development");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services.AddSwaggerGen();

        // UseSwaggerUI resolves IWebHostEnvironment from the container.
        var webEnv = Substitute.For<IWebHostEnvironment>();
        webEnv.EnvironmentName.Returns("Development");
        webEnv.ApplicationName.Returns("TestService");
        webEnv.ContentRootPath.Returns(AppContext.BaseDirectory);
        webEnv.WebRootPath.Returns(AppContext.BaseDirectory);
        webEnv.ContentRootFileProvider.Returns(new NullFileProvider());
        webEnv.WebRootFileProvider.Returns(new NullFileProvider());
        services.AddSingleton(webEnv);

        var serviceProvider = services.BuildServiceProvider();
        var app = Substitute.For<IApplicationBuilder>();
        app.ApplicationServices.Returns(serviceProvider);
        var mwBuilder = new InfrastructureMiddlewareBuilder(app, env, "TestService");

        var result = mwBuilder.UseSwagger();

        result.Should().BeSameAs(mwBuilder);
    }

    [Fact]
    public void UseSwagger_InProduction_SkipsSwagger_ReturnsBuilder()
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns("Production");
        var app = Substitute.For<IApplicationBuilder>();
        var mwBuilder = new InfrastructureMiddlewareBuilder(app, env, "TestService");

        var result = mwBuilder.UseSwagger();

        result.Should().BeSameAs(mwBuilder);
        // In production, no Use() should be called for swagger
        app.DidNotReceive().Use(Arg.Any<Func<RequestDelegate, RequestDelegate>>());
    }

    [Fact]
    public void UseRateLimiting_RegistersMiddleware_ReturnsBuilder()
    {
        var (mwBuilder, app) = CreateMiddlewareBuilderWithServices(services =>
        {
            services.AddLogging();
        });

        var result = mwBuilder.UseRateLimiting();

        result.Should().BeSameAs(mwBuilder);
        app.Received().Use(Arg.Any<Func<RequestDelegate, RequestDelegate>>());
    }

    [Fact]
    public void UseHealthCheckEndpoints_ReturnsBuilder()
    {
        var (mwBuilder, _) = CreateMiddlewareBuilderWithServices(services =>
        {
            services.AddLogging();
            services.AddOptions();
            services.AddHealthChecks();
        });

        var result = mwBuilder.UseHealthCheckEndpoints();

        result.Should().BeSameAs(mwBuilder);
    }

    [Fact]
    public void UsePermissions_ReturnsBuilder()
    {
        var (mwBuilder, _) = CreateMiddlewareBuilder();

        var result = mwBuilder.UsePermissions();

        result.Should().BeSameAs(mwBuilder);
    }

    [Fact]
    public void UseHttpCaching_ReturnsBuilder()
    {
        var (mwBuilder, _) = CreateMiddlewareBuilder();

        var result = mwBuilder.UseHttpCaching();

        result.Should().BeSameAs(mwBuilder);
    }

    [Fact]
    public void UseSerilogLogging_ReturnsBuilder()
    {
        var (mwBuilder, _) = CreateMiddlewareBuilderWithServices(services =>
        {
            services.AddLogging();
        });

        var result = mwBuilder.UseSerilogLogging();

        result.Should().BeSameAs(mwBuilder);
    }

    [Fact]
    public void UseTelemetry_ReturnsBuilder()
    {
        var (mwBuilder, _) = CreateMiddlewareBuilder();

        var result = mwBuilder.UseTelemetry();

        result.Should().BeSameAs(mwBuilder);
    }

    [Fact]
    public void FluentChaining_MiddlewarePipeline_Works()
    {
        var (mwBuilder, _) = CreateMiddlewareBuilderWithServices(services =>
        {
            services.AddAuthorization();
        });

        var result = mwBuilder
            .UseSecurityHeaders()
            .UseCorrelationId()
            .UseRequestLogging()
            .UseExceptionHandling()
            .UseInputSanitization()
            .UseCors()
            .UseAuth()
            .UseSecurityAudit();

        result.Should().BeSameAs(mwBuilder);
    }

    #endregion

    #region SecurityAuditMiddleware (Girder.Infrastructure.Security namespace)

    [Fact]
    public async Task RootSecurityAuditMiddleware_InvokeAsync_CallsNext()
    {
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };
        var auditLogger = Substitute.For<ISecurityAuditLogger>();
        var logger = NullLogger<RootSecurityAuditMiddleware>.Instance;
        var middleware = new RootSecurityAuditMiddleware(next, auditLogger, logger);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/data";
        context.Request.Method = "GET";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task RootSecurityAuditMiddleware_AuthPath_AuditsRequestStartedAndCompleted()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var auditLogger = Substitute.For<ISecurityAuditLogger>();
        var logger = NullLogger<RootSecurityAuditMiddleware>.Instance;
        var middleware = new RootSecurityAuditMiddleware(next, auditLogger, logger);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/auth/login";
        context.Request.Method = "POST";

        await middleware.InvokeAsync(context);

        await auditLogger.Received(2).LogSecurityEventAsync(
            Arg.Any<RootSecurityAuditEvent>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RootSecurityAuditMiddleware_AdminPath_AuditsEvents()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var auditLogger = Substitute.For<ISecurityAuditLogger>();
        var logger = NullLogger<RootSecurityAuditMiddleware>.Instance;
        var middleware = new RootSecurityAuditMiddleware(next, auditLogger, logger);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/admin/users";
        context.Request.Method = "GET";

        await middleware.InvokeAsync(context);

        await auditLogger.Received(2).LogSecurityEventAsync(
            Arg.Any<RootSecurityAuditEvent>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RootSecurityAuditMiddleware_NonSensitivePath_SkipsAudit()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var auditLogger = Substitute.For<ISecurityAuditLogger>();
        var logger = NullLogger<RootSecurityAuditMiddleware>.Instance;
        var middleware = new RootSecurityAuditMiddleware(next, auditLogger, logger);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/jobs";
        context.Request.Method = "GET";
        context.Response.StatusCode = 200;

        await middleware.InvokeAsync(context);

        await auditLogger.DidNotReceive().LogSecurityEventAsync(
            Arg.Any<RootSecurityAuditEvent>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RootSecurityAuditMiddleware_AuditLoggerThrows_DoesNotBubble()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var auditLogger = Substitute.For<ISecurityAuditLogger>();
        auditLogger.LogSecurityEventAsync(Arg.Any<RootSecurityAuditEvent>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Audit failure"));
        var logger = NullLogger<RootSecurityAuditMiddleware>.Instance;
        var middleware = new RootSecurityAuditMiddleware(next, auditLogger, logger);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/auth/token";
        context.Request.Method = "POST";

        // Should not throw even though audit logger throws
        var act = () => middleware.InvokeAsync(context);

        await act.Should().NotThrowAsync();
    }

    #endregion

    #region RateLimitExtensions

    [Fact]
    public void AddRateLimit_WithoutRedis_RegistersInMemoryService()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        _services.AddLogging();
        _services.AddRateLimit(config);

        var provider = _services.BuildServiceProvider();
        var rateLimitService = provider.GetService<IRateLimitService>();
        rateLimitService.Should().NotBeNull();
        rateLimitService.Should().BeOfType<InMemoryRateLimitService>();
    }

    [Fact]
    public void AddRateLimit_RegistersRateLimitMiddleware()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        _services.AddLogging();
        _services.AddRateLimit(config);

        _services.Should().Contain(sd =>
            sd.ServiceType == typeof(RateLimitMiddleware));
    }

    [Fact]
    public void AddRateLimit_RegistersHostedMaintenanceService()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        _services.AddLogging();
        _services.AddRateLimit(config);

        _services.Should().Contain(sd =>
            sd.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)
            && sd.ImplementationType == typeof(RateLimitMaintenanceService));
    }

    [Fact]
    public void AddRateLimitMiddleware_RegistersTransientMiddleware()
    {
        _services.AddRateLimitMiddleware();

        _services.Should().Contain(sd =>
            sd.ServiceType == typeof(RateLimitMiddleware)
            && sd.Lifetime == ServiceLifetime.Transient);
    }

    [Fact]
    public void ConfigureRateLimitRules_AddsRulesViaBuilder()
    {
        _services.AddLogging();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        _services.AddRateLimit(config);

        _services.ConfigureRateLimitRules(rules =>
        {
            rules.AddGlobalRule("test-global", 100, TimeSpan.FromMinutes(1));
            rules.AddEndpointRule("test-endpoint", new[] { "/api/test" }, 50, TimeSpan.FromMinutes(1));
        });

        // Verify a singleton factory registration was added for IRateLimitService
        _services.Should().Contain(sd =>
            sd.ServiceType == typeof(IRateLimitService)
            && sd.Lifetime == ServiceLifetime.Singleton);
    }

    #endregion

    #region RateLimitRuleBuilder

    [Fact]
    public void RateLimitRuleBuilder_AddGlobalRule_CreatesCorrectRule()
    {
        var builder = new RateLimitRuleBuilder();

        builder.AddGlobalRule("api-global", 1000, TimeSpan.FromHours(1));

        builder.Rules.Should().HaveCount(1);
        var rule = builder.Rules[0];
        rule.Id.Should().Be("global-api-global");
        rule.Name.Should().Be("api-global");
        rule.Configuration.RequestLimit.Should().Be(1000);
        rule.Priority.Should().Be(50);
    }

    [Fact]
    public void RateLimitRuleBuilder_AddRoleRule_SetsConditions()
    {
        var builder = new RateLimitRuleBuilder();

        builder.AddRoleRule("admin", new[] { "Admin", "SuperAdmin" }, 5000, TimeSpan.FromHours(1));

        builder.Rules.Should().HaveCount(1);
        var rule = builder.Rules[0];
        rule.Id.Should().Be("role-admin");
        rule.Conditions.UserRoles.Should().Contain("Admin");
        rule.Conditions.UserRoles.Should().Contain("SuperAdmin");
        rule.Priority.Should().Be(100);
    }

    [Fact]
    public void RateLimitRuleBuilder_AddEndpointRule_SetsEndpoints()
    {
        var builder = new RateLimitRuleBuilder();

        builder.AddEndpointRule("login", new[] { "/api/auth/login" }, 10, TimeSpan.FromMinutes(5));

        builder.Rules.Should().HaveCount(1);
        var rule = builder.Rules[0];
        rule.Id.Should().Be("endpoint-login");
        rule.Conditions.Endpoints.Should().Contain("/api/auth/login");
        rule.Priority.Should().Be(150);
    }

    [Fact]
    public void RateLimitRuleBuilder_AddBurstProtectionRule_SetsTokenBucket()
    {
        var builder = new RateLimitRuleBuilder();

        builder.AddBurstProtectionRule("burst", 50, 10.0, TimeSpan.FromSeconds(30));

        builder.Rules.Should().HaveCount(1);
        var rule = builder.Rules[0];
        rule.Id.Should().Be("burst-burst");
        rule.Configuration.Algorithm.Should().Be(RateLimitAlgorithm.TokenBucket);
        rule.Configuration.BurstLimit.Should().Be(50);
        rule.Configuration.RefillRate.Should().Be(10.0);
        rule.Priority.Should().Be(200);
    }

    [Fact]
    public void RateLimitRuleBuilder_AddTimeBasedRule_SetsTimeConditions()
    {
        var builder = new RateLimitRuleBuilder();
        var timeConditions = new TimeConditions();

        builder.AddTimeBasedRule("off-hours", timeConditions, 200, TimeSpan.FromHours(1));

        builder.Rules.Should().HaveCount(1);
        var rule = builder.Rules[0];
        rule.Id.Should().Be("time-off-hours");
        rule.Conditions.TimeConditions.Should().BeSameAs(timeConditions);
        rule.Priority.Should().Be(75);
    }

    [Fact]
    public void RateLimitRuleBuilder_AddCustomRule_AddsDirectly()
    {
        var builder = new RateLimitRuleBuilder();
        var customRule = new RateLimitRule
        {
            Id = "custom-1",
            Name = "Custom Rule",
            Configuration = new RateLimitConfiguration { RequestLimit = 42 },
            Conditions = new RateLimitConditions()
        };

        builder.AddCustomRule(customRule);

        builder.Rules.Should().HaveCount(1);
        builder.Rules[0].Should().BeSameAs(customRule);
    }

    [Fact]
    public void RateLimitRuleBuilder_FluentChaining_Works()
    {
        var builder = new RateLimitRuleBuilder();

        var result = builder
            .AddGlobalRule("g1", 100, TimeSpan.FromMinutes(1))
            .AddRoleRule("r1", new[] { "Admin" }, 500, TimeSpan.FromMinutes(1))
            .AddEndpointRule("e1", new[] { "/api" }, 50, TimeSpan.FromMinutes(1));

        result.Should().BeSameAs(builder);
        builder.Rules.Should().HaveCount(3);
    }

    #endregion

    #region SecurityHeadersExtensions (DI Registration)

    [Fact]
    public void AddSecurityHeaders_WithConfiguration_RegistersService()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        _services.AddSecurityHeaders(config);

        _services.Should().Contain(sd =>
            sd.ServiceType == typeof(ISecurityHeadersService));
    }

    [Fact]
    public void AddSecurityHeaders_WithActionOverload_RegistersService()
    {
        _services.AddSecurityHeaders(opts => { opts.EnableHsts = true; });

        _services.Should().Contain(sd =>
            sd.ServiceType == typeof(ISecurityHeadersService));
    }

    [Fact]
    public void AddSecurityHeaders_WithBuilderOverload_RegistersService()
    {
        _services.AddSecurityHeaders(builder =>
        {
            builder.ForDevelopment();
        });

        _services.Should().Contain(sd =>
            sd.ServiceType == typeof(ISecurityHeadersService));
    }

    #endregion

    #region SecurityHeadersMiddlewareExtensions (IApplicationBuilder)

    [Fact]
    public void UseSecurityHeaders_Extension_OnIApplicationBuilder_CallsUse()
    {
        var app = Substitute.For<IApplicationBuilder>();

        var result = SecurityHeadersMiddlewareExtensions.UseSecurityHeaders(app);

        result.Should().NotBeNull();
        // UseMiddleware<T> internally calls IApplicationBuilder.Use()
        app.Received().Use(Arg.Any<Func<RequestDelegate, RequestDelegate>>());
    }

    [Fact]
    public void UseSecurityHeaders_WithOptions_OnIApplicationBuilder_CallsUse()
    {
        var app = Substitute.For<IApplicationBuilder>();

        var result = SecurityHeadersMiddlewareExtensions.UseSecurityHeaders(app, opts =>
        {
            opts.EnableSecurityHeaders = true;
            opts.LogSecurityHeaders = true;
        });

        result.Should().NotBeNull();
        app.Received().Use(Arg.Any<Func<RequestDelegate, RequestDelegate>>());
    }

    #endregion

    #region SecurityAuditExtensions (DI Registration)

    [Fact]
    public void AddSecurityAudit_WithoutRedis_RegistersInMemoryService()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        _services.AddLogging();
        _services.AddSecurityAudit(config);

        var provider = _services.BuildServiceProvider();
        var auditService = provider.GetService<ISecurityAuditService>();
        auditService.Should().NotBeNull();
        auditService.Should().BeOfType<InMemorySecurityAuditService>();
    }

    [Fact]
    public void AddSecurityAudit_RegistersAuditMiddleware()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        _services.AddLogging();
        _services.AddSecurityAudit(config);

        _services.Should().Contain(sd =>
            sd.ServiceType == typeof(AuditSecurityAuditMiddleware));
    }

    [Fact]
    public void AddSecurityAudit_RegistersMaintenanceHostedService()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        _services.AddLogging();
        _services.AddSecurityAudit(config);

        _services.Should().Contain(sd =>
            sd.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)
            && sd.ImplementationType == typeof(SecurityAuditMaintenanceService));
    }

    [Fact]
    public void AddSecurityAuditMiddleware_RegistersTransient()
    {
        _services.AddSecurityAuditMiddleware();

        _services.Should().Contain(sd =>
            sd.ServiceType == typeof(AuditSecurityAuditMiddleware)
            && sd.Lifetime == ServiceLifetime.Transient);
    }

    #endregion

    #region SecurityAuditMiddlewareExtensions (IApplicationBuilder)

    [Fact]
    public void UseSecurityAudit_Extension_OnIApplicationBuilder_CallsUse()
    {
        var app = Substitute.For<IApplicationBuilder>();

        var result = SecurityAuditMiddlewareExtensions.UseSecurityAudit(app);

        result.Should().NotBeNull();
        app.Received().Use(Arg.Any<Func<RequestDelegate, RequestDelegate>>());
    }

    #endregion

    #region Helpers

    private (InfrastructureMiddlewareBuilder mwBuilder, IApplicationBuilder app) CreateMiddlewareBuilder()
    {
        var app = Substitute.For<IApplicationBuilder>();
        var mwBuilder = new InfrastructureMiddlewareBuilder(app, _environment, "TestService");
        return (mwBuilder, app);
    }

    private (InfrastructureMiddlewareBuilder mwBuilder, IApplicationBuilder app) CreateMiddlewareBuilderWithServices(
        Action<IServiceCollection> configureServices)
    {
        var services = new ServiceCollection();
        configureServices(services);
        var serviceProvider = services.BuildServiceProvider();

        var app = Substitute.For<IApplicationBuilder>();
        app.ApplicationServices.Returns(serviceProvider);
        var mwBuilder = new InfrastructureMiddlewareBuilder(app, _environment, "TestService");
        return (mwBuilder, app);
    }

    #endregion
}
