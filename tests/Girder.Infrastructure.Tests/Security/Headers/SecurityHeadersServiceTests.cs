using Girder.Infrastructure.Security.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Girder.Infrastructure.Tests.Security.Headers;

[Trait("Category", "Unit")]
public class SecurityHeadersServiceTests
{
    private readonly SecurityHeadersService _sut;
    private readonly ILogger<SecurityHeadersService> _logger = Substitute.For<ILogger<SecurityHeadersService>>();

    public SecurityHeadersServiceTests()
    {
        var options = Options.Create(new SecurityHeadersOptions());
        _sut = new SecurityHeadersService(_logger, options);
    }

    #region GetDefaultSecurityHeaders

    [Fact]
    public void GetDefaultSecurityHeaders_ReturnsExpectedHeaders()
    {
        var headers = _sut.GetDefaultSecurityHeaders();

        headers.Should().ContainKey("X-Content-Type-Options");
        headers["X-Content-Type-Options"].Should().Be("nosniff");
        headers.Should().ContainKey("X-Frame-Options");
        headers["X-Frame-Options"].Should().Be("DENY");
        headers.Should().ContainKey("X-XSS-Protection");
        headers["X-XSS-Protection"].Should().Be("1; mode=block");
        headers.Should().ContainKey("Referrer-Policy");
        headers["Referrer-Policy"].Should().Be("strict-origin-when-cross-origin");
    }

    [Fact]
    public void GetDefaultSecurityHeaders_HstsEnabled_IncludesHsts()
    {
        var headers = _sut.GetDefaultSecurityHeaders();

        headers.Should().ContainKey("Strict-Transport-Security");
        headers["Strict-Transport-Security"].Should().Contain("max-age=");
    }

    [Fact]
    public void GetDefaultSecurityHeaders_HstsDisabled_ExcludesHsts()
    {
        var options = Options.Create(new SecurityHeadersOptions { EnableHsts = false });
        var sut = new SecurityHeadersService(_logger, options);

        var headers = sut.GetDefaultSecurityHeaders();

        headers.Should().NotContainKey("Strict-Transport-Security");
    }

    [Fact]
    public void GetDefaultSecurityHeaders_HstsWithSubdomainsAndPreload_IncludesAll()
    {
        var options = Options.Create(new SecurityHeadersOptions
        {
            EnableHsts = true,
            HstsIncludeSubDomains = true,
            HstsPreload = true,
            HstsMaxAge = 31536000
        });
        var sut = new SecurityHeadersService(_logger, options);

        var headers = sut.GetDefaultSecurityHeaders();

        headers["Strict-Transport-Security"].Should().Contain("includeSubDomains");
        headers["Strict-Transport-Security"].Should().Contain("preload");
    }

    [Fact]
    public void GetDefaultSecurityHeaders_PermissionsPolicyEnabled_IncludesHeader()
    {
        var headers = _sut.GetDefaultSecurityHeaders();

        headers.Should().ContainKey("Permissions-Policy");
        headers["Permissions-Policy"].Should().Contain("camera=()");
        headers["Permissions-Policy"].Should().Contain("microphone=()");
    }

    [Fact]
    public void GetDefaultSecurityHeaders_CrossOriginPolicies_IncludedWhenEnabled()
    {
        var options = Options.Create(new SecurityHeadersOptions
        {
            EnableCrossOriginEmbedderPolicy = true,
            EnableCrossOriginOpenerPolicy = true,
            EnableCrossOriginResourcePolicy = true
        });
        var sut = new SecurityHeadersService(_logger, options);

        var headers = sut.GetDefaultSecurityHeaders();

        headers.Should().ContainKey("Cross-Origin-Embedder-Policy");
        headers["Cross-Origin-Embedder-Policy"].Should().Be("require-corp");
        headers.Should().ContainKey("Cross-Origin-Opener-Policy");
        headers["Cross-Origin-Opener-Policy"].Should().Be("same-origin");
        headers.Should().ContainKey("Cross-Origin-Resource-Policy");
        headers["Cross-Origin-Resource-Policy"].Should().Be("same-origin");
    }

    [Fact]
    public void GetDefaultSecurityHeaders_DefaultCspEnabled_IncludesCsp()
    {
        var headers = _sut.GetDefaultSecurityHeaders();

        headers.Should().ContainKey("Content-Security-Policy");
        headers["Content-Security-Policy"].Should().Contain("default-src");
    }

    [Fact]
    public void GetDefaultSecurityHeaders_DefaultCspDisabled_ExcludesCsp()
    {
        var options = Options.Create(new SecurityHeadersOptions { EnableDefaultCsp = false });
        var sut = new SecurityHeadersService(_logger, options);

        var headers = sut.GetDefaultSecurityHeaders();

        headers.Should().NotContainKey("Content-Security-Policy");
    }

    #endregion

    #region GetSecurityHeaders

    [Fact]
    public void GetSecurityHeaders_ApiRequest_RemovesFrameOptionsAndSetsStrictCsp()
    {
        var context = new SecurityHeadersContext { IsApiRequest = true };

        var headers = _sut.GetSecurityHeaders(context);

        headers.Should().NotContainKey("X-Frame-Options");
        headers.Should().ContainKey("Content-Security-Policy");
        headers["Content-Security-Policy"].Should().Contain("'none'");
    }

    [Fact]
    public void GetSecurityHeaders_StaticContent_MinimalHeaders()
    {
        var context = new SecurityHeadersContext { IsStaticContent = true };

        var headers = _sut.GetSecurityHeaders(context);

        headers.Should().ContainKey("X-Content-Type-Options");
        headers.Should().ContainKey("Cache-Control");
        headers["Cache-Control"].Should().Contain("immutable");
        headers.Should().NotContainKey("X-Frame-Options");
    }

    [Fact]
    public void GetSecurityHeaders_RegularRequest_IncludesAllDefaults()
    {
        var context = new SecurityHeadersContext();

        var headers = _sut.GetSecurityHeaders(context);

        headers.Should().ContainKey("X-Content-Type-Options");
        headers.Should().ContainKey("X-Frame-Options");
        headers.Should().ContainKey("Referrer-Policy");
    }

    [Fact]
    public void GetSecurityHeaders_WithCustomRequirements_AddsCustomHeaders()
    {
        var context = new SecurityHeadersContext
        {
            CustomRequirements = new Dictionary<string, object?>
            {
                ["X-Custom-Header"] = "custom-value"
            }
        };

        var headers = _sut.GetSecurityHeaders(context);

        headers.Should().ContainKey("X-Custom-Header");
        headers["X-Custom-Header"].Should().Be("custom-value");
    }

    #endregion

    #region BuildContentSecurityPolicy

    [Fact]
    public void BuildContentSecurityPolicy_ValidBuilder_ReturnsCspString()
    {
        var builder = new ContentSecurityPolicyBuilder()
            .DefaultSource(CspSources.Self)
            .ScriptSource(CspSources.Self);

        var result = _sut.BuildContentSecurityPolicy(builder);

        result.Should().Contain("default-src 'self'");
        result.Should().Contain("script-src 'self'");
    }

    #endregion

    #region ValidateContentSecurityPolicy

    [Fact]
    public void ValidateContentSecurityPolicy_EmptyString_ReturnsErrors()
    {
        var result = _sut.ValidateContentSecurityPolicy("");

        result.Errors.Should().NotBeEmpty();
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateContentSecurityPolicy_NullString_ReturnsErrors()
    {
        var result = _sut.ValidateContentSecurityPolicy(null!);

        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void ValidateContentSecurityPolicy_ValidCsp_ReturnsValid()
    {
        var csp = "default-src 'self'; script-src 'self'; object-src 'none'; base-uri 'self'";

        var result = _sut.ValidateContentSecurityPolicy(csp);

        result.IsValid.Should().BeTrue();
        result.SecurityScore.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ValidateContentSecurityPolicy_WithUnsafeInline_AddsWarning()
    {
        var csp = "default-src 'self'; script-src 'self' 'unsafe-inline'";

        var result = _sut.ValidateContentSecurityPolicy(csp);

        result.Warnings.Should().Contain(w => w.Contains("unsafe-inline"));
    }

    [Fact]
    public void ValidateContentSecurityPolicy_WithUnsafeEval_AddsWarning()
    {
        var csp = "default-src 'self'; script-src 'self' 'unsafe-eval'";

        var result = _sut.ValidateContentSecurityPolicy(csp);

        result.Warnings.Should().Contain(w => w.Contains("unsafe-eval"));
    }

    [Fact]
    public void ValidateContentSecurityPolicy_WithWildcard_AddsWarning()
    {
        var csp = "default-src *";

        var result = _sut.ValidateContentSecurityPolicy(csp);

        result.Warnings.Should().Contain(w => w.Contains("*"));
    }

    [Fact]
    public void ValidateContentSecurityPolicy_MissingDefaultSrc_AddsWarning()
    {
        var csp = "script-src 'self'";

        var result = _sut.ValidateContentSecurityPolicy(csp);

        result.Warnings.Should().Contain(w => w.Contains("default-src"));
    }

    [Fact]
    public void ValidateContentSecurityPolicy_MissingCriticalDirectives_AddsRecommendations()
    {
        var csp = "default-src 'self'";

        var result = _sut.ValidateContentSecurityPolicy(csp);

        result.Recommendations.Should().NotBeEmpty();
    }

    [Fact]
    public void ValidateContentSecurityPolicy_InvalidDirective_AddsError()
    {
        var csp = "default-src 'self'; invalid-directive 'self'";

        var result = _sut.ValidateContentSecurityPolicy(csp);

        result.Errors.Should().Contain(e => e.Contains("invalid-directive"));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateContentSecurityPolicy_SecureCsp_HighScore()
    {
        var csp = "default-src 'none'; script-src 'self'; style-src 'self'; object-src 'none'; base-uri 'self'; upgrade-insecure-requests; block-all-mixed-content";

        var result = _sut.ValidateContentSecurityPolicy(csp);

        result.SecurityScore.Should().BeGreaterThanOrEqualTo(80);
    }

    [Fact]
    public void ValidateContentSecurityPolicy_DataUriInScriptSrc_AddsWarning()
    {
        var csp = "default-src 'self'; script-src 'self' data:";

        var result = _sut.ValidateContentSecurityPolicy(csp);

        result.Warnings.Should().Contain(w => w.Contains("data:"));
    }

    #endregion

    #region AnalyzeSecurityHeaders

    [Fact]
    public void AnalyzeSecurityHeaders_AllPresent_HighScore()
    {
        var headers = new Dictionary<string, string>
        {
            ["Content-Security-Policy"] = "default-src 'self'",
            ["X-Frame-Options"] = "DENY",
            ["X-Content-Type-Options"] = "nosniff",
            ["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains",
            ["Referrer-Policy"] = "strict-origin-when-cross-origin",
            ["X-XSS-Protection"] = "1; mode=block"
        };

        var result = _sut.AnalyzeSecurityHeaders(headers);

        result.OverallScore.Should().BeGreaterThan(0);
        result.MissingHeaders.Should().BeEmpty();
        result.Vulnerabilities.Should().BeEmpty();
    }

    [Fact]
    public void AnalyzeSecurityHeaders_MissingCsp_ReportsVulnerability()
    {
        var headers = new Dictionary<string, string>
        {
            ["X-Frame-Options"] = "DENY",
            ["X-Content-Type-Options"] = "nosniff"
        };

        var result = _sut.AnalyzeSecurityHeaders(headers);

        result.MissingHeaders.Should().Contain("Content-Security-Policy");
        result.Vulnerabilities.Should().Contain(v =>
            v.AffectedHeader == "Content-Security-Policy" &&
            v.Severity == SecurityVulnerabilitySeverity.High);
    }

    [Fact]
    public void AnalyzeSecurityHeaders_EmptyHeaders_ReportsAllMissing()
    {
        var headers = new Dictionary<string, string>();

        var result = _sut.AnalyzeSecurityHeaders(headers);

        result.MissingHeaders.Should().HaveCountGreaterThanOrEqualTo(6);
        result.Vulnerabilities.Should().NotBeEmpty();
    }

    [Fact]
    public void AnalyzeSecurityHeaders_InsecureValues_ReportsInsecure()
    {
        var headers = new Dictionary<string, string>
        {
            ["Content-Security-Policy"] = "default-src 'self'",
            ["X-Frame-Options"] = "ALLOW-FROM evil.com",
            ["X-Content-Type-Options"] = "nosniff",
            ["Strict-Transport-Security"] = "max-age=31536000",
            ["Referrer-Policy"] = "unsafe-url",
            ["X-XSS-Protection"] = "1; mode=block"
        };

        var result = _sut.AnalyzeSecurityHeaders(headers);

        result.InsecureHeaders.Should().NotBeEmpty();
    }

    [Fact]
    public void AnalyzeSecurityHeaders_GeneratesRecommendations()
    {
        var headers = new Dictionary<string, string>();

        var result = _sut.AnalyzeSecurityHeaders(headers);

        result.Recommendations.Should().NotBeEmpty();
    }

    #endregion

    #region GenerateNonce

    [Fact]
    public void GenerateNonce_ReturnsBase64String()
    {
        var nonce = _sut.GenerateNonce();

        nonce.Should().NotBeNullOrEmpty();
        var action = () => Convert.FromBase64String(nonce);
        action.Should().NotThrow();
    }

    [Fact]
    public void GenerateNonce_GeneratesUniqueValues()
    {
        var nonce1 = _sut.GenerateNonce();
        var nonce2 = _sut.GenerateNonce();

        nonce1.Should().NotBe(nonce2);
    }

    #endregion

    #region CalculateHash

    [Fact]
    public void CalculateHash_Sha256_ReturnsConsistentHash()
    {
        var content = "test content";

        var hash1 = _sut.CalculateHash(content, HashAlgorithm.Sha256);
        var hash2 = _sut.CalculateHash(content, HashAlgorithm.Sha256);

        hash1.Should().Be(hash2);
        hash1.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CalculateHash_Sha384_ReturnsHash()
    {
        var hash = _sut.CalculateHash("test", HashAlgorithm.Sha384);

        hash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CalculateHash_Sha512_ReturnsHash()
    {
        var hash = _sut.CalculateHash("test", HashAlgorithm.Sha512);

        hash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CalculateHash_DifferentContent_DifferentHash()
    {
        var hash1 = _sut.CalculateHash("content-a");
        var hash2 = _sut.CalculateHash("content-b");

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void CalculateHash_DifferentAlgorithms_DifferentHash()
    {
        var content = "test content";

        var hash256 = _sut.CalculateHash(content, HashAlgorithm.Sha256);
        var hash512 = _sut.CalculateHash(content, HashAlgorithm.Sha512);

        hash256.Should().NotBe(hash512);
    }

    #endregion

    #region ContentSecurityPolicyBuilder

    [Fact]
    public void CspBuilder_DefaultSource_BuildsCorrectly()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .DefaultSource(CspSources.Self)
            .Build();

        csp.Should().Be("default-src 'self'");
    }

    [Fact]
    public void CspBuilder_MultipleDirectives_BuildsCorrectly()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .DefaultSource(CspSources.Self)
            .ScriptSource(CspSources.Self, CspSources.UnsafeInline)
            .StyleSource(CspSources.Self)
            .Build();

        csp.Should().Contain("default-src 'self'");
        csp.Should().Contain("script-src 'self' 'unsafe-inline'");
        csp.Should().Contain("style-src 'self'");
    }

    [Fact]
    public void CspBuilder_ReportOnly_ReturnsReportOnlyHeaderName()
    {
        var builder = new ContentSecurityPolicyBuilder()
            .DefaultSource(CspSources.Self)
            .ReportOnly();

        builder.GetHeaderName().Should().Be("Content-Security-Policy-Report-Only");
    }

    [Fact]
    public void CspBuilder_NotReportOnly_ReturnsCspHeaderName()
    {
        var builder = new ContentSecurityPolicyBuilder()
            .DefaultSource(CspSources.Self);

        builder.GetHeaderName().Should().Be("Content-Security-Policy");
    }

    [Fact]
    public void CspBuilder_UpgradeInsecureRequests_IncludesDirective()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .DefaultSource(CspSources.Self)
            .UpgradeInsecureRequests()
            .Build();

        csp.Should().Contain("upgrade-insecure-requests");
    }

    [Fact]
    public void CspBuilder_BlockAllMixedContent_IncludesDirective()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .DefaultSource(CspSources.Self)
            .BlockAllMixedContent()
            .Build();

        csp.Should().Contain("block-all-mixed-content");
    }

    [Fact]
    public void CspBuilder_AllowInlineScriptsWithNonce_AddsNonce()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .ScriptSource(CspSources.Self)
            .AllowInlineScriptsWithNonce("abc123")
            .Build();

        csp.Should().Contain("'nonce-abc123'");
    }

    [Fact]
    public void CspBuilder_AllowInlineStylesWithNonce_AddsNonce()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .StyleSource(CspSources.Self)
            .AllowInlineStylesWithNonce("xyz789")
            .Build();

        csp.Should().Contain("'nonce-xyz789'");
    }

    [Fact]
    public void CspBuilder_GetDirectives_ReturnsAllDirectives()
    {
        var builder = new ContentSecurityPolicyBuilder()
            .DefaultSource(CspSources.Self)
            .ScriptSource(CspSources.Self);

        var directives = builder.GetDirectives();

        directives.Should().ContainKey("default-src");
        directives.Should().ContainKey("script-src");
    }

    [Fact]
    public void CspBuilder_AllowUnsafeEval_AddsUnsafeEval()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .ScriptSource(CspSources.Self)
            .AllowUnsafeEval()
            .Build();

        csp.Should().Contain("'unsafe-eval'");
    }

    [Fact]
    public void CspBuilder_ReportUri_AddsReportUri()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .DefaultSource(CspSources.Self)
            .ReportUri("https://example.com/csp-report")
            .Build();

        csp.Should().Contain("report-uri https://example.com/csp-report");
    }

    #endregion
}
