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
    public static string Of(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Connection.RemoteIpAddress?.ToString() ?? Unknown;
    }
}
