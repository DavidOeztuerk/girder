using Girder.Abstractions.Observability;
using Girder.Abstractions.Security.Audit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System.Text;
using System.Text.Json;
using Girder.Infrastructure.Http;

namespace Girder.Infrastructure.Security.InputSanitization;

/// <summary>
/// Middleware for automatic input sanitization and injection detection
/// </summary>
public class InputSanitizationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IInputSanitizer _inputSanitizer;
    private readonly ILogger<InputSanitizationMiddleware> _logger;
    private readonly InputSanitizationOptions _options;

    public InputSanitizationMiddleware(
        RequestDelegate next,
        IInputSanitizer inputSanitizer,
        ILogger<InputSanitizationMiddleware> logger,
        Microsoft.Extensions.Options.IOptions<InputSanitizationOptions> options)
    {
        _next = next;
        _inputSanitizer = inputSanitizer;
        _logger = logger;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.EnableInputSanitization || ShouldSkipSanitization(context))
        {
            await _next(context);
            return;
        }

        try
        {
            var shouldContinue = await ProcessRequestAsync(context);
            if (!shouldContinue)
            {
                // Request was blocked due to injection detection or sanitization error
                // Response has already been written, do not call _next
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during input sanitization");

            if (_options.BlockOnSanitizationError)
            {
                await HandleSanitizationError(context, "Input sanitization failed");
                return;
            }
        }

        await _next(context);
    }

    /// <summary>
    /// Process and sanitize request inputs
    /// </summary>
    /// <returns>True if request should continue to next middleware, false if blocked</returns>
    private async Task<bool> ProcessRequestAsync(HttpContext context)
    {
        // Sanitize query parameters
        if (context.Request.Query.Any())
        {
            if (!await SanitizeQueryParameters(context))
                return false;
        }

        // Sanitize headers
        if (!await SanitizeHeaders(context))
            return false;

        // Sanitize request body for POST/PUT/PATCH
        if (HasRequestBody(context))
        {
            if (!await SanitizeRequestBody(context))
                return false;
        }

        // Sanitize form data
        if (context.Request.HasFormContentType)
        {
            if (!await SanitizeFormData(context))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Sanitize query parameters
    /// </summary>
    /// <returns>True if request should continue, false if blocked</returns>
    private async Task<bool> SanitizeQueryParameters(HttpContext context)
    {
        var sanitizedQuery = new Dictionary<string, StringValues>();
        var hasInjection = false;

        foreach (var param in context.Request.Query)
        {
            var key = param.Key;
            var values = param.Value.ToArray();

            for (int i = 0; i < values.Length; i++)
            {
                var value = values[i] ?? string.Empty;

                // Detect injection attempts
                var injectionResult = _inputSanitizer.DetectInjectionAttempt(value);
                if (injectionResult.InjectionDetected)
                {
                    hasInjection = true;
                    await LogInjectionAttempt(context, "QueryParameter", key, value, injectionResult);

                    if (_options.BlockOnInjectionDetection)
                    {
                        await HandleInjectionAttempt(context, injectionResult);
                        return false; // Request blocked
                    }
                }

                // Sanitize the value
                var sanitized = _inputSanitizer.SanitizeText(value, GetSanitizationOptions(key));
                values[i] = sanitized;
            }

            // Store sanitized values for later reconstruction
            sanitizedQuery[key] = new StringValues(values);
        }

        // Rebuild the entire query string at once with all sanitized values
        var queryBuilder = new QueryBuilder(sanitizedQuery);
        context.Request.QueryString = queryBuilder.ToQueryString();

        if (hasInjection && _options.LogInjectionAttempts)
        {
            _logger.LogWarning("Injection attempt detected in query parameters for {Path}", context.Request.Path);
        }

        return true; // Continue processing
    }

    /// <summary>
    /// Sanitize request headers
    /// </summary>
    /// <returns>True if request should continue, false if blocked</returns>
    private async Task<bool> SanitizeHeaders(HttpContext context)
    {
        // Addresses, and nothing else.
        //
        // Referer used to be in this list and is not input: it names the page the
        // caller came from, chosen by our own routing. Every request from a page
        // called /delete-account carried it, so every one of them was refused —
        // including the call asking who is signed in, which left that page telling
        // a signed-in person to sign in. Whoever wants the Referer checked is
        // checking their own URL structure.
        //
        // User-Agent was dropped earlier for the same class of reason.
        var headersToSanitize = new[] { "X-Forwarded-For", "X-Real-IP" };

        foreach (var headerName in headersToSanitize)
        {
            if (context.Request.Headers.TryGetValue(headerName, out var headerValues))
            {
                var sanitizedValues = new List<string>();

                foreach (var value in headerValues)
                {
                    if (!string.IsNullOrEmpty(value))
                    {
                        var injectionResult = _inputSanitizer.DetectInjectionAttempt(value);
                        if (injectionResult.InjectionDetected)
                        {
                            await LogInjectionAttempt(context, "Header", headerName, value, injectionResult);

                            if (_options.BlockOnInjectionDetection)
                            {
                                await HandleInjectionAttempt(context, injectionResult);
                                return false; // Request blocked
                            }
                        }

                        var sanitized = _inputSanitizer.SanitizeText(value, new TextSanitizationOptions
                        {
                            MaxLength = 2048,
                            RemoveControlCharacters = true,
                            NormalizeWhitespace = true
                        });

                        sanitizedValues.Add(sanitized);
                    }
                }

                if (sanitizedValues.Any())
                {
                    context.Request.Headers[headerName] = sanitizedValues.ToArray();
                }
            }
        }

        return true; // Continue processing
    }

    /// <summary>
    /// Sanitize request body
    /// </summary>
    /// <returns>True if request should continue, false if blocked</returns>
    private async Task<bool> SanitizeRequestBody(HttpContext context)
    {
        if (context.Request.ContentLength > _options.MaxRequestBodySize)
        {
            await HandleSanitizationError(context, "Request body too large for sanitization");
            return false; // Request blocked
        }

        context.Request.EnableBuffering();

        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        context.Request.Body.Position = 0;

        if (string.IsNullOrEmpty(body))
            return true;

        var contentType = context.Request.ContentType?.ToLowerInvariant() ?? "";

        if (contentType.Contains("application/json"))
        {
            return !_options.InspectJsonBodies || await SanitizeJsonBody(context, body);
        }
        else if (contentType.Contains("application/xml") || contentType.Contains("text/xml"))
        {
            return await SanitizeXmlBody(context, body);
        }
        else if (contentType.Contains("text/plain"))
        {
            return await SanitizeTextBody(context, body);
        }

        return true;
    }

    /// <summary>
    /// Inspects every string in a JSON body, and refuses the request over any one
    /// of them.
    /// </summary>
    /// <remarks>
    /// <para>The raw document is never matched against: braces, quotes and colons
    /// are JSON's own syntax, so every body would look like an attack. Each string
    /// value goes through the same detector the query string does.</para>
    ///
    /// <para>This surface used to be exempt. A substring list decided whether a
    /// value was worth sanitising, nothing was ever refused, and the body was
    /// re-serialised on every request whether or not anything had changed. For an
    /// application whose whole write surface is JSON that left the middleware
    /// inspecting no place anything arrives, while quietly rewriting every request
    /// that passed through it.</para>
    ///
    /// <para><strong>The body is left exactly as it arrived.</strong> Refusing a
    /// request says what happened; editing someone's text without saying so does
    /// not, and a model binder downstream has no way to tell the two apart. The
    /// re-serialisation also turned every number into a <c>decimal</c> and rebuilt
    /// property names — a rewrite nobody asked for on a body nobody had objected
    /// to.</para>
    /// </remarks>
    /// <returns>True if the request should continue, false if it was refused.</returns>
    private async Task<bool> SanitizeJsonBody(HttpContext context, string jsonBody)
    {
        try
        {
            var document = JsonDocument.Parse(jsonBody);

            var found = new Sighting();
            InspectJsonElement(document.RootElement, found);

            if (found.Result is { } injection)
            {
                await LogInjectionAttempt(
                    context, "JsonBody", found.Field ?? "RequestBody", "", injection);

                if (_options.BlockOnInjectionDetection)
                {
                    await HandleInjectionAttempt(context, injection);
                    return false;
                }
            }

            return true;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid JSON in request body");

            if (_options.BlockOnInvalidInput)
            {
                await HandleSanitizationError(context, "Invalid JSON format");
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Sanitize XML request body
    /// </summary>
    /// <returns>True if request should continue, false if blocked</returns>
    private async Task<bool> SanitizeXmlBody(HttpContext context, string xmlBody)
    {
        // Detect XML injection patterns
        var injectionResult = _inputSanitizer.DetectInjectionAttempt(xmlBody);
        if (injectionResult.InjectionDetected)
        {
            await LogInjectionAttempt(context, "XmlBody", "RequestBody", xmlBody, injectionResult);

            if (_options.BlockOnInjectionDetection)
            {
                await HandleInjectionAttempt(context, injectionResult);
                return false; // Request blocked
            }
        }

        // Basic XML sanitization (remove dangerous patterns)
        var sanitized = _inputSanitizer.SanitizeText(xmlBody, new TextSanitizationOptions
        {
            AllowHtml = false,
            RemoveControlCharacters = true,
            BlacklistedPatterns = new List<string>
            {
                @"<!DOCTYPE[^>]*>", // Remove DOCTYPE declarations
                @"<!ENTITY[^>]*>",  // Remove entity declarations
                @"&[^;]+;",         // Remove entity references
            }
        });

        var sanitizedBytes = Encoding.UTF8.GetBytes(sanitized);
        context.Request.Body = new MemoryStream(sanitizedBytes);
        context.Request.ContentLength = sanitizedBytes.Length;
        return true;
    }

    /// <summary>
    /// Sanitize plain text request body
    /// </summary>
    /// <returns>True if request should continue, false if blocked</returns>
    private async Task<bool> SanitizeTextBody(HttpContext context, string textBody)
    {
        var injectionResult = _inputSanitizer.DetectInjectionAttempt(textBody);
        if (injectionResult.InjectionDetected)
        {
            await LogInjectionAttempt(context, "TextBody", "RequestBody", textBody, injectionResult);

            if (_options.BlockOnInjectionDetection)
            {
                await HandleInjectionAttempt(context, injectionResult);
                return false; // Request blocked
            }
        }

        var sanitized = _inputSanitizer.SanitizeText(textBody, new TextSanitizationOptions
        {
            AllowHtml = false,
            MaxLength = _options.MaxTextFieldLength,
            RemoveControlCharacters = true,
            NormalizeWhitespace = true
        });

        var sanitizedBytes = Encoding.UTF8.GetBytes(sanitized);
        context.Request.Body = new MemoryStream(sanitizedBytes);
        context.Request.ContentLength = sanitizedBytes.Length;
        return true;
    }

    /// <summary>
    /// Sanitize form data
    /// </summary>
    /// <returns>True if request should continue, false if blocked</returns>
    private async Task<bool> SanitizeFormData(HttpContext context)
    {
        try
        {
            var form = await context.Request.ReadFormAsync();
            var sanitizedForm = new Dictionary<string, string>();

            foreach (var field in form)
            {
                var key = field.Key;
                var values = field.Value.ToArray();

                for (int i = 0; i < values.Length; i++)
                {
                    var value = values[i] ?? string.Empty;

                    var injectionResult = _inputSanitizer.DetectInjectionAttempt(value);
                    if (injectionResult.InjectionDetected)
                    {
                        await LogInjectionAttempt(context, "FormField", key, value, injectionResult);

                        if (_options.BlockOnInjectionDetection)
                        {
                            await HandleInjectionAttempt(context, injectionResult);
                            return false; // Request blocked
                        }
                    }

                    var sanitized = _inputSanitizer.SanitizeText(value, GetSanitizationOptions(key));
                    sanitizedForm[key] = sanitized;
                }
            }

            // Note: Modifying form data in middleware is complex and may require custom form collection
            // This is a simplified implementation
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading form data");
            return true; // Continue on error, let downstream handle validation
        }
    }

    /// <summary>What a walk of the body found, and in which field.</summary>
    /// <remarks>
    /// Carried through the walk rather than kept on the middleware: one instance
    /// serves every request in the pipeline, so a field here would be one
    /// request's finding read by another's.
    /// </remarks>
    private sealed class Sighting
    {
        public InjectionDetectionResult? Result { get; set; }

        public string? Field { get; set; }
    }

    /// <summary>
    /// Walks the body and stops at the first string that carries injection
    /// syntax. Reads only — nothing is written back.
    /// </summary>
    private void InspectJsonElement(JsonElement element, Sighting found, string? field = null)
    {
        if (found.Result is not null)
        {
            return;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    InspectJsonElement(property.Value, found, property.Name);
                }
                return;

            case JsonValueKind.Array:
                foreach (var entry in element.EnumerateArray())
                {
                    InspectJsonElement(entry, found, field);
                }
                return;

            case JsonValueKind.String:
                var value = element.GetString() ?? "";

                // An address is not input in the sense meant here: the @ and the
                // dots are its own syntax.
                if (value.Contains('@') && IsLikelyEmail(value))
                {
                    return;
                }

                var injection = _inputSanitizer.DetectInjectionAttempt(value);
                if (injection.InjectionDetected)
                {
                    found.Result = injection;
                    found.Field = field;
                }

                return;

            default:
                return;
        }
    }

    private TextSanitizationOptions GetSanitizationOptions(string fieldName)
    {
        var fieldLower = fieldName.ToLowerInvariant();
        
        // Field-specific sanitization rules
        return fieldLower switch
        {
            var f when f.Contains("email") => new TextSanitizationOptions
            {
                MaxLength = 254,
                AllowHtml = false,
                RemoveControlCharacters = true,
                BlacklistedCharacters = new HashSet<char> { '<', '>', '"', '\'' }
            },
            var f when f.Contains("url") || f.Contains("link") => new TextSanitizationOptions
            {
                MaxLength = 2048,
                AllowHtml = false,
                RemoveControlCharacters = true
            },
            var f when f.Contains("phone") => new TextSanitizationOptions
            {
                MaxLength = 20,
                AllowHtml = false,
                RemoveControlCharacters = true,
                BlacklistedPatterns = new List<string> { @"[^\d\+\-\(\)\s\.]" }
            },
            var f when f.Contains("name") => new TextSanitizationOptions
            {
                MaxLength = 100,
                AllowHtml = false,
                RemoveControlCharacters = true,
                BlacklistedCharacters = new HashSet<char> { '<', '>', '"', '\'', '&' }
            },
            var f when f.Contains("description") || f.Contains("comment") => new TextSanitizationOptions
            {
                MaxLength = _options.MaxTextFieldLength,
                AllowHtml = _options.AllowHtmlInTextFields,
                HtmlLevel = HtmlSanitizationLevel.Basic,
                RemoveControlCharacters = true
            },
            _ => new TextSanitizationOptions
            {
                MaxLength = _options.MaxTextFieldLength,
                AllowHtml = false,
                RemoveControlCharacters = true
            }
        };
    }

    private async Task LogInjectionAttempt(
        HttpContext context, 
        string inputType, 
        string fieldName, 
        string value, 
        InjectionDetectionResult injectionResult)
    {
        if (!_options.LogInjectionAttempts)
            return;

        var clientIp = ClientAddress.Of(context);
        var userAgent = context.Request.Headers.UserAgent.ToString();

        _logger.LogWarning(
            "Injection attempt detected: Type={InjectionType}, Field={Field}, IP={IP}, UserAgent={UserAgent}, Risk={Risk}, Patterns={Patterns}",
            injectionResult.InjectionType,
            $"{inputType}:{fieldName}",
            clientIp,
            userAgent,
            injectionResult.RiskLevel,
            string.Join(", ", injectionResult.DetectedPatterns));

        // Log to security audit system if available
        try
        {
            var auditService = context.RequestServices.GetService<Girder.Abstractions.Security.Audit.ISecurityAuditService>();
            if (auditService != null)
            {
                await auditService.LogSecurityEventAsync(
                    "InjectionAttemptDetected",
                    $"Injection attempt detected in {inputType}:{fieldName}",
                    injectionResult.RiskLevel switch
                    {
                        RiskLevel.Critical => Girder.Abstractions.Security.Audit.SecurityEventSeverity.Critical,
                        RiskLevel.High => Girder.Abstractions.Security.Audit.SecurityEventSeverity.High,
                        RiskLevel.Medium => Girder.Abstractions.Security.Audit.SecurityEventSeverity.Medium,
                        _ => Girder.Abstractions.Security.Audit.SecurityEventSeverity.Low
                    },
                    new
                    {
                        InjectionType = injectionResult.InjectionType?.ToString(),
                        InputType = inputType,
                        FieldName = fieldName,
                        DetectedPatterns = injectionResult.DetectedPatterns,
                        RiskLevel = injectionResult.RiskLevel.ToString(),
                        ConfidenceScore = injectionResult.ConfidenceScore,
                        InputValue = _options.LogSensitiveData ? value : "[REDACTED]"
                    });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log injection attempt to audit service");
        }
    }

    /// <summary>
    /// Writes the refusal in the same shape as every other error Girder produces.
    /// </summary>
    /// <remarks>
    /// It used to be <c>application/json</c> with an <c>error</c> string and no
    /// id at all, so a caller had a second error shape to learn and nothing to
    /// quote when they reported being refused. The one thing it still cannot say
    /// is <em>which field</em> — a request refused here never reached the
    /// validator that knows. That is the honest cost of an edge filter, and the
    /// reason <see cref="InputSanitizationOptions.InspectJsonBodies"/> exists.
    /// </remarks>
    private async Task HandleInjectionAttempt(HttpContext context, InjectionDetectionResult injectionResult)
    {
        await WriteProblem(
            context,
            "https://girder.dev/problems/input-validation-failed",
            "Potentially malicious input detected",
            _options.IncludeInjectionDetailsInResponse
                ? new
                {
                    injectionType = injectionResult.InjectionType?.ToString(),
                    riskLevel = injectionResult.RiskLevel.ToString(),
                    detectedPatterns = injectionResult.DetectedPatterns
                }
                : null);
    }

    private async Task HandleSanitizationError(HttpContext context, string message)
    {
        await WriteProblem(
            context,
            "https://girder.dev/problems/input-sanitization-failed",
            message,
            details: null);
    }

    private static async Task WriteProblem(
        HttpContext context,
        string type,
        string detail,
        object? details)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.ContentType = "application/problem+json";

        var correlationId = CorrelationId.Current
            ?? context.Items[CorrelationId.BaggageKey] as string
            ?? context.Request.Headers[CorrelationId.HeaderName].FirstOrDefault()
            ?? context.TraceIdentifier;

        var json = JsonSerializer.Serialize(
            new
            {
                type,
                title = "Bad request",
                status = StatusCodes.Status400BadRequest,
                detail,
                instance = context.Request.Path.Value,
                correlationId,
                traceId = context.TraceIdentifier,
                details,
                timestamp = DateTime.UtcNow
            },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        await context.Response.WriteAsync(json);
    }

    private static bool ShouldSkipSanitization(HttpContext context)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";

        // Skip for health checks, metrics, static content, and auth endpoints
        if (path.Contains("/health") ||
            path.Contains("/metrics") ||
            path.Contains("/swagger") ||
            path.Contains("/favicon") ||
            path.Contains("/auth/register") ||  // Skip auth - passwords may contain special chars
            path.Contains("/auth/login") ||     // Skip auth - passwords may contain special chars
            path.Contains("/hub") ||             // Skip SignalR hubs - JWT tokens in query string
            path.Contains("/hubs/") ||           // Skip SignalR hubs - JWT tokens in query string
            path.Contains("/webhook") ||         // Skip webhooks - signature validation requires raw body
            path.Contains("/payments/webhook") || // Skip Stripe webhooks specifically
            path.EndsWith(".css") ||
            path.EndsWith(".js") ||
            path.EndsWith(".png") ||
            path.EndsWith(".jpg") ||
            path.EndsWith(".gif"))
        {
            return true;
        }

        // Skip for OPTIONS requests
        if (context.Request.Method == "OPTIONS")
        {
            return true;
        }

        return false;
    }

    private static bool HasRequestBody(HttpContext context)
    {
        return context.Request.Method is "POST" or "PUT" or "PATCH" &&
               context.Request.ContentLength > 0;
    }
    private static bool IsLikelyEmail(string value)
    {
        // Simple email check - contains @ and a dot after it
        var atIndex = value.IndexOf('@');
        if (atIndex > 0 && atIndex < value.Length - 1)
        {
            var domainPart = value.Substring(atIndex + 1);
            return domainPart.Contains('.');
        }
        return false;
    }

}

/// <summary>
/// Input sanitization middleware configuration options
/// </summary>
public class InputSanitizationOptions
{
    /// <summary>
    /// Enable input sanitization middleware
    /// </summary>
    public bool EnableInputSanitization { get; set; } = true;

    /// <summary>
    /// Block requests when injection is detected
    /// </summary>
    public bool BlockOnInjectionDetection { get; set; } = true;

    /// <summary>
    /// Whether the string values of a JSON body are inspected.
    /// </summary>
    /// <remarks>
    /// <para><strong>On.</strong> Off, this middleware inspects nothing for an
    /// application whose write surface is JSON — which is most of them — while
    /// still refusing traffic on the query string it barely uses. A promise
    /// wider than its effect is worse than no promise, because people rely on
    /// it.</para>
    ///
    /// <para><strong>When to turn it off.</strong> An application that validates
    /// every field itself and answers with a problem document naming the field.
    /// This filter runs first and cannot name a field — a request refused here
    /// never reached the validator that knows which one — so a precise 422 turns
    /// into a blunt 400, and the person filling in the form is told less than
    /// before. Measured in one application: five field-level refusals across
    /// three services lost their field name this way.</para>
    ///
    /// <para>Turning it off is a decision about who reports the error, not about
    /// whether the value is refused. Turning it off <em>without</em> validators
    /// is a decision to refuse nothing.</para>
    /// </remarks>
    public bool InspectJsonBodies { get; set; } = true;

    /// <summary>
    /// Block requests when sanitization fails
    /// </summary>
    public bool BlockOnSanitizationError { get; set; } = false;

    /// <summary>
    /// Block requests with invalid input formats
    /// </summary>
    public bool BlockOnInvalidInput { get; set; } = false;

    /// <summary>
    /// Log injection attempts
    /// </summary>
    public bool LogInjectionAttempts { get; set; } = true;

    /// <summary>
    /// Log sensitive data in injection logs (for debugging)
    /// </summary>
    public bool LogSensitiveData { get; set; } = false;

    /// <summary>
    /// Include injection details in error responses
    /// </summary>
    public bool IncludeInjectionDetailsInResponse { get; set; } = false;

    /// <summary>
    /// Allow HTML in text fields
    /// </summary>
    public bool AllowHtmlInTextFields { get; set; } = false;

    /// <summary>
    /// Maximum request body size to sanitize (bytes)
    /// </summary>
    public long MaxRequestBodySize { get; set; } = 10 * 1024 * 1024; // 10MB

    /// <summary>
    /// Maximum text field length
    /// </summary>
    public int MaxTextFieldLength { get; set; } = 10000;

    /// <summary>
    /// Paths to exclude from sanitization
    /// </summary>
    public List<string> ExcludedPaths { get; set; } = new()
    {
        "/health",
        "/metrics",
        "/swagger"
    };

    /// <summary>
    /// Content types to exclude from sanitization
    /// </summary>
    public List<string> ExcludedContentTypes { get; set; } = new()
    {
        "multipart/form-data",
        "application/octet-stream",
        "image/*",
        "video/*",
        "audio/*"
    };
}

/// <summary>
/// Extension methods for using input sanitization middleware
/// </summary>
public static class InputSanitizationMiddlewareExtensions
{
    /// <summary>
    /// Use input sanitization middleware
    /// </summary>
    public static IApplicationBuilder UseInputSanitization(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<InputSanitizationMiddleware>();
    }
}