using Girder.Abstractions.Hosting;
using Girder.Infrastructure.Audit;
using Girder.Infrastructure.Sovereignty;
using Microsoft.Extensions.DependencyInjection;

namespace Girder.Infrastructure.Builder;

/// <summary>
/// Fluent configuration builder for sovereign platform defaults.
/// </summary>
public sealed class SovereignPlatformBuilder
{
    private readonly GirderBuilder _girder;
    private readonly List<Action<EgressPolicyBuilder>> _egressConfigurators = [];
    private readonly List<DeclaredDependency> _declaredDependencies = [];
    private bool _allowLoopback = true;
    private bool _allowPrivateNetworks = true;

    internal SovereignPlatformBuilder(GirderBuilder girder)
    {
        _girder = girder;
    }

    /// <summary>
    /// Allows egress calls to specific hostnames.
    /// </summary>
    public SovereignPlatformBuilder Allow(params string[] hosts)
    {
        ArgumentNullException.ThrowIfNull(hosts);
        _egressConfigurators.Add(p => p.Allow(hosts));
        return this;
    }

    /// <summary>
    /// Allows egress calls to subdomains of the given domains.
    /// </summary>
    public SovereignPlatformBuilder AllowSubdomainsOf(params string[] domains)
    {
        ArgumentNullException.ThrowIfNull(domains);
        _egressConfigurators.Add(p => p.AllowSubdomainsOf(domains));
        return this;
    }

    /// <summary>
    /// Customizes the egress policy directly.
    /// </summary>
    public SovereignPlatformBuilder ConfigureEgress(Action<EgressPolicyBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _egressConfigurators.Add(configure);
        return this;
    }

    /// <summary>
    /// Declares an external dependency for the sovereignty report.
    /// </summary>
    public SovereignPlatformBuilder DeclareDependency(string name, string? endpoint)
    {
        _declaredDependencies.Add(new DeclaredDependency(name, endpoint));
        return this;
    }

    /// <summary>
    /// Configures a custom destination sink for the sovereign audit trail.
    /// </summary>
    public SovereignPlatformBuilder WithAuditSink<TSink>()
        where TSink : class, ISovereignAuditSink
    {
        _girder.Services.AddSovereignAuditSink<TSink>();
        return this;
    }

    internal void Apply()
    {
        // Ensure configuration is accessible in DI for SovereigntyReport
        _girder.Services.AddSingleton(_girder.Configuration);

        // 1. Strict Egress: loopback + private networks by default, all undeclared calls fail-closed
        _girder.Services.AddGirderEgressPolicy(policy =>
        {
            if (_allowLoopback)
            {
                policy.AllowLoopback();
            }

            if (_allowPrivateNetworks)
            {
                policy.AllowPrivateNetworks();
            }

            foreach (var configure in _egressConfigurators)
            {
                configure(policy);
            }
        });

        // 2. Data Masking: ensure logging module with DataMaskingEnricher is active
        _girder.Use(GirderModule.Logging);

        // 3. Sovereignty Report: inspects configuration, endpoints and egress boundaries
        _girder.Services.AddGirderSovereigntyReport([.. _declaredDependencies]);

        // 4. Sovereign Audit Trail: tamper-evident hash-chained audit logging
        _girder.Services.AddSovereignAuditTrail();
    }
}

/// <summary>
/// Extension methods for sovereign platform integration on <see cref="GirderBuilder"/>.
/// </summary>
public static class SovereignPlatformExtensions
{
    /// <summary>
    /// Bundles GirderBuilder with sovereign defaults:
    /// strict egress policy, PII data masking in logs, sovereignty report, and audit trail.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddGirder(config, env, "identity", girder => girder
    ///     .UseDefaults()
    ///     .AddSovereignPlatform(sovereign => sovereign
    ///         .Allow("openbao.internal")
    ///         .DeclareDependency("Secrets", config["OpenBao:Address"])));
    /// </code>
    /// </example>
    public static GirderBuilder AddSovereignPlatform(
        this GirderBuilder girder,
        Action<SovereignPlatformBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(girder);

        var builder = new SovereignPlatformBuilder(girder);
        configure?.Invoke(builder);
        builder.Apply();

        return girder;
    }
}
