using Infrastructure.Security.Headers;
using Microsoft.AspNetCore.Http;

namespace Infrastructure.Tests.Security.Headers;

[Trait("Category", "Unit")]
public class ContentSecurityPolicyAttributeTests
{
    [Fact]
    public void Attribute_DefaultValues_AreCorrect()
    {
        var attr = new ContentSecurityPolicyAttribute();

        attr.ScriptSources.Should().BeNull();
        attr.StyleSources.Should().BeNull();
        attr.ImageSources.Should().BeNull();
        attr.ConnectSources.Should().BeNull();
        attr.AllowUnsafeInline.Should().BeFalse();
        attr.AllowUnsafeEval.Should().BeFalse();
        attr.ReportOnly.Should().BeFalse();
    }

    [Fact]
    public void Attribute_CanSetAllProperties()
    {
        var attr = new ContentSecurityPolicyAttribute
        {
            ScriptSources = "'self' cdn.example.com",
            StyleSources = "'self' 'unsafe-inline'",
            ImageSources = "'self' data:",
            ConnectSources = "'self' wss://api.example.com",
            AllowUnsafeInline = true,
            AllowUnsafeEval = true,
            ReportOnly = true
        };

        attr.ScriptSources.Should().Be("'self' cdn.example.com");
        attr.StyleSources.Should().Be("'self' 'unsafe-inline'");
        attr.ImageSources.Should().Be("'self' data:");
        attr.ConnectSources.Should().Be("'self' wss://api.example.com");
        attr.AllowUnsafeInline.Should().BeTrue();
        attr.AllowUnsafeEval.Should().BeTrue();
        attr.ReportOnly.Should().BeTrue();
    }

    [Fact]
    public void Attribute_IsAttributeClass()
    {
        typeof(ContentSecurityPolicyAttribute).Should().BeAssignableTo<Attribute>();
    }

    [Fact]
    public void Attribute_HasCorrectAttributeUsage()
    {
        var usage = typeof(ContentSecurityPolicyAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .FirstOrDefault() as AttributeUsageAttribute;

        usage.Should().NotBeNull();
        usage!.ValidOn.Should().HaveFlag(AttributeTargets.Class);
        usage.ValidOn.Should().HaveFlag(AttributeTargets.Method);
    }
}

[Trait("Category", "Unit")]
public class ContentSecurityPolicyBuilderTests
{
    #region Basic directives

    [Fact]
    public void DefaultSource_SingleSource_BuildsCorrectly()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .DefaultSource(CspSources.Self)
            .Build();

        csp.Should().Be("default-src 'self'");
    }

    [Fact]
    public void DefaultSource_MultipleSources_BuildsCorrectly()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .DefaultSource(CspSources.Self, CspSources.Https)
            .Build();

        csp.Should().Contain("default-src");
        csp.Should().Contain("'self'");
        csp.Should().Contain("https:");
    }

    [Fact]
    public void ScriptSource_BuildsCorrectly()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .ScriptSource(CspSources.Self, CspSources.UnsafeInline)
            .Build();

        csp.Should().Contain("script-src 'self' 'unsafe-inline'");
    }

    [Fact]
    public void StyleSource_BuildsCorrectly()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .StyleSource(CspSources.Self)
            .Build();

        csp.Should().Contain("style-src 'self'");
    }

    [Fact]
    public void ImageSource_BuildsCorrectly()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .ImageSource(CspSources.Self, CspSources.Data, CspSources.Https)
            .Build();

        csp.Should().Contain("img-src");
        csp.Should().Contain("'self'");
    }

    [Fact]
    public void ConnectSource_BuildsCorrectly()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .ConnectSource(CspSources.Self)
            .Build();

        csp.Should().Contain("connect-src 'self'");
    }

    [Fact]
    public void FontSource_BuildsCorrectly()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .FontSource(CspSources.Self, CspSources.Https)
            .Build();

        csp.Should().Contain("font-src");
    }

    [Fact]
    public void ObjectSource_NoneSource_BuildsCorrectly()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .ObjectSource(CspSources.None)
            .Build();

        csp.Should().Contain("object-src 'none'");
    }

    [Fact]
    public void MediaSource_BuildsCorrectly()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .MediaSource(CspSources.Self)
            .Build();

        csp.Should().Contain("media-src 'self'");
    }

    [Fact]
    public void FrameSource_BuildsCorrectly()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .FrameSource(CspSources.Self)
            .Build();

        csp.Should().Contain("frame-src 'self'");
    }

    [Fact]
    public void ChildSource_BuildsCorrectly()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .ChildSource(CspSources.Self)
            .Build();

        csp.Should().Contain("child-src 'self'");
    }

    [Fact]
    public void WorkerSource_BuildsCorrectly()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .WorkerSource(CspSources.Self)
            .Build();

        csp.Should().Contain("worker-src 'self'");
    }

    [Fact]
    public void ManifestSource_BuildsCorrectly()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .ManifestSource(CspSources.Self)
            .Build();

        csp.Should().Contain("manifest-src 'self'");
    }

    [Fact]
    public void BaseUri_BuildsCorrectly()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .BaseUri(CspSources.Self)
            .Build();

        csp.Should().Contain("base-uri 'self'");
    }

    [Fact]
    public void FormAction_BuildsCorrectly()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .FormAction(CspSources.Self)
            .Build();

        csp.Should().Contain("form-action 'self'");
    }

    [Fact]
    public void FrameAncestors_NoneSource_BuildsCorrectly()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .FrameAncestors(CspSources.None)
            .Build();

        csp.Should().Contain("frame-ancestors 'none'");
    }

    #endregion

    #region Special directives

    [Fact]
    public void UpgradeInsecureRequests_Enabled_IncludesDirective()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .DefaultSource(CspSources.Self)
            .UpgradeInsecureRequests()
            .Build();

        csp.Should().Contain("upgrade-insecure-requests");
    }

    [Fact]
    public void UpgradeInsecureRequests_Disabled_ExcludesDirective()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .DefaultSource(CspSources.Self)
            .UpgradeInsecureRequests(false)
            .Build();

        csp.Should().NotContain("upgrade-insecure-requests");
    }

    [Fact]
    public void BlockAllMixedContent_Enabled_IncludesDirective()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .DefaultSource(CspSources.Self)
            .BlockAllMixedContent()
            .Build();

        csp.Should().Contain("block-all-mixed-content");
    }

    [Fact]
    public void BlockAllMixedContent_Disabled_ExcludesDirective()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .DefaultSource(CspSources.Self)
            .BlockAllMixedContent(false)
            .Build();

        csp.Should().NotContain("block-all-mixed-content");
    }

    [Fact]
    public void ReportUri_AddsReportUri()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .DefaultSource(CspSources.Self)
            .ReportUri("https://csp.example.com/report")
            .Build();

        csp.Should().Contain("report-uri https://csp.example.com/report");
    }

    [Fact]
    public void ReportTo_AddsReportTo()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .DefaultSource(CspSources.Self)
            .ReportTo("csp-endpoint")
            .Build();

        csp.Should().Contain("report-to csp-endpoint");
    }

    [Fact]
    public void ReportOnly_GetHeaderName_ReturnsReportOnlyName()
    {
        var builder = new ContentSecurityPolicyBuilder()
            .DefaultSource(CspSources.Self)
            .ReportOnly(true);

        builder.GetHeaderName().Should().Be("Content-Security-Policy-Report-Only");
    }

    [Fact]
    public void ReportOnly_False_GetHeaderName_ReturnsCspName()
    {
        var builder = new ContentSecurityPolicyBuilder()
            .DefaultSource(CspSources.Self)
            .ReportOnly(false);

        builder.GetHeaderName().Should().Be("Content-Security-Policy");
    }

    #endregion

    #region Nonce and hash

    [Fact]
    public void AllowInlineScriptsWithNonce_AddsNonce()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .ScriptSource(CspSources.Self)
            .AllowInlineScriptsWithNonce("abc123")
            .Build();

        csp.Should().Contain("'nonce-abc123'");
    }

    [Fact]
    public void AllowInlineStylesWithNonce_AddsNonce()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .StyleSource(CspSources.Self)
            .AllowInlineStylesWithNonce("xyz789")
            .Build();

        csp.Should().Contain("'nonce-xyz789'");
    }

    [Fact]
    public void AllowInlineScriptsWithNonce_CalledTwice_DeduplicatesNonce()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .ScriptSource(CspSources.Self)
            .AllowInlineScriptsWithNonce("samenonce")
            .AllowInlineScriptsWithNonce("samenonce")
            .Build();

        var count = csp.Split("'nonce-samenonce'").Length - 1;
        count.Should().Be(1);
    }

    [Fact]
    public void AllowInlineScriptsWithHash_Sha256_AddsHash()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .ScriptSource(CspSources.Self)
            .AllowInlineScriptsWithHash("abc123hash", HashAlgorithm.Sha256)
            .Build();

        csp.Should().Contain("'sha256-abc123hash'");
    }

    [Fact]
    public void AllowInlineScriptsWithHash_Sha384_AddsHash()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .ScriptSource(CspSources.Self)
            .AllowInlineScriptsWithHash("abc123hash", HashAlgorithm.Sha384)
            .Build();

        csp.Should().Contain("'sha384-abc123hash'");
    }

    [Fact]
    public void AllowInlineScriptsWithHash_Sha512_AddsHash()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .ScriptSource(CspSources.Self)
            .AllowInlineScriptsWithHash("abc123hash", HashAlgorithm.Sha512)
            .Build();

        csp.Should().Contain("'sha512-abc123hash'");
    }

    [Fact]
    public void AllowInlineStylesWithHash_AddsHash()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .StyleSource(CspSources.Self)
            .AllowInlineStylesWithHash("styhash", HashAlgorithm.Sha256)
            .Build();

        csp.Should().Contain("'sha256-styhash'");
    }

    [Fact]
    public void AllowUnsafeInlineScripts_AddsUnsafeInline()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .ScriptSource(CspSources.Self)
            .AllowUnsafeInlineScripts()
            .Build();

        csp.Should().Contain("'unsafe-inline'");
    }

    [Fact]
    public void AllowUnsafeInlineStyles_AddsUnsafeInline()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .StyleSource(CspSources.Self)
            .AllowUnsafeInlineStyles()
            .Build();

        csp.Should().Contain("'unsafe-inline'");
    }

    [Fact]
    public void AllowUnsafeEval_AddsUnsafeEval()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .ScriptSource(CspSources.Self)
            .AllowUnsafeEval()
            .Build();

        csp.Should().Contain("'unsafe-eval'");
    }

    #endregion

    #region AddDirective

    [Fact]
    public void AddDirective_CustomDirective_AddsDirective()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .AddDirective("custom-directive", "'self'")
            .Build();

        csp.Should().Contain("custom-directive 'self'");
    }

    #endregion

    #region GetDirectives

    [Fact]
    public void GetDirectives_ReturnsAllDirectives()
    {
        var builder = new ContentSecurityPolicyBuilder()
            .DefaultSource(CspSources.Self)
            .ScriptSource(CspSources.Self)
            .StyleSource(CspSources.Self);

        var directives = builder.GetDirectives();

        directives.Should().ContainKey("default-src");
        directives.Should().ContainKey("script-src");
        directives.Should().ContainKey("style-src");
    }

    [Fact]
    public void GetDirectives_ReturnsCopy_NotReference()
    {
        var builder = new ContentSecurityPolicyBuilder()
            .DefaultSource(CspSources.Self);

        var directives1 = builder.GetDirectives();
        var directives2 = builder.GetDirectives();

        directives1.Should().NotBeSameAs(directives2);
    }

    #endregion

    #region Build

    [Fact]
    public void Build_DirectiveWithNoSources_OmitsSources()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .UpgradeInsecureRequests()
            .Build();

        csp.Should().Be("upgrade-insecure-requests");
    }

    [Fact]
    public void Build_MultipleDirectives_JoinedWithSemicolon()
    {
        var csp = new ContentSecurityPolicyBuilder()
            .DefaultSource(CspSources.Self)
            .ScriptSource(CspSources.Self)
            .Build();

        csp.Should().Contain("; ");
    }

    #endregion
}

[Trait("Category", "Unit")]
public class CspSourcesTests
{
    [Fact]
    public void Constants_HaveCorrectValues()
    {
        CspSources.Self.Should().Be("'self'");
        CspSources.None.Should().Be("'none'");
        CspSources.UnsafeInline.Should().Be("'unsafe-inline'");
        CspSources.UnsafeEval.Should().Be("'unsafe-eval'");
        CspSources.UnsafeHashes.Should().Be("'unsafe-hashes'");
        CspSources.StrictDynamic.Should().Be("'strict-dynamic'");
        CspSources.ReportSample.Should().Be("'report-sample'");
        CspSources.WasmUnsafeEval.Should().Be("'wasm-unsafe-eval'");
        CspSources.Https.Should().Be("https:");
        CspSources.Http.Should().Be("http:");
        CspSources.Data.Should().Be("data:");
        CspSources.Blob.Should().Be("blob:");
    }

    [Fact]
    public void Nonce_ReturnsCorrectFormat()
    {
        var result = CspSources.Nonce("abc123");

        result.Should().Be("'nonce-abc123'");
    }

    [Fact]
    public void Hash_Sha256_ReturnsCorrectFormat()
    {
        var result = CspSources.Hash("abc123", HashAlgorithm.Sha256);

        result.Should().Be("'sha256-abc123'");
    }

    [Fact]
    public void Hash_Sha384_ReturnsCorrectFormat()
    {
        var result = CspSources.Hash("abc123", HashAlgorithm.Sha384);

        result.Should().Be("'sha384-abc123'");
    }

    [Fact]
    public void Hash_Sha512_ReturnsCorrectFormat()
    {
        var result = CspSources.Hash("abc123", HashAlgorithm.Sha512);

        result.Should().Be("'sha512-abc123'");
    }

    [Fact]
    public void Scheme_ReturnsCorrectFormat()
    {
        var result = CspSources.Scheme("blob");

        result.Should().Be("blob:");
    }
}

[Trait("Category", "Unit")]
public class HttpContextSecurityExtensionsTests
{
    [Fact]
    public void GetScriptNonce_WhenSet_ReturnsNonce()
    {
        var context = new DefaultHttpContext();
        context.Items["ScriptNonce"] = "test-nonce";

        var nonce = context.GetScriptNonce();

        nonce.Should().Be("test-nonce");
    }

    [Fact]
    public void GetScriptNonce_WhenNotSet_ReturnsNull()
    {
        var context = new DefaultHttpContext();

        var nonce = context.GetScriptNonce();

        nonce.Should().BeNull();
    }

    [Fact]
    public void GetStyleNonce_WhenSet_ReturnsNonce()
    {
        var context = new DefaultHttpContext();
        context.Items["StyleNonce"] = "style-nonce";

        var nonce = context.GetStyleNonce();

        nonce.Should().Be("style-nonce");
    }

    [Fact]
    public void GetStyleNonce_WhenNotSet_ReturnsNull()
    {
        var context = new DefaultHttpContext();

        var nonce = context.GetStyleNonce();

        nonce.Should().BeNull();
    }

    [Fact]
    public void AddAllowedDomain_FirstDomain_CreatesList()
    {
        var context = new DefaultHttpContext();

        context.AddAllowedDomain("example.com");

        var domains = context.GetAllowedDomains();
        domains.Should().Contain("example.com");
    }

    [Fact]
    public void AddAllowedDomain_MultipleDomains_AllAdded()
    {
        var context = new DefaultHttpContext();

        context.AddAllowedDomain("example.com");
        context.AddAllowedDomain("cdn.example.com");

        var domains = context.GetAllowedDomains();
        domains.Should().Contain("example.com");
        domains.Should().Contain("cdn.example.com");
        domains.Should().HaveCount(2);
    }

    [Fact]
    public void AddAllowedDomain_DuplicateDomain_NotAddedTwice()
    {
        var context = new DefaultHttpContext();

        context.AddAllowedDomain("example.com");
        context.AddAllowedDomain("example.com");

        var domains = context.GetAllowedDomains();
        domains.Should().HaveCount(1);
    }

    [Fact]
    public void GetAllowedDomains_WhenNotSet_ReturnsEmptyList()
    {
        var context = new DefaultHttpContext();

        var domains = context.GetAllowedDomains();

        domains.Should().BeEmpty();
    }
}
