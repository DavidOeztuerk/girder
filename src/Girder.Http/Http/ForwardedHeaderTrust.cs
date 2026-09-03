using System.Net;
using IPNetwork = System.Net.IPNetwork;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;

namespace Girder.Infrastructure.Http;

/// <summary>
/// Present in the container once forwarded headers may be believed, and from
/// whom.
/// </summary>
/// <remarks>
/// A marker rather than a flag on an options class: the pipeline step has to
/// know whether to run at all, and an options value read at request time would
/// answer too late.
/// </remarks>
public sealed class ForwardedHeaderTrust
{
    internal ForwardedHeaderTrust(IReadOnlyList<string> proxies) => Proxies = proxies;

    /// <summary>The proxies named by the application, as it wrote them.</summary>
    public IReadOnlyList<string> Proxies { get; }
}

/// <summary>
/// Declares which proxies may speak for the caller.
/// </summary>
public static class ForwardedHeaderExtensions
{
    /// <summary>
    /// Believes <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c>, but only
    /// from the addresses named here.
    /// </summary>
    /// <remarks>
    /// Without this call no forwarded header is read anywhere in Girder, and
    /// every component sees the connection's own address. That is the default
    /// because the alternative has no floor: a header the caller writes is worth
    /// exactly what the caller wants it to be worth, and a rate limit keyed on
    /// one is a rate limit anybody resets.
    /// <para>
    /// The platform's own middleware does the rewriting, so the trust list is
    /// <see cref="ForwardedHeadersOptions.KnownProxies"/> and
    /// <see cref="ForwardedHeadersOptions.KnownIPNetworks"/>. Both are cleared
    /// first: they carry loopback out of the box, and a default that arrives
    /// unasked is not a declaration.
    /// </para>
    /// <para>
    /// <c>X-Real-IP</c> is not read. It is not part of the platform's set, and a
    /// second parser would be a second thing to get right. A proxy that sets only
    /// that header can say so through <paramref name="configure"/>:
    /// <c>options.ForwardedForHeaderName = "X-Real-IP"</c>.
    /// </para>
    /// </remarks>
    /// <param name="services">The container.</param>
    /// <param name="proxies">
    /// Addresses (<c>10.0.0.7</c>) or networks in CIDR form
    /// (<c>10.0.0.0/8</c>). At least one — an empty list would be a call that
    /// reads as trust and grants none.
    /// </param>
    /// <param name="configure">
    /// The platform's own options, for anything this does not settle:
    /// <c>ForwardLimit</c>, a different header name, additional forwarded
    /// values.
    /// </param>
    public static IServiceCollection TrustForwardedHeadersFrom(
        this IServiceCollection services,
        string[] proxies,
        Action<ForwardedHeadersOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(proxies);

        if (proxies.Length == 0)
        {
            throw new ArgumentException(
                "Name at least one proxy whose forwarded headers may be believed, "
                + "as an address (10.0.0.7) or a network (10.0.0.0/8). "
                + "To trust none, do not call TrustForwardedHeadersFrom at all.",
                nameof(proxies));
        }

        var addresses = new List<IPAddress>();
        var networks = new List<IPNetwork>();

        foreach (var proxy in proxies)
        {
            if (proxy.Contains('/', StringComparison.Ordinal))
            {
                networks.Add(ParseNetwork(proxy));
            }
            else if (IPAddress.TryParse(proxy, out var address))
            {
                addresses.Add(address);
            }
            else
            {
                throw new ArgumentException(
                    $"'{proxy}' is neither an address nor a CIDR network.", nameof(proxies));
            }
        }

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();

            foreach (var address in addresses)
            {
                options.KnownProxies.Add(address);
            }

            foreach (var network in networks)
            {
                options.KnownIPNetworks.Add(network);
            }

            configure?.Invoke(options);
        });

        services.AddSingleton(new ForwardedHeaderTrust(proxies));
        return services;
    }

    private static IPNetwork ParseNetwork(string cidr)
    {
        var parts = cidr.Split('/', 2);

        if (!IPAddress.TryParse(parts[0], out var prefix)
            || !int.TryParse(parts[1], out var length))
        {
            throw new ArgumentException($"'{cidr}' is not a CIDR network.", nameof(cidr));
        }

        return new IPNetwork(prefix, length);
    }
}
