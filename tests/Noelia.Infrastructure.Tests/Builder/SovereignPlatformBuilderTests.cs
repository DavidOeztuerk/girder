using FluentAssertions;
using Noelia.Abstractions.Hosting;
using Noelia.Infrastructure.Audit;
using Noelia.Abstractions.Audit;
using Noelia.Abstractions.Sovereignty;
using Noelia.Infrastructure.Builder;
using Noelia.Infrastructure.Extensions;
using Noelia.Infrastructure.Sovereignty;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Noelia.Infrastructure.Tests.Builder;

[Trait("Category", "Unit")]
public class SovereignPlatformBuilderTests
{
    [Fact]
    public void AddSovereignPlatform_registers_sovereign_defaults()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = "Host=postgres.internal;Database=app"
        }).Build();

        var env = new Umgebungsstub();

        // A host registers this; a bare ServiceCollection does not, and the
        // sovereignty report reads connection strings out of it.
        services.AddSingleton<IConfiguration>(config);

        services.AddNoelia(config, env, "sovereign-service", noelia => noelia
            .UseDefaults()
            .AddSovereignPlatform(sovereign => sovereign
                .Allow("openbao.internal")
                .DeclareDependency("Telemetry", "http://collector.local:4317")));

        var provider = services.BuildServiceProvider();

        // 1. Strict Egress Policy is registered and enforcing
        var egressPolicy = provider.GetService<IEgressPolicy>();
        egressPolicy.Should().NotBeNull();
        egressPolicy!.IsEnforcing.Should().BeTrue();
        egressPolicy.IsAllowed(new Uri("http://localhost:8080")).Should().BeTrue();
        egressPolicy.IsAllowed(new Uri("http://10.0.0.5:5000")).Should().BeTrue();
        egressPolicy.IsAllowed(new Uri("http://openbao.internal:8200")).Should().BeTrue();
        egressPolicy.IsAllowed(new Uri("https://third-party-cloud.com")).Should().BeFalse();

        // 2. Sovereignty Report is registered and functional
        var report = provider.GetService<ISovereigntyReport>();
        report.Should().NotBeNull();
        var assessment = report!.Assess();
        assessment.Dependencies.Should().Contain(f => f.Name == "Database");
        assessment.Dependencies.Should().Contain(f => f.Name == "Telemetry");

        // 3. Audit Trail Service and Sink are registered
        var auditService = provider.GetService<IAuditTrailService>();
        auditService.Should().NotBeNull();
        var auditSink = provider.GetService<ISovereignAuditSink>();
        auditSink.Should().NotBeNull();

        // 4. Logging module is included in composition
        var composition = provider.GetRequiredService<NoeliaComposition>();
        composition.Included.Should().Contain(NoeliaModule.Logging);
    }

    [Fact]
    public void AddSovereignPlatform_returns_builder_for_fluent_chaining()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var env = new Umgebungsstub();

        NoeliaBuilder? capturedBuilder = null;

        services.AddNoelia(config, env, "fluent-service", noelia =>
        {
            var result = noelia.AddSovereignPlatform();
            capturedBuilder = result;
            result.Should().BeSameAs(noelia);
        });

        capturedBuilder.Should().NotBeNull();
    }

    [Fact]
    public void AddSovereignPlatform_without_defaults_selects_logging_before_build()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        services.AddSingleton<IConfiguration>(config);

        services.AddNoelia(
            config,
            new Umgebungsstub(),
            "sovereign-service",
            noelia => noelia.AddSovereignPlatform());

        var composition = services.BuildServiceProvider().GetRequiredService<NoeliaComposition>();
        composition.Included.Should().Equal(
            NoeliaModule.Logging,
            NoeliaModule.SovereignPlatform);
    }

    private sealed class Umgebungsstub : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "sovereign-service";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
