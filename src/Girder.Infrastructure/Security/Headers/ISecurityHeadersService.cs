namespace Infrastructure.Security.Headers;

/// <summary>
/// Interface for security headers management
/// </summary>
public interface ISecurityHeadersService
{
    /// <summary>
    /// Build Content Security Policy header
    /// </summary>
    string BuildContentSecurityPolicy(ContentSecurityPolicyBuilder builder);

    /// <summary>
    /// Get default security headers
    /// </summary>
    Dictionary<string, string> GetDefaultSecurityHeaders();

    /// <summary>
    /// Get security headers for specific context
    /// </summary>
    Dictionary<string, string> GetSecurityHeaders(SecurityHeadersContext context);

    /// <summary>
    /// Validate Content Security Policy
    /// </summary>
    ContentSecurityPolicyValidationResult ValidateContentSecurityPolicy(string csp);

    /// <summary>
    /// Analyze security headers for vulnerabilities
    /// </summary>
    SecurityHeadersAnalysisResult AnalyzeSecurityHeaders(Dictionary<string, string> headers);

    /// <summary>
    /// Generate nonce for inline scripts/styles
    /// </summary>
    string GenerateNonce();

    /// <summary>
    /// Calculate hash for inline content
    /// </summary>
    string CalculateHash(string content, HashAlgorithm algorithm = HashAlgorithm.Sha256);
}

/// <summary>
/// Content Security Policy builder
/// </summary>
public class ContentSecurityPolicyBuilder
{
    private readonly Dictionary<string, List<string>> _directives = new();
    private readonly List<string> _reportEndpoints = new();
    private bool _reportOnly = false;

    /// <summary>
    /// Set default-src directive
    /// </summary>
    public ContentSecurityPolicyBuilder DefaultSource(params string[] sources)
    {
        SetDirective("default-src", sources);
        return this;
    }

    /// <summary>
    /// Set script-src directive
    /// </summary>
    public ContentSecurityPolicyBuilder ScriptSource(params string[] sources)
    {
        SetDirective("script-src", sources);
        return this;
    }

    /// <summary>
    /// Set style-src directive
    /// </summary>
    public ContentSecurityPolicyBuilder StyleSource(params string[] sources)
    {
        SetDirective("style-src", sources);
        return this;
    }

    /// <summary>
    /// Set img-src directive
    /// </summary>
    public ContentSecurityPolicyBuilder ImageSource(params string[] sources)
    {
        SetDirective("img-src", sources);
        return this;
    }

    /// <summary>
    /// Set connect-src directive
    /// </summary>
    public ContentSecurityPolicyBuilder ConnectSource(params string[] sources)
    {
        SetDirective("connect-src", sources);
        return this;
    }

    /// <summary>
    /// Set font-src directive
    /// </summary>
    public ContentSecurityPolicyBuilder FontSource(params string[] sources)
    {
        SetDirective("font-src", sources);
        return this;
    }

    /// <summary>
    /// Set object-src directive
    /// </summary>
    public ContentSecurityPolicyBuilder ObjectSource(params string[] sources)
    {
        SetDirective("object-src", sources);
        return this;
    }

    /// <summary>
    /// Set media-src directive
    /// </summary>
    public ContentSecurityPolicyBuilder MediaSource(params string[] sources)
    {
        SetDirective("media-src", sources);
        return this;
    }

    /// <summary>
    /// Set frame-src directive
    /// </summary>
    public ContentSecurityPolicyBuilder FrameSource(params string[] sources)
    {
        SetDirective("frame-src", sources);
        return this;
    }

    /// <summary>
    /// Set child-src directive
    /// </summary>
    public ContentSecurityPolicyBuilder ChildSource(params string[] sources)
    {
        SetDirective("child-src", sources);
        return this;
    }

    /// <summary>
    /// Set worker-src directive
    /// </summary>
    public ContentSecurityPolicyBuilder WorkerSource(params string[] sources)
    {
        SetDirective("worker-src", sources);
        return this;
    }

    /// <summary>
    /// Set manifest-src directive
    /// </summary>
    public ContentSecurityPolicyBuilder ManifestSource(params string[] sources)
    {
        SetDirective("manifest-src", sources);
        return this;
    }

    /// <summary>
    /// Set base-uri directive
    /// </summary>
    public ContentSecurityPolicyBuilder BaseUri(params string[] sources)
    {
        SetDirective("base-uri", sources);
        return this;
    }

    /// <summary>
    /// Set form-action directive
    /// </summary>
    public ContentSecurityPolicyBuilder FormAction(params string[] sources)
    {
        SetDirective("form-action", sources);
        return this;
    }

    /// <summary>
    /// Set frame-ancestors directive
    /// </summary>
    public ContentSecurityPolicyBuilder FrameAncestors(params string[] sources)
    {
        SetDirective("frame-ancestors", sources);
        return this;
    }

    /// <summary>
    /// Add upgrade-insecure-requests directive
    /// </summary>
    public ContentSecurityPolicyBuilder UpgradeInsecureRequests(bool enabled = true)
    {
        if (enabled)
        {
            SetDirective("upgrade-insecure-requests", new string[0]);
        }
        return this;
    }

    /// <summary>
    /// Add block-all-mixed-content directive
    /// </summary>
    public ContentSecurityPolicyBuilder BlockAllMixedContent(bool enabled = true)
    {
        if (enabled)
        {
            SetDirective("block-all-mixed-content", new string[0]);
        }
        return this;
    }

    /// <summary>
    /// Set report-uri directive
    /// </summary>
    public ContentSecurityPolicyBuilder ReportUri(params string[] uris)
    {
        SetDirective("report-uri", uris);
        return this;
    }

    /// <summary>
    /// Set report-to directive
    /// </summary>
    public ContentSecurityPolicyBuilder ReportTo(string groupName)
    {
        SetDirective("report-to", new[] { groupName });
        return this;
    }

    /// <summary>
    /// Set to report-only mode
    /// </summary>
    public ContentSecurityPolicyBuilder ReportOnly(bool reportOnly = true)
    {
        _reportOnly = reportOnly;
        return this;
    }

    /// <summary>
    /// Allow inline scripts with nonce
    /// </summary>
    public ContentSecurityPolicyBuilder AllowInlineScriptsWithNonce(string nonce)
    {
        AddToDirective("script-src", $"'nonce-{nonce}'");
        return this;
    }

    /// <summary>
    /// Allow inline styles with nonce
    /// </summary>
    public ContentSecurityPolicyBuilder AllowInlineStylesWithNonce(string nonce)
    {
        AddToDirective("style-src", $"'nonce-{nonce}'");
        return this;
    }

    /// <summary>
    /// Allow inline scripts with hash
    /// </summary>
    public ContentSecurityPolicyBuilder AllowInlineScriptsWithHash(string hash, HashAlgorithm algorithm = HashAlgorithm.Sha256)
    {
        var algorithmName = algorithm.ToString().ToLowerInvariant();
        AddToDirective("script-src", $"'{algorithmName}-{hash}'");
        return this;
    }

    /// <summary>
    /// Allow inline styles with hash
    /// </summary>
    public ContentSecurityPolicyBuilder AllowInlineStylesWithHash(string hash, HashAlgorithm algorithm = HashAlgorithm.Sha256)
    {
        var algorithmName = algorithm.ToString().ToLowerInvariant();
        AddToDirective("style-src", $"'{algorithmName}-{hash}'");
        return this;
    }

    /// <summary>
    /// Allow unsafe inline scripts (not recommended)
    /// </summary>
    public ContentSecurityPolicyBuilder AllowUnsafeInlineScripts()
    {
        AddToDirective("script-src", "'unsafe-inline'");
        return this;
    }

    /// <summary>
    /// Allow unsafe inline styles (not recommended)
    /// </summary>
    public ContentSecurityPolicyBuilder AllowUnsafeInlineStyles()
    {
        AddToDirective("style-src", "'unsafe-inline'");
        return this;
    }

    /// <summary>
    /// Allow unsafe eval (not recommended)
    /// </summary>
    public ContentSecurityPolicyBuilder AllowUnsafeEval()
    {
        AddToDirective("script-src", "'unsafe-eval'");
        return this;
    }

    /// <summary>
    /// Add custom directive
    /// </summary>
    public ContentSecurityPolicyBuilder AddDirective(string directive, params string[] sources)
    {
        SetDirective(directive, sources);
        return this;
    }

    /// <summary>
    /// Build the CSP header value
    /// </summary>
    public string Build()
    {
        var directives = new List<string>();

        foreach (var directive in _directives)
        {
            if (directive.Value.Any())
            {
                directives.Add($"{directive.Key} {string.Join(" ", directive.Value)}");
            }
            else
            {
                directives.Add(directive.Key);
            }
        }

        return string.Join("; ", directives);
    }

    /// <summary>
    /// Get header name (CSP or CSP-Report-Only)
    /// </summary>
    public string GetHeaderName()
    {
        return _reportOnly ? "Content-Security-Policy-Report-Only" : "Content-Security-Policy";
    }

    /// <summary>
    /// Get all directives
    /// </summary>
    public Dictionary<string, List<string>> GetDirectives()
    {
        return new Dictionary<string, List<string>>(_directives);
    }

    private void SetDirective(string directive, string[] sources)
    {
        _directives[directive] = sources.ToList();
    }

    private void AddToDirective(string directive, string source)
    {
        if (!_directives.ContainsKey(directive))
        {
            _directives[directive] = new List<string>();
        }
        
        if (!_directives[directive].Contains(source))
        {
            _directives[directive].Add(source);
        }
    }
}

/// <summary>
/// Security headers context for customization
/// </summary>
public class SecurityHeadersContext
{
    /// <summary>
    /// Request path
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Whether the request is for an API endpoint
    /// </summary>
    public bool IsApiRequest { get; set; }

    /// <summary>
    /// Whether the request is for static content
    /// </summary>
    public bool IsStaticContent { get; set; }

    /// <summary>
    /// Whether the page contains inline scripts
    /// </summary>
    public bool HasInlineScripts { get; set; }

    /// <summary>
    /// Whether the page contains inline styles
    /// </summary>
    public bool HasInlineStyles { get; set; }

    /// <summary>
    /// External domains that need to be allowed
    /// </summary>
    public List<string> AllowedDomains { get; set; } = new();

    /// <summary>
    /// CDN domains for assets
    /// </summary>
    public List<string> CdnDomains { get; set; } = new();

    /// <summary>
    /// Nonces for inline content
    /// </summary>
    public Dictionary<string, string> Nonces { get; set; } = new();

    /// <summary>
    /// Custom security requirements
    /// </summary>
    public Dictionary<string, object?> CustomRequirements { get; set; } = new();
}

/// <summary>
/// CSP validation result
/// </summary>
public class ContentSecurityPolicyValidationResult
{
    /// <summary>
    /// Whether the CSP is valid
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Validation errors
    /// </summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// Validation warnings
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// Security recommendations
    /// </summary>
    public List<string> Recommendations { get; set; } = new();

    /// <summary>
    /// Security score (0-100)
    /// </summary>
    public int SecurityScore { get; set; }

    /// <summary>
    /// Detected directives
    /// </summary>
    public Dictionary<string, List<string>> DetectedDirectives { get; set; } = new();
}

/// <summary>
/// Security headers analysis result
/// </summary>
public class SecurityHeadersAnalysisResult
{
    /// <summary>
    /// Overall security score (0-100)
    /// </summary>
    public int OverallScore { get; set; }

    /// <summary>
    /// Missing critical headers
    /// </summary>
    public List<string> MissingHeaders { get; set; } = new();

    /// <summary>
    /// Insecure header values
    /// </summary>
    public Dictionary<string, string> InsecureHeaders { get; set; } = new();

    /// <summary>
    /// Security vulnerabilities found
    /// </summary>
    public List<SecurityVulnerability> Vulnerabilities { get; set; } = new();

    /// <summary>
    /// Recommendations for improvement
    /// </summary>
    public List<string> Recommendations { get; set; } = new();

    /// <summary>
    /// Header analysis details
    /// </summary>
    public Dictionary<string, HeaderAnalysis> HeaderAnalysis { get; set; } = new();
}

/// <summary>
/// Security vulnerability information
/// </summary>
public class SecurityVulnerability
{
    /// <summary>
    /// Vulnerability type
    /// </summary>
    public SecurityVulnerabilityType Type { get; set; }

    /// <summary>
    /// Severity level
    /// </summary>
    public SecurityVulnerabilitySeverity Severity { get; set; }

    /// <summary>
    /// Description of the vulnerability
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Affected header
    /// </summary>
    public string? AffectedHeader { get; set; }

    /// <summary>
    /// Remediation advice
    /// </summary>
    public string Remediation { get; set; } = string.Empty;

    /// <summary>
    /// OWASP reference
    /// </summary>
    public string? OwaspReference { get; set; }
}

/// <summary>
/// Header analysis details
/// </summary>
public class HeaderAnalysis
{
    /// <summary>
    /// Whether the header is present
    /// </summary>
    public bool IsPresent { get; set; }

    /// <summary>
    /// Header value
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    /// Whether the value is secure
    /// </summary>
    public bool IsSecure { get; set; }

    /// <summary>
    /// Security score for this header (0-100)
    /// </summary>
    public int Score { get; set; }

    /// <summary>
    /// Issues found with this header
    /// </summary>
    public List<string> Issues { get; set; } = new();

    /// <summary>
    /// Recommendations for this header
    /// </summary>
    public List<string> Recommendations { get; set; } = new();
}

/// <summary>
/// Security vulnerability types
/// </summary>
public enum SecurityVulnerabilityType
{
    MissingSecurityHeader,
    InsecureHeaderValue,
    ClickjackingVulnerability,
    MixedContentVulnerability,
    XssVulnerability,
    ContentTypeSniffingVulnerability,
    ReferrerLeakage,
    PermissionsPolicyMissing,
    CertificateTransparencyMissing
}

/// <summary>
/// Security vulnerability severity levels
/// </summary>
public enum SecurityVulnerabilitySeverity
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// Hash algorithms for CSP
/// </summary>
public enum HashAlgorithm
{
    Sha256,
    Sha384,
    Sha512
}

/// <summary>
/// Common CSP source values
/// </summary>
public static class CspSources
{
    public const string Self = "'self'";
    public const string None = "'none'";
    public const string UnsafeInline = "'unsafe-inline'";
    public const string UnsafeEval = "'unsafe-eval'";
    public const string UnsafeHashes = "'unsafe-hashes'";
    public const string StrictDynamic = "'strict-dynamic'";
    public const string ReportSample = "'report-sample'";
    public const string WasmUnsafeEval = "'wasm-unsafe-eval'";
    
    /// <summary>
    /// Create nonce source
    /// </summary>
    public static string Nonce(string nonce) => $"'nonce-{nonce}'";
    
    /// <summary>
    /// Create hash source
    /// </summary>
    public static string Hash(string hash, HashAlgorithm algorithm = HashAlgorithm.Sha256)
    {
        var algorithmName = algorithm.ToString().ToLowerInvariant();
        return $"'{algorithmName}-{hash}'";
    }
    
    /// <summary>
    /// Create scheme source
    /// </summary>
    public static string Scheme(string scheme) => $"{scheme}:";
    
    /// <summary>
    /// HTTPS scheme
    /// </summary>
    public const string Https = "https:";
    
    /// <summary>
    /// HTTP scheme
    /// </summary>
    public const string Http = "http:";
    
    /// <summary>
    /// Data scheme
    /// </summary>
    public const string Data = "data:";
    
    /// <summary>
    /// Blob scheme
    /// </summary>
    public const string Blob = "blob:";
}