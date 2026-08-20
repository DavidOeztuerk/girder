using Girder.Infrastructure.Security.Headers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Girder.Infrastructure.Tests.Security;

/// <summary>
/// A JSON response must still say that nothing may frame it.
/// </summary>
/// <remarks>
/// <c>X-Frame-Options</c> is dropped for API responses, which is right — it is
/// the legacy header. What replaced it has to take its place, and
/// <c>frame-ancestors</c> does <b>not</b> fall back to <c>default-src</c>: a
/// policy of <c>default-src 'none'</c> alone permits framing.
/// </remarks>
[Trait("Category", "Unit")]
public class ApiResponseFramingTests
{
    private readonly SecurityHeadersService _service = new(
        NullLogger<SecurityHeadersService>.Instance,
        Options.Create(new SecurityHeadersOptions()));

    [Fact]
    public void An_api_response_forbids_framing_through_the_policy()
    {
        var headers = _service.GetSecurityHeaders(ApiContext());

        headers["Content-Security-Policy"].Should().Contain("frame-ancestors 'none'");
    }

    [Fact]
    public void An_api_response_drops_the_legacy_header()
    {
        var headers = _service.GetSecurityHeaders(ApiContext());

        headers.Should().NotContainKey("X-Frame-Options");
    }

    /// <summary>
    /// The analysis must then find nothing: Girder used to remove the header
    /// and report it missing in the same response.
    /// </summary>
    [Fact]
    public void And_the_analysis_reports_no_missing_framing_protection()
    {
        var headers = _service.GetSecurityHeaders(ApiContext());

        var result = _service.AnalyzeSecurityHeaders(new Dictionary<string, string>(headers));

        result.MissingHeaders.Should().NotContain("X-Frame-Options");
    }

    private static SecurityHeadersContext ApiContext() => new() { IsApiRequest = true };
}
