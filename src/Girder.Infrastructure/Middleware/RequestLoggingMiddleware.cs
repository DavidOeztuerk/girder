using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Girder.Infrastructure.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Girder.Core.Logging;

namespace Girder.Infrastructure.Middleware;

public partial class RequestLoggingMiddleware(
    RequestDelegate next,
    ILogger<RequestLoggingMiddleware> logger,
    IOptions<ObservabilityOptions> observabilityOptions)
{
    private readonly RequestDelegate _next = next;
    private readonly ILogger<RequestLoggingMiddleware> _logger = logger;
    private readonly ObservabilityOptions _observabilityOptions = observabilityOptions.Value;

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

            return Shape.OfJson(bodyText, request.ContentType);
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

            return Shape.OfJson(text);
        }

        return null;
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

    /// <summary>
    /// Removes token-bearing parameters from a query string.
    /// </summary>
    /// <remarks>
    /// A verification link or a password-reset link is a credential in a URL,
    /// and a URL is the one part of a request that everything logs.
    /// </remarks>
    private static string? RedactQueryString(string? queryString)
    {
        if (string.IsNullOrEmpty(queryString))
        {
            return queryString;
        }

        return SensitiveQueryParamRegex().Replace(queryString, match =>
        {
            var parameter = match.Value.Split('=')[0];
            return $"{parameter}=[REDACTED]";
        });
    }

    /// <summary>Headers that carry a credential and are never written out.</summary>
    private static bool IsSensitiveHeader(string headerName) =>
        headerName.ToLowerInvariant() is
            "authorization" or "cookie" or "set-cookie" or "x-api-key" or "x-auth-token";

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
