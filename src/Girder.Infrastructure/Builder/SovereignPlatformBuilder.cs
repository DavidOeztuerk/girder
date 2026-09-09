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
    private Action<IServiceCollection>? _sink;

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
    /// Stops trusting loopback.
    /// </summary>
    /// <remarks>
    /// Loopback is allowed by default because a sidecar, an agent or a local
    /// proxy is the usual reason a sovereign deployment calls out at all. Where
    /// there is none, saying so closes a door nobody needs.
    /// </remarks>
    public SovereignPlatformBuilder WithoutLoopback()
    {
        _allowLoopback = false;
        return this;
    }

    /// <summary>
    /// Stops trusting the private ranges.
    /// </summary>
    /// <remarks>
    /// RFC1918 is allowed by default because the database, the cache and the
    /// broker live there. A service that reaches them only through named hosts
    /// can close the ranges and keep the names.
    /// </remarks>
    public SovereignPlatformBuilder WithoutPrivateNetworks()
    {
        _allowPrivateNetworks = false;
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
        _sink = services => services.AddSovereignAuditSink<TSink>();
        return this;
    }

    /// <summary>
    /// Hands the whole bundle to the composition as one module.
    /// </summary>
    /// <remarks>
    /// Registered through <c>Use(module, register)</c> rather than straight into
    /// the container, so it appears in <c>GirderComposition</c> like everything
    /// else and a service that deliberately calls outward can drop it with a
    /// reason. Nothing is set up for a module that was dropped — an egress
    /// boundary registered anyway would go on refusing the calls that reason
    /// allowed for.
    /// </remarks>
    internal void Apply() => _girder.Use(GirderModule.SovereignPlatform, girder =>
    {
        // Logging carries the masking enricher, and it is a module of its own.
        girder.Use(GirderModule.Logging);

        girder.Services.AddGirderEgressPolicy(policy =>
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

        girder.Services.AddGirderSovereigntyReport([.. _declaredDependencies]);

        _sink?.Invoke(girder.Services);
        girder.Services.AddSovereignAuditTrail();
    });
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
