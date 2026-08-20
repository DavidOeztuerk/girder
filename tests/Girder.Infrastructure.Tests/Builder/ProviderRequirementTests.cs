using Girder.Abstractions.Caching;
using Girder.Infrastructure.Builder;
using Girder.Infrastructure.Builder.Modules;
using Girder.Infrastructure.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Girder.Infrastructure.Tests.Builder;

/// <summary>
/// A module that needs a provider must say so before the first request, not
/// during it.
/// </summary>
/// <remarks>
/// The container resolves lazily, so an unregistered provider used to surface
/// as a 500 on whichever request first touched it — in production, naming a
/// type the caller never heard of. These tests pin that the failure happens at
/// startup and names the call that fixes it.
/// </remarks>
[Trait("Category", "Unit")]
public class ProviderRequirementTests
{
    private static WebApplication BuildApp(Action<InfrastructureBuilder> modules,
                                           Action<IServiceCollection>? providers = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSharedInfrastructure(
            builder.Configuration, builder.Environment, "test", modules);
        providers?.Invoke(builder.Services);

        // Scope validation on, as it is in Development: resolving a scoped
        // service from the root provider then throws rather than answering.
        builder.Host.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = true;
            options.ValidateOnBuild = false;
        });

        return builder.Build();
    }

    /// <summary>Runs the startup filters the way the host does.</summary>
    private static Action ConfigurePipeline(WebApplication app) => () =>
    {
        foreach (var filter in app.Services.GetServices<IStartupFilter>())
        {
            filter.Configure(_ => { })(app);
        }
    };

    [Fact]
    public void A_module_without_its_provider_refuses_to_start()
    {
        var app = BuildApp(infra => infra.AddCaching());

        ConfigurePipeline(app).Should().Throw<InvalidOperationException>()
            .WithMessage("*AddCaching()*IDistributedCacheService*");
    }

    [Fact]
    public void The_message_names_the_call_that_fixes_it()
    {
        // A diagnostic that says what is missing but not what to do about it
        // sends the reader into the source of a package they did not write.
        var app = BuildApp(infra => infra.AddCaching());

        ConfigurePipeline(app).Should().Throw<InvalidOperationException>()
            .WithMessage("*AddInMemoryCache*");
    }

    [Fact]
    public void With_the_provider_present_startup_proceeds()
    {
        var app = BuildApp(
            infra => infra.AddCaching(),
            services => services.AddSingleton(Substitute.For<IDistributedCacheService>()));

        ConfigurePipeline(app).Should().NotThrow();
    }

    [Fact]
    public void Every_missing_provider_is_reported_at_once()
    {
        // Reporting one at a time turns a five-minute fix into five restarts.
        var app = BuildApp(infra => infra.AddCaching().AddSecurityMonitoring());

        ConfigurePipeline(app).Should().Throw<InvalidOperationException>()
            .WithMessage("*2 provider registration*");
    }

    [Theory]
    [InlineData("AddSecurityHeaders")]
    [InlineData("AddHealthChecks")]
    [InlineData("AddObservability")]
    [InlineData("AddResilience")]
    [InlineData("AddInputSanitization")]
    [InlineData("AddAuditLogging")]
    [InlineData("AddAuthorization")]
    [InlineData("AddResourceAuthorization")]
    [InlineData("AddDistributedRateLimiting")]
    [InlineData("AddSecretManagement")]
    public void A_module_that_needs_no_provider_stands_on_its_own(string moduleName)
    {
        // Girder's promise is that a service takes only what it runs. A module
        // that quietly needs another one breaks that promise.
        Action<InfrastructureBuilder> module = moduleName switch
        {
            "AddSecurityHeaders" => b => b.AddSecurityHeaders(),
            "AddHealthChecks" => b => b.AddHealthChecks(),
            "AddObservability" => b => b.AddObservability(),
            "AddResilience" => b => b.AddResilience(),
            "AddInputSanitization" => b => b.AddInputSanitization(),
            "AddAuditLogging" => b => b.AddAuditLogging(),
            "AddAuthorization" => b => b.AddAuthorization(),
            "AddResourceAuthorization" => b => b.AddResourceAuthorization(),
            "AddDistributedRateLimiting" => b => b.AddDistributedRateLimiting(),
            "AddSecretManagement" => b => b.AddSecretManagement(),
            _ => throw new ArgumentOutOfRangeException(nameof(moduleName))
        };

        var app = BuildApp(module);

        ConfigurePipeline(app).Should().NotThrow();
    }

    /// <summary>
    /// A scoped provider satisfies a requirement just as a singleton does.
    /// </summary>
    /// <remarks>
    /// The check used to resolve the service from the root provider, which
    /// throws outright for a scoped registration when scope validation is on —
    /// so a correctly wired service crashed at startup instead of starting.
    /// Asking whether the type is registered avoids building anything at all.
    /// </remarks>
    [Fact]
    public void A_scoped_provider_satisfies_a_requirement()
    {
        var app = BuildApp(
            infra => infra.AddCaching(),
            services => services.AddScoped(_ => Substitute.For<IDistributedCacheService>()));

        ConfigurePipeline(app).Should().NotThrow();
    }

    [Fact]
    public void A_missing_scoped_provider_is_still_reported()
    {
        var app = BuildApp(infra => infra.AddCaching());

        ConfigurePipeline(app).Should().Throw<InvalidOperationException>()
            .WithMessage("*IDistributedCacheService*");
    }
}
