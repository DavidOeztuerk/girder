using Girder.Infrastructure.Security.Headers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Girder.Infrastructure.Tests.Security;

/// <summary>
/// The header analysis must report what is actually wrong, not the distance to
/// a fixed ideal.
/// </summary>
/// <remarks>
/// A warning that fires on every response of a correctly configured service is
/// one every operator turns off, and then it protects nobody.
/// </remarks>
[Trait("Category", "Unit")]
public class HeaderAnalysisRelevanceTests
{
    private readonly SecurityHeadersService _service = new(
        NullLogger<SecurityHeadersService>.Instance,
        Options.Create(new SecurityHeadersOptions()));

    /// <summary>
    /// Browsers removed the XSS auditor this header controlled, and OWASP
    /// advises against sending it.
    /// </summary>
    [Fact]
    public void X_XSS_Protection_is_not_asked_for()
    {
        var result = _service.AnalyzeSecurityHeaders(Complete());

        result.MissingHeaders.Should().NotContain("X-XSS-Protection");
    }

    /// <summary>
    /// A response that carries every header worth carrying scores full marks.
    /// </summary>
    [Fact]
    public void A_correctly_headed_response_reports_nothing()
    {
        var result = _service.AnalyzeSecurityHeaders(Complete());

        result.MissingHeaders.Should().BeEmpty();
        result.Vulnerabilities.Should().BeEmpty();
        result.OverallScore.Should().BeGreaterThanOrEqualTo(80);
    }

    /// <summary>
    /// <c>frame-ancestors</c> is what replaced <c>X-Frame-Options</c>, and a
    /// response carrying it is not missing framing protection.
    /// </summary>
    [Fact]
    public void Frame_ancestors_stands_in_for_X_Frame_Options()
    {
        var headers = Complete();
        headers.Remove("X-Frame-Options");

        var result = _service.AnalyzeSecurityHeaders(headers);

        result.MissingHeaders.Should().NotContain("X-Frame-Options");
    }

    /// <summary>
    /// Without <c>frame-ancestors</c> the legacy header is still the only
    /// framing protection there is, so its absence is worth saying.
    /// </summary>
    [Fact]
    public void Without_either_the_missing_framing_protection_is_reported()
    {
        var headers = Complete();
        headers.Remove("X-Frame-Options");
        headers["Content-Security-Policy"] = "default-src 'none'";

        var result = _service.AnalyzeSecurityHeaders(headers);

        result.MissingHeaders.Should().Contain("X-Frame-Options");
    }

    private static Dictionary<string, string> Complete() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'",
        ["X-Frame-Options"] = "DENY",
        ["X-Content-Type-Options"] = "nosniff",
        ["Strict-Transport-Security"] = "max-age=31536000",
        ["Referrer-Policy"] = "no-referrer"
    };
}
