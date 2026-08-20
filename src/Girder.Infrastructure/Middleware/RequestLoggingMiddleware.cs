using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Girder.Infrastructure.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Girder.Infrastructure.Middleware;

public partial class RequestLoggingMiddleware(
    RequestDelegate next,
    ILogger<RequestLoggingMiddleware> logger,
    IOptions<ObservabilityOptions> observabilityOptions)
{
    private readonly RequestDelegate _next = next;
    private readonly ILogger<RequestLoggingMiddleware> _logger = logger;
    private readonly ObservabilityOptions _observabilityOptions = observabilityOptions.Value;

    /// <summary>
    /// Field names whose values are removed from a body before it is logged.
    /// </summary>
    /// <remarks>
    /// The same list the CQRS logging behaviour uses. Keeping a second copy
    /// here is how the two drifted apart: this one knew about tokens and
    /// addresses and not about names, so a profile update went into the log in
    /// full.
    /// </remarks>
    private static readonly Regex SensitiveJsonField = new(
        $$""""(?<key>{{Girder.Core.Logging.SensitiveFieldNames.Alternation}})"\s*:\s*(?:"[^"]*"|-?\d+(?:\.\d+)?|true|false|null)"""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Bodies that are redacted whole rather than field by field.
    /// </summary>
    /// <remarks>
    /// A credential arriving in a body is worth more than the diagnostic value
    /// of the rest of it.
    /// </remarks>
    private static readonly string[] RedactWholeBody =
    [
        "password", "accesstoken", "access_token", "refreshtoken", "refresh_token",
        "secret", "credential", "privatekey", "private_key"
    ];

    [GeneratedRegex(
        @"(access_token|token|code|email)=[^&\s""]*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SensitiveQueryParamRegex();

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_observabilityOptions.EnableDetailedHttpLogging || ShouldSkipDetailedLogging(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var originalBodyStream = context.Response.Body;

        try
        {
            await LogRequestAsync(context);

            using var responseBody = new MemoryStream();
            context.Response.Body = responseBody;

            await _next(context);

            stopwatch.Stop();
            await LogResponseAsync(context, responseBody, stopwatch.ElapsedMilliseconds);

            responseBody.Seek(0, SeekOrigin.Begin);
            await responseBody.CopyToAsync(originalBodyStream);
        }
        finally
        {
            context.Response.Body = originalBodyStream;
        }
    }

    private async Task LogRequestAsync(HttpContext context)
    {
        var request = context.Request;

        var requestLog = new
        {
            Method = request.Method,
            Path = request.Path.Value,
            QueryString = RedactQueryString(request.QueryString.Value),
            Headers = request.Headers
                .Where(h => !IsSensitiveHeader(h.Key))
                .ToDictionary(h => h.Key, h => h.Value.ToString()),
            UserAgent = request.Headers.UserAgent.ToString(),
            RemoteIpAddress = context.Connection.RemoteIpAddress?.ToString(),
            Body = await GetRequestBodyAsync(request)
        };

        _logger.LogInformation("Incoming Request: {@RequestLog}", requestLog);
    }

    private async Task LogResponseAsync(HttpContext context, MemoryStream responseBody, long elapsedMs)
    {
        var response = context.Response;

        var responseLog = new
        {
            StatusCode = response.StatusCode,
            ElapsedMilliseconds = elapsedMs,
            Headers = response.Headers
                .Where(h => !IsSensitiveHeader(h.Key))
                .ToDictionary(h => h.Key, h => h.Value.ToString()),
            ContentLength = response.ContentLength,
            Body = await GetResponseBodyAsync(responseBody, context.Request.Path)
        };

        if (response.StatusCode >= 400)
        {
            _logger.LogWarning("Outgoing Response (Error): {@ResponseLog}", responseLog);
        }
        else
        {
            _logger.LogInformation("Outgoing Response: {@ResponseLog}", responseLog);
        }
    }

    private static async Task<string?> GetRequestBodyAsync(HttpRequest request)
    {
        if (!request.HasFormContentType && request.ContentLength > 0)
        {
            request.EnableBuffering();

            var buffer = new byte[Convert.ToInt32(request.ContentLength)];
            await request.Body.ReadExactlyAsync(buffer, 0, buffer.Length);

            var bodyText = Encoding.UTF8.GetString(buffer);
            request.Body.Position = 0;

            return SanitizeBody(bodyText);
        }

        return null;
    }

    private static async Task<string?> GetResponseBodyAsync(MemoryStream responseBody, PathString requestPath)
    {
        if (responseBody.Length > 0)
        {
            responseBody.Seek(0, SeekOrigin.Begin);
            var text = await new StreamReader(responseBody).ReadToEndAsync();
            responseBody.Seek(0, SeekOrigin.Begin);

            // Auth endpoints return tokens — never log their response bodies
            if (IsAuthEndpoint(requestPath))
            {
                return "[REDACTED - Auth response]";
            }

            return SanitizeBody(text.Length > 1000 ? text[..1000] + "..." : text);
        }

        return null;
    }

    private static string SanitizeBody(string body)
    {
        var lowerBody = body.ToLowerInvariant();

        if (RedactWholeBody.Any(keyword => lowerBody.Contains(keyword)))
        {
            return "[REDACTED - Contains sensitive information]";
        }

        // Field by field, so what is not about a person stays readable.
        var sanitized = SensitiveJsonField.Replace(
            body, match => $"\"{match.Groups["key"].Value}\": \"[REDACTED]\"");

        sanitized = SensitiveQueryParamRegex().Replace(sanitized, match =>
        {
            var parameter = match.Value.Split('=')[0];
            return $"{parameter}=[REDACTED]";
        });

        // What a person typed into a free-text field. No list of field names
        // reaches an address inside a todo title.
        sanitized = Girder.Core.Logging.SensitiveValuePatterns.MaskAll(sanitized);

        return sanitized.Length > 1000 ? sanitized[..1000] + "..." : sanitized;
    }

    private static string? RedactQueryString(string? queryString)
    {
        if (string.IsNullOrEmpty(queryString)) return queryString;

        return SensitiveQueryParamRegex().Replace(queryString, m =>
        {
            var paramName = m.Value.Split('=')[0];
            return $"{paramName}=[REDACTED]";
        });
    }

    private static bool IsAuthEndpoint(PathString path)
    {
        if (!path.HasValue) return false;
        var p = path.Value!;
        return p.Contains("/auth/", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/login", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/register", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/refresh", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/verify-email", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/service-token", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSensitiveHeader(string headerName)
    {
        var sensitiveHeaders = new[]
        {
            "authorization",
            "cookie",
            "x-api-key",
            "x-auth-token",
            "set-cookie"
        };

        return sensitiveHeaders.Contains(headerName.ToLowerInvariant());
    }

    private static bool ShouldSkipDetailedLogging(PathString path)
    {
        if (!path.HasValue)
        {
            return false;
        }

        var value = path.Value!;
        return value.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/metrics", StringComparison.OrdinalIgnoreCase);
    }
}
