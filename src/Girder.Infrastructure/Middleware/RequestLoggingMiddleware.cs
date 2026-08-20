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

            return DescribeBody(bodyText, request.ContentType);
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

            return DescribeBody(text, contentType: null);
        }

        return null;
    }

    /// <summary>
    /// Describes a body without reproducing any of it.
    /// </summary>
    /// <remarks>
    /// Field names and value sizes, never values. Redacting the fields we
    /// recognise was enumeration, and enumeration is always incomplete — a case
    /// reference, a note to a doctor, the name of a company someone is leaving:
    /// none of it is on any list, and all of it went into the log in full.
    /// <para>
    /// This is safe because of how it is built rather than because of what
    /// somebody remembered to add, and it keeps what a person debugging
    /// actually needs: which fields arrived, and whether they were empty.
    /// </para>
    /// </remarks>
    private static string DescribeBody(string body, string? contentType)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "[empty]";
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(body);
            var description = new StringBuilder();
            Describe(document.RootElement, description, depth: 0);
            return description.ToString();
        }
        catch (System.Text.Json.JsonException)
        {
            // Not JSON, so there is no structure to describe and no safe way to
            // show any of it.
            return $"[{body.Length} characters, {contentType ?? "unknown type"}]";
        }
    }

    private const int MaxDescribedDepth = 3;

    private static void Describe(
        System.Text.Json.JsonElement element,
        StringBuilder into,
        int depth)
    {
        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object when depth >= MaxDescribedDepth:
                into.Append("{…}");
                break;

            case System.Text.Json.JsonValueKind.Object:
                into.Append('{');
                var first = true;
                foreach (var property in element.EnumerateObject())
                {
                    if (!first)
                    {
                        into.Append(", ");
                    }

                    first = false;
                    into.Append(property.Name).Append(": ");
                    Describe(property.Value, into, depth + 1);
                }

                into.Append('}');
                break;

            case System.Text.Json.JsonValueKind.Array:
                into.Append('[').Append(element.GetArrayLength()).Append(" items]");
                break;

            case System.Text.Json.JsonValueKind.String:
                // The length, because "was it empty" is the question a log is
                // asked. The characters themselves are the person's.
                into.Append("string(").Append(element.GetString()?.Length ?? 0).Append(')');
                break;

            case System.Text.Json.JsonValueKind.Number:
                // A number is as identifying as a string — a salary, a balance,
                // a date of birth as a timestamp.
                into.Append("number");
                break;

            case System.Text.Json.JsonValueKind.Null:
                into.Append("null");
                break;

            default:
                into.Append("bool");
                break;
        }
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
