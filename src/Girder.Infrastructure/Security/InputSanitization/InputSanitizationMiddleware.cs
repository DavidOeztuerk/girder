using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System.Text;
using System.Text.Json;

namespace Infrastructure.Security.InputSanitization;

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
        // Skip User-Agent validation as it contains legitimate characters that trigger false positives
        var headersToSanitize = new[] { "Referer", "X-Forwarded-For", "X-Real-IP" };

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
            return await SanitizeJsonBody(context, body);
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
    /// Sanitize JSON request body
    /// </summary>
    /// <returns>True if request should continue, false if blocked</returns>
    private async Task<bool> SanitizeJsonBody(HttpContext context, string jsonBody)
    {
        try
        {
            // Skip injection detection on raw JSON as it contains legitimate characters
            // Instead, parse JSON first and then check individual values

            // Parse and sanitize JSON
            var jsonDoc = JsonDocument.Parse(jsonBody);
            var sanitized = SanitizeJsonElement(jsonDoc.RootElement);

            var sanitizedJson = JsonSerializer.Serialize(sanitized, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            // Replace request body with sanitized version
            var sanitizedBytes = Encoding.UTF8.GetBytes(sanitizedJson);
            context.Request.Body = new MemoryStream(sanitizedBytes);
            context.Request.ContentLength = sanitizedBytes.Length;
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

    private object? SanitizeJsonElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var obj = new Dictionary<string, object?>();
                foreach (var prop in element.EnumerateObject())
                {
                    var sanitizedValue = SanitizeJsonElement(prop.Value);
                    obj[prop.Name] = sanitizedValue;
                }
                return obj;

            case JsonValueKind.Array:
                return element.EnumerateArray().Select(SanitizeJsonElement).ToArray();

            case JsonValueKind.String:
                var stringValue = element.GetString() ?? "";
                // Only sanitize for actual dangerous patterns, not JSON syntax
                // Skip email validation as @ is legitimate in emails
                if (!stringValue.Contains("@") || !IsLikelyEmail(stringValue))
                {
                    // Check for actual injection attempts in string values
                    if (ContainsDangerousPattern(stringValue))
                    {
                        return _inputSanitizer.SanitizeText(stringValue, new TextSanitizationOptions
                        {
                            AllowHtml = false,
                            MaxLength = _options.MaxTextFieldLength,
                            RemoveControlCharacters = true
                        });
                    }
                }
                return stringValue;

            case JsonValueKind.Number:
                return element.GetDecimal();

            case JsonValueKind.True:
            case JsonValueKind.False:
                return element.GetBoolean();

            case JsonValueKind.Null:
            default:
                return null;
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

        var clientIp = GetClientIpAddress(context);
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
            var auditService = context.RequestServices.GetService<Audit.ISecurityAuditService>();
            if (auditService != null)
            {
                await auditService.LogSecurityEventAsync(
                    "InjectionAttemptDetected",
                    $"Injection attempt detected in {inputType}:{fieldName}",
                    injectionResult.RiskLevel switch
                    {
                        RiskLevel.Critical => Audit.SecurityEventSeverity.Critical,
                        RiskLevel.High => Audit.SecurityEventSeverity.High,
                        RiskLevel.Medium => Audit.SecurityEventSeverity.Medium,
                        _ => Audit.SecurityEventSeverity.Low
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

    private async Task HandleInjectionAttempt(HttpContext context, InjectionDetectionResult injectionResult)
    {
        context.Response.StatusCode = 400; // Bad Request
        context.Response.ContentType = "application/json";

        var errorResponse = new
        {
            error = "input_validation_failed",
            message = "Potentially malicious input detected",
            details = _options.IncludeInjectionDetailsInResponse ? new
            {
                injectionType = injectionResult.InjectionType?.ToString(),
                riskLevel = injectionResult.RiskLevel.ToString(),
                detectedPatterns = injectionResult.DetectedPatterns
            } : null,
            timestamp = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }

    private async Task HandleSanitizationError(HttpContext context, string message)
    {
        context.Response.StatusCode = 400; // Bad Request
        context.Response.ContentType = "application/json";

        var errorResponse = new
        {
            error = "input_sanitization_failed",
            message = message,
            timestamp = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

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

    private static string GetClientIpAddress(HttpContext context)
    {
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',')[0].Trim();
        }

        var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp))
        {
            return realIp;
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
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

    private static bool ContainsDangerousPattern(string value)
    {
        // Only check for actual dangerous patterns, not JSON/email syntax
        var dangerousPatterns = new[]
        {
            "<script", "</script>", "javascript:", "onerror=", "onclick=",
            "DROP TABLE", "DELETE FROM", "INSERT INTO", "UPDATE SET",
            "../", "..\\", "cmd.exe", "/bin/bash", "powershell.exe"
        };

        var valueLower = value.ToLowerInvariant();
        return dangerousPatterns.Any(pattern => valueLower.Contains(pattern.ToLowerInvariant()));
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