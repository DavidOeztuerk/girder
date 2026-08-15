using System.Net;

namespace Girder.Infrastructure.Sovereignty;

/// <summary>
/// An <see cref="IEgressPolicy"/> built from a list of declared hosts.
/// </summary>
public sealed class EgressPolicy : IEgressPolicy
{
    private readonly HashSet<string> _exactHosts;
    private readonly string[] _suffixes;
    private readonly bool _allowLoopback;
    private readonly bool _allowPrivateNetworks;

    internal EgressPolicy(
        IEnumerable<string> exactHosts,
        IEnumerable<string> suffixes,
        bool allowLoopback,
        bool allowPrivateNetworks)
    {
        _exactHosts = new HashSet<string>(exactHosts, StringComparer.OrdinalIgnoreCase);
        _suffixes = suffixes.ToArray();
        _allowLoopback = allowLoopback;
        _allowPrivateNetworks = allowPrivateNetworks;

        DeclaredHosts = _exactHosts
            .Concat(_suffixes.Select(s => $"*{s}"))
            .Concat(allowLoopback ? ["<loopback>"] : Array.Empty<string>())
            .Concat(allowPrivateNetworks ? ["<private>"] : Array.Empty<string>())
            .ToArray();
    }

    /// <summary>A policy that declares nothing and therefore allows everything.</summary>
    public static IEgressPolicy Unrestricted { get; } = new EgressPolicy([], [], false, false);

    /// <inheritdoc />
    public IReadOnlyCollection<string> DeclaredHosts { get; }

    /// <inheritdoc />
    public bool IsEnforcing => DeclaredHosts.Count > 0;

    /// <inheritdoc />
    public bool IsAllowed(Uri destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (!IsEnforcing)
        {
            return true;
        }

        var host = destination.Host;

        if (_exactHosts.Contains(host))
        {
            return true;
        }

        foreach (var suffix in _suffixes)
        {
            if (host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return IPAddress.TryParse(host, out var address)
            ? (_allowLoopback && IPAddress.IsLoopback(address))
              || (_allowPrivateNetworks && IsPrivate(address))
            : _allowLoopback && IsLoopbackName(host);
    }

    private static bool IsLoopbackName(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase);

    /// <summary>RFC 1918 and RFC 4193 ranges — networks you run yourself.</summary>
    private static bool IsPrivate(IPAddress address)
    {
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            // fc00::/7, unique local addresses.
            return (address.GetAddressBytes()[0] & 0xFE) == 0xFC;
        }

        var octets = address.GetAddressBytes();

        return octets[0] switch
        {
            10 => true,
            172 => octets[1] >= 16 && octets[1] <= 31,
            192 => octets[1] == 168,
            _ => false
        };
    }
}
