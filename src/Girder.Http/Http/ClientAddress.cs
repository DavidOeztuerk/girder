using Microsoft.AspNetCore.Http;

namespace Girder.Infrastructure.Http;

/// <summary>
/// Who the caller is, as far as the connection can prove.
/// </summary>
/// <remarks>
/// One place, because the answer decides who a rate limit counts, whose address
/// an audit entry names, and which caller a security alert accuses. A component
/// that reads <c>X-Forwarded-For</c> for itself is reading something the caller
/// wrote, and the consequences differ per component only in how long it takes to
/// notice.
/// <para>
/// Behind a proxy the connection is the proxy, and the address wanted is one hop
/// further out. That is what <see cref="ForwardedHeaderTrust"/> is for: a named
/// proxy list lets the platform rewrite
/// <see cref="ConnectionInfo.RemoteIpAddress"/>, and this keeps reading the same
/// property either way.
/// </para>
/// </remarks>
public static class ClientAddress
{
    /// <summary>What is reported when the connection has no address at all.</summary>
    /// <remarks>
    /// Reachable in-process, and for a rate limit it is one shared bucket rather
    /// than an exemption — the safe direction for a value nobody vouched for.
    /// </remarks>
    public const string Unknown = "unknown";

    /// <summary>The caller's address, or <see cref="Unknown"/>.</summary>
    /// <remarks>
    /// An IPv4 address that arrived over a dual-stack socket is written back as
    /// IPv4. The kernel reports it mapped — <c>::ffff:10.0.0.1</c> — and whether
    /// it does depends on the listener, not the caller; left alone, one machine
    /// gets a bucket under each spelling and therefore twice the allowance it was
    /// given, and an audit entry names it two ways.
    /// </remarks>
    /// <param name="context">The request being served.</param>
    public static string Of(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var address = context.Connection.RemoteIpAddress;
        if (address is null)
        {
            return Unknown;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.ToString();
    }
}
