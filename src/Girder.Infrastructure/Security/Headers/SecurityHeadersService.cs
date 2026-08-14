using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Infrastructure.Security.Headers;

/// <summary>
/// Security headers service implementation
/// </summary>
public class SecurityHeadersService : ISecurityHeadersService
{
    private readonly ILogger<SecurityHeadersService> _logger;
    private readonly SecurityHeadersOptions _options;

    public SecurityHeadersService(
        ILogger<SecurityHeadersService> logger,
        Microsoft.Extensions.Options.IOptions<SecurityHeadersOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public string BuildContentSecurityPolicy(ContentSecurityPolicyBuilder builder)
    {
        try
        {
            return builder.Build();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building Content Security Policy");
            return "";
        }
    }

    public Dictionary<string, string> GetDefaultSecurityHeaders()
    {
        var headers = new Dictionary<string, string>();

        // X-Content-Type-Options
        headers["X-Content-Type-Options"] = "nosniff";

        // X-Frame-Options
        headers["X-Frame-Options"] = "DENY";

        // X-XSS-Protection (for legacy browsers)
        headers["X-XSS-Protection"] = "1; mode=block";

        // Referrer-Policy
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

        // Strict-Transport-Security
        if (_options.EnableHsts)
        {
            var hsts = $"max-age={_options.HstsMaxAge}";
            if (_options.HstsIncludeSubDomains)
                hsts += "; includeSubDomains";
            if (_options.HstsPreload)
                hsts += "; preload";
            
            headers["Strict-Transport-Security"] = hsts;
        }

        // Permissions-Policy
        if (_options.EnablePermissionsPolicy)
        {
            headers["Permissions-Policy"] = BuildPermissionsPolicy();
        }

        // Cross-Origin-Embedder-Policy
        if (_options.EnableCrossOriginEmbedderPolicy)
        {
            headers["Cross-Origin-Embedder-Policy"] = "require-corp";
        }

        // Cross-Origin-Opener-Policy
        if (_options.EnableCrossOriginOpenerPolicy)
        {
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
        }

        // Cross-Origin-Resource-Policy
        if (_options.EnableCrossOriginResourcePolicy)
        {
            headers["Cross-Origin-Resource-Policy"] = "same-origin";
        }

        // Content Security Policy
        if (_options.EnableDefaultCsp)
        {
            var cspBuilder = CreateDefaultCspBuilder();
            headers[cspBuilder.GetHeaderName()] = cspBuilder.Build();
        }

        return headers;
    }

    public Dictionary<string, string> GetSecurityHeaders(SecurityHeadersContext context)
    {
        var headers = GetDefaultSecurityHeaders();

        try
        {
            // Customize headers based on context
            if (context.IsApiRequest)
            {
                // Remove frame options for API requests
                headers.Remove("X-Frame-Options");
                
                // Stricter CSP for APIs
                var apiCsp = new ContentSecurityPolicyBuilder()
                    .DefaultSource(CspSources.None)
                    .Build();
                headers["Content-Security-Policy"] = apiCsp;
            }
            else if (context.IsStaticContent)
            {
                // Minimal headers for static content
                headers.Clear();
                headers["X-Content-Type-Options"] = "nosniff";
                headers["Cache-Control"] = "public, max-age=31536000, immutable";
            }
            else
            {
                // Build custom CSP based on context
                var cspBuilder = CreateContextualCspBuilder(context);
                headers[cspBuilder.GetHeaderName()] = cspBuilder.Build();
            }

            // Add custom headers from context
            foreach (var requirement in context.CustomRequirements)
            {
                if (requirement.Value is string headerValue)
                {
                    headers[requirement.Key] = headerValue;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error customizing security headers for context");
        }

        return headers;
    }

    public ContentSecurityPolicyValidationResult ValidateContentSecurityPolicy(string csp)
    {
        var result = new ContentSecurityPolicyValidationResult();
        
        try
        {
            if (string.IsNullOrWhiteSpace(csp))
            {
                result.Errors.Add("CSP is empty or null");
                return result;
            }

            // Parse CSP directives
            var directives = ParseCspDirectives(csp);
            result.DetectedDirectives = directives;

            // Validate directives
            ValidateDirectives(directives, result);

            // Calculate security score
            result.SecurityScore = CalculateCspSecurityScore(directives, result);
            result.IsValid = !result.Errors.Any();

            _logger.LogDebug("CSP validation completed: Valid={IsValid}, Score={Score}, Errors={ErrorCount}", 
                result.IsValid, result.SecurityScore, result.Errors.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating CSP");
            result.Errors.Add($"Validation failed: {ex.Message}");
        }

        return result;
    }

    public SecurityHeadersAnalysisResult AnalyzeSecurityHeaders(Dictionary<string, string> headers)
    {
        var result = new SecurityHeadersAnalysisResult();
        
        try
        {
            var requiredHeaders = GetRequiredSecurityHeaders();
            var headerAnalysis = new Dictionary<string, HeaderAnalysis>();

            // Analyze each required header
            foreach (var requiredHeader in requiredHeaders)
            {
                var analysis = AnalyzeHeader(requiredHeader, headers);
                headerAnalysis[requiredHeader] = analysis;

                if (!analysis.IsPresent)
                {
                    result.MissingHeaders.Add(requiredHeader);
                    result.Vulnerabilities.Add(new SecurityVulnerability
                    {
                        Type = SecurityVulnerabilityType.MissingSecurityHeader,
                        Severity = GetHeaderSeverity(requiredHeader),
                        Description = $"Missing security header: {requiredHeader}",
                        AffectedHeader = requiredHeader,
                        Remediation = GetHeaderRemediation(requiredHeader)
                    });
                }
                else if (!analysis.IsSecure)
                {
                    result.InsecureHeaders[requiredHeader] = analysis.Value ?? "";
                    result.Vulnerabilities.Add(new SecurityVulnerability
                    {
                        Type = SecurityVulnerabilityType.InsecureHeaderValue,
                        Severity = SecurityVulnerabilitySeverity.Medium,
                        Description = $"Insecure value for header: {requiredHeader}",
                        AffectedHeader = requiredHeader,
                        Remediation = $"Update {requiredHeader} to use secure values"
                    });
                }
            }

            result.HeaderAnalysis = headerAnalysis;
            
            // Calculate overall score
            result.OverallScore = CalculateOverallSecurityScore(headerAnalysis);
            
            // Generate recommendations
            result.Recommendations = GenerateSecurityRecommendations(result);

            _logger.LogDebug("Security headers analysis completed: Score={Score}, Vulnerabilities={VulnCount}", 
                result.OverallScore, result.Vulnerabilities.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing security headers");
        }

        return result;
    }

    public string GenerateNonce()
    {
        var bytes = new byte[16];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    public string CalculateHash(string content, HashAlgorithm algorithm = HashAlgorithm.Sha256)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            var hashBytes = algorithm switch
            {
                HashAlgorithm.Sha256 => SHA256.HashData(bytes),
                HashAlgorithm.Sha384 => SHA384.HashData(bytes),
                HashAlgorithm.Sha512 => SHA512.HashData(bytes),
                _ => SHA256.HashData(bytes)
            };
            
            return Convert.ToBase64String(hashBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating hash for content");
            return "";
        }
    }

    #region Private Methods

    private ContentSecurityPolicyBuilder CreateDefaultCspBuilder()
    {
        var builder = new ContentSecurityPolicyBuilder()
            .DefaultSource(CspSources.Self)
            .ScriptSource(CspSources.Self)
            .StyleSource(CspSources.Self, CspSources.UnsafeInline) // Allow inline styles for compatibility
            .ImageSource(CspSources.Self, CspSources.Data, CspSources.Https)
            .FontSource(CspSources.Self, CspSources.Https)
            .ConnectSource(CspSources.Self)
            .MediaSource(CspSources.Self)
            .ObjectSource(CspSources.None)
            .FrameSource(CspSources.Self)
            .BaseUri(CspSources.Self)
            .FormAction(CspSources.Self)
            .FrameAncestors(CspSources.None);

        if (_options.UpgradeInsecureRequests)
        {
            builder.UpgradeInsecureRequests();
        }

        if (_options.BlockAllMixedContent)
        {
            builder.BlockAllMixedContent();
        }

        if (!string.IsNullOrEmpty(_options.ReportUri))
        {
            builder.ReportUri(_options.ReportUri);
        }

        return builder;
    }

    private ContentSecurityPolicyBuilder CreateContextualCspBuilder(SecurityHeadersContext context)
    {
        var builder = CreateDefaultCspBuilder();

        // Add allowed domains
        if (context.AllowedDomains.Any())
        {
            var domains = context.AllowedDomains.ToArray();
            builder.ConnectSource(domains.Prepend(CspSources.Self).ToArray());
        }

        // Add CDN domains
        if (context.CdnDomains.Any())
        {
            var cdnDomains = context.CdnDomains.ToArray();
            builder.ScriptSource(cdnDomains.Prepend(CspSources.Self).ToArray())
                   .StyleSource(cdnDomains.Prepend(CspSources.Self).Append(CspSources.UnsafeInline).ToArray())
                   .ImageSource(cdnDomains.Prepend(CspSources.Self).Append(CspSources.Data).Append(CspSources.Https).ToArray())
                   .FontSource(cdnDomains.Prepend(CspSources.Self).Append(CspSources.Https).ToArray());
        }

        // Handle inline scripts
        if (context.HasInlineScripts)
        {
            if (context.Nonces.TryGetValue("script", out var scriptNonce))
            {
                builder.AllowInlineScriptsWithNonce(scriptNonce);
            }
            else
            {
                _logger.LogWarning("Inline scripts detected but no nonce provided");
            }
        }

        // Handle inline styles
        if (context.HasInlineStyles)
        {
            if (context.Nonces.TryGetValue("style", out var styleNonce))
            {
                builder.AllowInlineStylesWithNonce(styleNonce);
            }
            else
            {
                // Keep unsafe-inline for styles if no nonce is provided
                _logger.LogWarning("Inline styles detected but no nonce provided");
            }
        }

        return builder;
    }

    private string BuildPermissionsPolicy()
    {
        var policies = new List<string>();

        // Disable potentially dangerous features by default
        var restrictedFeatures = new[]
        {
            "accelerometer", "ambient-light-sensor", "autoplay", "battery",
            "camera", "display-capture", "document-domain", "encrypted-media",
            "execution-while-not-rendered", "execution-while-out-of-viewport",
            "fullscreen", "geolocation", "gyroscope", "magnetometer",
            "microphone", "midi", "navigation-override", "payment",
            "picture-in-picture", "publickey-credentials-get", "screen-wake-lock",
            "sync-xhr", "usb", "web-share", "xr-spatial-tracking"
        };

        foreach (var feature in restrictedFeatures)
        {
            policies.Add($"{feature}=()");
        }

        // Allow specific features for same origin only
        var selfOnlyFeatures = new[] { "clipboard-read", "clipboard-write" };
        foreach (var feature in selfOnlyFeatures)
        {
            policies.Add($"{feature}=(self)");
        }

        return string.Join(", ", policies);
    }

    private Dictionary<string, List<string>> ParseCspDirectives(string csp)
    {
        var directives = new Dictionary<string, List<string>>();
        
        var directivePairs = csp.Split(';', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var directivePair in directivePairs)
        {
            var trimmed = directivePair.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            var directiveName = parts[0];
            var sources = parts.Skip(1).ToList();
            
            directives[directiveName] = sources;
        }

        return directives;
    }

    private void ValidateDirectives(Dictionary<string, List<string>> directives, ContentSecurityPolicyValidationResult result)
    {
        // Check for required directives
        if (!directives.ContainsKey("default-src"))
        {
            result.Warnings.Add("Consider adding 'default-src' directive as a fallback");
        }

        // Check for dangerous values
        foreach (var directive in directives)
        {
            foreach (var source in directive.Value)
            {
                if (source == "'unsafe-inline'")
                {
                    result.Warnings.Add($"'{directive.Key}' allows 'unsafe-inline' which may enable XSS attacks");
                }
                
                if (source == "'unsafe-eval'")
                {
                    result.Warnings.Add($"'{directive.Key}' allows 'unsafe-eval' which may enable code injection");
                }

                if (source == "*")
                {
                    result.Warnings.Add($"'{directive.Key}' allows all sources (*) which is very permissive");
                }

                if (source.StartsWith("data:") && directive.Key == "script-src")
                {
                    result.Warnings.Add("'script-src' allows data: URIs which may enable XSS attacks");
                }
            }
        }

        // Check for missing critical directives
        var criticalDirectives = new[] { "script-src", "object-src", "base-uri" };
        foreach (var critical in criticalDirectives)
        {
            if (!directives.ContainsKey(critical))
            {
                result.Recommendations.Add($"Consider adding '{critical}' directive for better security");
            }
        }

        // Validate directive syntax
        foreach (var directive in directives)
        {
            if (!IsValidDirectiveName(directive.Key))
            {
                result.Errors.Add($"Unknown or invalid directive: {directive.Key}");
            }
        }
    }

    private static bool IsValidDirectiveName(string directiveName)
    {
        var validDirectives = new[]
        {
            "default-src", "script-src", "style-src", "img-src", "connect-src",
            "font-src", "object-src", "media-src", "frame-src", "child-src",
            "worker-src", "manifest-src", "base-uri", "form-action", "frame-ancestors",
            "upgrade-insecure-requests", "block-all-mixed-content", "report-uri", "report-to"
        };

        return validDirectives.Contains(directiveName);
    }

    private static int CalculateCspSecurityScore(Dictionary<string, List<string>> directives, ContentSecurityPolicyValidationResult result)
    {
        var score = 100;

        // Penalize for missing critical directives
        if (!directives.ContainsKey("default-src")) score -= 10;
        if (!directives.ContainsKey("script-src")) score -= 15;
        if (!directives.ContainsKey("object-src")) score -= 10;

        // Penalize for unsafe values
        foreach (var directive in directives)
        {
            foreach (var source in directive.Value)
            {
                if (source == "'unsafe-inline'") score -= 20;
                if (source == "'unsafe-eval'") score -= 25;
                if (source == "*") score -= 15;
            }
        }

        // Penalize for errors
        score -= result.Errors.Count * 10;

        // Bonus for security features
        if (directives.ContainsKey("upgrade-insecure-requests")) score += 5;
        if (directives.ContainsKey("block-all-mixed-content")) score += 5;

        return Math.Max(0, Math.Min(100, score));
    }

    private HeaderAnalysis AnalyzeHeader(string headerName, Dictionary<string, string> headers)
    {
        var analysis = new HeaderAnalysis
        {
            IsPresent = headers.ContainsKey(headerName)
        };

        if (analysis.IsPresent)
        {
            analysis.Value = headers[headerName];
            analysis.IsSecure = IsHeaderValueSecure(headerName, analysis.Value);
            analysis.Score = CalculateHeaderScore(headerName, analysis.Value, analysis.IsSecure);
            
            if (!analysis.IsSecure)
            {
                analysis.Issues.Add($"Insecure value: {analysis.Value}");
                analysis.Recommendations.Add(GetHeaderRecommendation(headerName));
            }
        }
        else
        {
            analysis.Score = 0;
            analysis.Issues.Add("Header is missing");
            analysis.Recommendations.Add($"Add {headerName} header");
        }

        return analysis;
    }

    private static bool IsHeaderValueSecure(string headerName, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        return headerName.ToLowerInvariant() switch
        {
            "x-frame-options" => value.Equals("DENY", StringComparison.OrdinalIgnoreCase) || 
                                value.Equals("SAMEORIGIN", StringComparison.OrdinalIgnoreCase),
            "x-content-type-options" => value.Equals("nosniff", StringComparison.OrdinalIgnoreCase),
            "x-xss-protection" => value.StartsWith("1", StringComparison.OrdinalIgnoreCase),
            "strict-transport-security" => value.Contains("max-age=", StringComparison.OrdinalIgnoreCase),
            "referrer-policy" => IsSecureReferrerPolicy(value),
            "content-security-policy" => !string.IsNullOrWhiteSpace(value),
            _ => true
        };
    }

    private static bool IsSecureReferrerPolicy(string value)
    {
        var secureValues = new[]
        {
            "no-referrer", "same-origin", "strict-origin", "strict-origin-when-cross-origin"
        };
        
        return secureValues.Contains(value, StringComparer.OrdinalIgnoreCase);
    }

    private static int CalculateHeaderScore(string headerName, string? value, bool isSecure)
    {
        var baseScore = isSecure ? 100 : 50;
        
        // Adjust score based on header importance
        var importance = GetHeaderImportance(headerName);
        
        return (int)(baseScore * importance);
    }

    private static double GetHeaderImportance(string headerName)
    {
        return headerName.ToLowerInvariant() switch
        {
            "content-security-policy" => 1.0,
            "strict-transport-security" => 0.9,
            "x-frame-options" => 0.8,
            "x-content-type-options" => 0.7,
            "referrer-policy" => 0.6,
            "x-xss-protection" => 0.3, // Deprecated but still used
            _ => 0.5
        };
    }

    private static List<string> GetRequiredSecurityHeaders()
    {
        return new List<string>
        {
            "Content-Security-Policy",
            "X-Frame-Options",
            "X-Content-Type-Options",
            "Strict-Transport-Security",
            "Referrer-Policy",
            "X-XSS-Protection"
        };
    }

    private static SecurityVulnerabilitySeverity GetHeaderSeverity(string headerName)
    {
        return headerName.ToLowerInvariant() switch
        {
            "content-security-policy" => SecurityVulnerabilitySeverity.High,
            "x-frame-options" => SecurityVulnerabilitySeverity.Medium,
            "strict-transport-security" => SecurityVulnerabilitySeverity.High,
            "x-content-type-options" => SecurityVulnerabilitySeverity.Medium,
            "referrer-policy" => SecurityVulnerabilitySeverity.Low,
            _ => SecurityVulnerabilitySeverity.Low
        };
    }

    private static string GetHeaderRemediation(string headerName)
    {
        return headerName.ToLowerInvariant() switch
        {
            "content-security-policy" => "Implement a Content Security Policy to prevent XSS attacks",
            "x-frame-options" => "Add X-Frame-Options header to prevent clickjacking attacks",
            "strict-transport-security" => "Add HSTS header to enforce HTTPS connections",
            "x-content-type-options" => "Add X-Content-Type-Options: nosniff to prevent MIME type sniffing",
            "referrer-policy" => "Add Referrer-Policy header to control referrer information leakage",
            _ => $"Add {headerName} header for improved security"
        };
    }

    private static string GetHeaderRecommendation(string headerName)
    {
        return headerName.ToLowerInvariant() switch
        {
            "x-frame-options" => "Use 'DENY' or 'SAMEORIGIN'",
            "x-content-type-options" => "Use 'nosniff'",
            "x-xss-protection" => "Use '1; mode=block'",
            "strict-transport-security" => "Use 'max-age=31536000; includeSubDomains'",
            "referrer-policy" => "Use 'strict-origin-when-cross-origin'",
            _ => "Use secure values according to OWASP guidelines"
        };
    }

    private static int CalculateOverallSecurityScore(Dictionary<string, HeaderAnalysis> headerAnalysis)
    {
        if (!headerAnalysis.Any())
            return 0;

        var totalWeight = 0.0;
        var weightedScore = 0.0;

        foreach (var analysis in headerAnalysis)
        {
            var weight = GetHeaderImportance(analysis.Key);
            totalWeight += weight;
            weightedScore += analysis.Value.Score * weight;
        }

        return totalWeight > 0 ? (int)(weightedScore / totalWeight) : 0;
    }

    private static List<string> GenerateSecurityRecommendations(SecurityHeadersAnalysisResult result)
    {
        var recommendations = new List<string>();

        if (result.MissingHeaders.Any())
        {
            recommendations.Add($"Add missing security headers: {string.Join(", ", result.MissingHeaders)}");
        }

        if (result.InsecureHeaders.Any())
        {
            recommendations.Add("Review and update insecure header values");
        }

        if (result.OverallScore < 70)
        {
            recommendations.Add("Implement comprehensive security headers to improve protection");
        }

        var criticalVulns = result.Vulnerabilities.Count(v => v.Severity >= SecurityVulnerabilitySeverity.High);
        if (criticalVulns > 0)
        {
            recommendations.Add($"Address {criticalVulns} critical security vulnerabilities immediately");
        }

        return recommendations;
    }

    #endregion
}

/// <summary>
/// Security headers configuration options
/// </summary>
public class SecurityHeadersOptions
{
    /// <summary>
    /// Enable HSTS
    /// </summary>
    public bool EnableHsts { get; set; } = true;

    /// <summary>
    /// HSTS max age in seconds
    /// </summary>
    public int HstsMaxAge { get; set; } = 31536000; // 1 year

    /// <summary>
    /// Include subdomains in HSTS
    /// </summary>
    public bool HstsIncludeSubDomains { get; set; } = true;

    /// <summary>
    /// Enable HSTS preload
    /// </summary>
    public bool HstsPreload { get; set; } = false;

    /// <summary>
    /// Enable default CSP
    /// </summary>
    public bool EnableDefaultCsp { get; set; } = true;

    /// <summary>
    /// Upgrade insecure requests
    /// </summary>
    public bool UpgradeInsecureRequests { get; set; } = true;

    /// <summary>
    /// Block all mixed content
    /// </summary>
    public bool BlockAllMixedContent { get; set; } = false;

    /// <summary>
    /// CSP report URI
    /// </summary>
    public string? ReportUri { get; set; }

    /// <summary>
    /// Enable Permissions Policy
    /// </summary>
    public bool EnablePermissionsPolicy { get; set; } = true;

    /// <summary>
    /// Enable Cross-Origin-Embedder-Policy
    /// </summary>
    public bool EnableCrossOriginEmbedderPolicy { get; set; } = false;

    /// <summary>
    /// Enable Cross-Origin-Opener-Policy
    /// </summary>
    public bool EnableCrossOriginOpenerPolicy { get; set; } = true;

    /// <summary>
    /// Enable Cross-Origin-Resource-Policy
    /// </summary>
    public bool EnableCrossOriginResourcePolicy { get; set; } = true;
}