using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Security.Cryptography;

namespace Girder.Infrastructure.Middleware;

/// <summary>
/// Production-grade Security Headers Middleware
/// Implements comprehensive security headers based on OWASP recommendations.
/// Generates per-request nonces for script-src CSP hardening.
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public SecurityHeadersMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        _next = next;
        _configuration = configuration;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Generate a per-request nonce for CSP
        var nonce = GenerateNonce();
        context.Items["CspNonce"] = nonce;

        // Add security headers before processing request
        AddSecurityHeaders(context, nonce);

        await _next(context);
    }

    private static string GenerateNonce()
    {
        var bytes = new byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    private void AddSecurityHeaders(HttpContext context, string nonce)
    {
        var response = context.Response;

        // =================================================================
        // 1. CLICKJACKING PROTECTION
        // =================================================================
        response.Headers.TryAdd("X-Frame-Options", "DENY");

        // =================================================================
        // 2. XSS PROTECTION
        // =================================================================
        response.Headers.TryAdd("X-XSS-Protection", "1; mode=block");

        // =================================================================
        // 3. MIME-TYPE SNIFFING PREVENTION
        // =================================================================
        response.Headers.TryAdd("X-Content-Type-Options", "nosniff");

        // =================================================================
        // 4. REFERRER POLICY
        // =================================================================
        response.Headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");

        // =================================================================
        // 5. CONTENT SECURITY POLICY (CSP)
        // =================================================================
        var csp = BuildContentSecurityPolicy(context, nonce);
        response.Headers.TryAdd("Content-Security-Policy", csp);

        // Report-Only mode for monitoring stricter CSP
        if (!_environment.IsDevelopment())
        {
            var cspReportOnly = BuildStrictCspReportOnly(context, nonce);
            response.Headers.TryAdd("Content-Security-Policy-Report-Only", cspReportOnly);
        }

        // =================================================================
        // 6. HTTP STRICT TRANSPORT SECURITY (HSTS)
        // =================================================================
        if (!_environment.IsDevelopment())
        {
            response.Headers.TryAdd("Strict-Transport-Security",
                "max-age=31536000; includeSubDomains; preload");
        }

        // =================================================================
        // 7. PERMISSIONS POLICY
        // =================================================================
        response.Headers.TryAdd("Permissions-Policy", BuildPermissionsPolicy());

        // =================================================================
        // 8. CROSS-ORIGIN POLICIES
        // =================================================================
        response.Headers.TryAdd("Cross-Origin-Opener-Policy", "same-origin-allow-popups");
        response.Headers.TryAdd("Cross-Origin-Resource-Policy", "same-origin");
        response.Headers.TryAdd("Cross-Origin-Embedder-Policy", "credentialless");

        // =================================================================
        // 9. ADDITIONAL SECURITY HEADERS
        // =================================================================
        if (!_environment.IsDevelopment())
        {
            response.Headers.TryAdd("Expect-CT", "max-age=86400, enforce");
        }

        response.Headers.TryAdd("X-Permitted-Cross-Domain-Policies", "none");

        // =================================================================
        // 10. REMOVE INFORMATION DISCLOSURE HEADERS
        // =================================================================
        response.Headers.Remove("Server");
        response.Headers.Remove("X-Powered-By");
        response.Headers.Remove("X-AspNet-Version");
        response.Headers.Remove("X-AspNetMvc-Version");
        response.Headers.Remove("X-SourceFiles");
    }

    /// <summary>
    /// Build Content Security Policy with nonce-based script protection.
    /// - Production: Uses nonce for script-src (no unsafe-inline/unsafe-eval)
    /// - Development: Keeps unsafe-inline/unsafe-eval for Vite HMR
    /// - style-src always allows unsafe-inline (MUI/Emotion requirement)
    /// </summary>
    private string BuildContentSecurityPolicy(HttpContext context, string nonce)
    {
        var sentryDsn = _configuration["Sentry:Dsn"] ?? "";
        var sentryIngestDomain = "";
        if (!string.IsNullOrEmpty(sentryDsn) && Uri.TryCreate(sentryDsn, UriKind.Absolute, out var sentryUri))
        {
            sentryIngestDomain = $"https://{sentryUri.Host}";
        }

        var connectSources = BuildConnectSources();
        if (!string.IsNullOrEmpty(sentryIngestDomain))
        {
            connectSources += $" {sentryIngestDomain}";
        }

        // In development: keep unsafe-inline/unsafe-eval for Vite HMR and dev tools
        // In production: use nonce for scripts, remove unsafe-eval entirely
        var scriptSrc = _environment.IsDevelopment()
            ? "script-src 'self' 'unsafe-inline' 'unsafe-eval'"
            : $"script-src 'self' 'nonce-{nonce}'";

        var csp = new List<string>
        {
            "default-src 'self'",
            scriptSrc,

            // style-src: unsafe-inline required for MUI/Emotion runtime CSS injection
            "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com",

            "img-src 'self' data: https: blob:",
            "font-src 'self' https://fonts.gstatic.com data:",
            $"connect-src {connectSources}",
            "media-src 'self' blob: mediastream:",
            "worker-src 'self' blob:",
            "frame-src 'self'",
            "object-src 'none'",
            "base-uri 'self'",
            "form-action 'self'",
            "frame-ancestors 'none'",
            _environment.IsDevelopment() ? "" : "upgrade-insecure-requests"
        };

        return string.Join("; ", csp.Where(d => !string.IsNullOrEmpty(d)));
    }

    /// <summary>
    /// Build stricter CSP for Report-Only mode.
    /// Tests nonce-only policy without unsafe-inline for styles too.
    /// Violations are reported but not enforced.
    /// </summary>
    private string BuildStrictCspReportOnly(HttpContext context, string nonce)
    {
        var csp = new List<string>
        {
            "default-src 'self'",
            $"script-src 'self' 'nonce-{nonce}'",
            // Report-only: test without unsafe-inline for styles
            $"style-src 'self' 'nonce-{nonce}' https://fonts.googleapis.com",
            "img-src 'self' data: https: blob:",
            "font-src 'self' https://fonts.gstatic.com data:",
            $"connect-src {BuildConnectSources()}",
            "media-src 'self' blob: mediastream:",
            "worker-src 'self' blob:",
            "frame-src 'self'",
            "object-src 'none'",
            "base-uri 'self'",
            "form-action 'self'",
            "frame-ancestors 'none'",
            "report-uri /api/csp-report"
        };

        return string.Join("; ", csp.Where(d => !string.IsNullOrEmpty(d)));
    }

    /// <summary>
    /// Build Permissions Policy
    /// </summary>
    private string BuildPermissionsPolicy()
    {
        return string.Join(", ", new[]
        {
            "camera=(self)",
            "microphone=(self)",
            "display-capture=(self)",
            "geolocation=()",
            "payment=()",
            "usb=()",
            "bluetooth=()",
            "magnetometer=()",
            "gyroscope=()",
            "accelerometer=()",
            "autoplay=(self)",
            "encrypted-media=(self)",
            "fullscreen=(self)",
            "picture-in-picture=(self)",
            "screen-wake-lock=(self)",
            "web-share=(self)"
        });
    }

    private string BuildConnectSources()
    {
        var connectSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "'self'"
        };

        foreach (var origin in ResolveBrowserOrigins())
        {
            connectSources.Add(origin);

            if (origin.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                connectSources.Add("wss://" + origin["https://".Length..]);
            }
            else if (origin.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                connectSources.Add("ws://" + origin["http://".Length..]);
            }
        }

        if (_environment.IsDevelopment())
        {
            connectSources.Add("http://localhost:3000");
            connectSources.Add("http://localhost:8080");
            connectSources.Add("http://localhost:4443");
            connectSources.Add("ws://localhost:*");
            connectSources.Add("wss://localhost:*");
        }

        return string.Join(' ', connectSources);
    }

    private IEnumerable<string> ResolveBrowserOrigins()
    {
        var origins = new List<string>();

        AddOrigins(origins, _configuration.GetSection("Cors:AllowedOrigins").Get<string[]>());
        AddDelimitedOrigins(origins, _configuration["CORS_ORIGINS"] ?? Environment.GetEnvironmentVariable("CORS_ORIGINS"));
        AddOrigin(origins, _configuration["FRONTEND_URL"] ?? Environment.GetEnvironmentVariable("FRONTEND_URL"));
        AddOrigin(origins, _configuration["API_BASE_URL"] ?? Environment.GetEnvironmentVariable("API_BASE_URL"));
        AddOrigin(origins, _configuration["MEDIA_BASE_URL"] ?? Environment.GetEnvironmentVariable("MEDIA_BASE_URL"));

        if (_environment.IsDevelopment())
        {
            origins.Add("http://localhost:3000");
            origins.Add("http://localhost:8080");
            origins.Add("http://localhost:4443");
        }

        return origins
            .Select(NormalizeOrigin)
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Distinct(StringComparer.OrdinalIgnoreCase)!;
    }

    private static void AddOrigins(List<string> origins, IEnumerable<string>? values)
    {
        if (values == null)
        {
            return;
        }

        foreach (var value in values)
        {
            AddOrigin(origins, value);
        }
    }

    private static void AddDelimitedOrigins(List<string> origins, string? delimitedOrigins)
    {
        if (string.IsNullOrWhiteSpace(delimitedOrigins))
        {
            return;
        }

        foreach (var origin in delimitedOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            AddOrigin(origins, origin);
        }
    }

    private static void AddOrigin(List<string> origins, string? origin)
    {
        if (!string.IsNullOrWhiteSpace(origin))
        {
            origins.Add(origin);
        }
    }

    private static string? NormalizeOrigin(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin) || !Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }
}
