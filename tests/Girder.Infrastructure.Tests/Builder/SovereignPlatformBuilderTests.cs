using FluentAssertions;
using Girder.Abstractions.Hosting;
using Girder.Infrastructure.Audit;
using Girder.Infrastructure.Builder;
using Girder.Infrastructure.Extensions;
using Girder.Infrastructure.Sovereignty;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Girder.Infrastructure.Tests.Builder;

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

        services.AddGirder(config, env, "sovereign-service", girder => girder
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
        var composition = provider.GetRequiredService<GirderComposition>();
        composition.Included.Should().Contain(GirderModule.Logging);
    }

    [Fact]
    public void AddSovereignPlatform_returns_builder_for_fluent_chaining()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var env = new Umgebungsstub();

        GirderBuilder? capturedBuilder = null;

        services.AddGirder(config, env, "fluent-service", girder =>
        {
            var result = girder.AddSovereignPlatform();
            capturedBuilder = result;
            result.Should().BeSameAs(girder);
        });

        capturedBuilder.Should().NotBeNull();
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
