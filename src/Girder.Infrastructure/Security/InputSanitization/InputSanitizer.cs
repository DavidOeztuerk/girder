using Microsoft.Extensions.Logging;
using System.Net.Mail;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;

namespace Infrastructure.Security.InputSanitization;

/// <summary>
/// Comprehensive input sanitizer for preventing injection attacks
/// </summary>
public class InputSanitizer : IInputSanitizer
{
    private readonly ILogger<InputSanitizer> _logger;
    private readonly Dictionary<InjectionType, List<Regex>> _injectionPatterns;
    private readonly HtmlEncoder _htmlEncoder;

    // Common dangerous patterns
    // Note: Removed underscore (_), at-sign (@), and percent (%) from pattern as they are common in:
    // - Enum values (e.g., in_person, both_ways)
    // - Email addresses (@ symbol)
    // - URL parameters and identifiers
    // - Percent-encoding in URLs
    // The SQL keywords and actual dangerous patterns still provide protection.
    private static readonly Regex SqlInjectionPattern = new(
        @"(\b(ALTER|CREATE|DELETE|DROP|EXEC(UTE)?|INSERT|MERGE|SELECT|UPDATE|UNION|SCRIPT|JAVASCRIPT|VBSCRIPT)\b)|(\-\-|\/\*|\*\/|;|\||`|'|""|\[|\]|\{|\})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex XssPattern = new(
        @"<\s*\/?(?:script|object|embed|form|input|iframe|meta|link|style|img|svg|math|details|template)\b[^>]*>|on\w+\s*=|javascript:|data:.*base64|eval\s*\(|expression\s*\(",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PathTraversalPattern = new(
        @"(\.\.[/\\])|(%2e%2e[/\\])|(%252e%252e[/\\])|(\.\.%5c)|(\.\.%2f)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CommandInjectionPattern = new(
        @"[\|&;`'\""$(){}[\]<>]|(\b(cmd|bash|sh|powershell|exec|system|eval|call)\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> DangerousHtmlTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "object", "embed", "form", "input", "iframe", "meta", "link", "style",
        "svg", "math", "details", "template", "audio", "video", "canvas", "base"
    };

    private static readonly HashSet<string> AllowedBasicTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "br", "strong", "em", "b", "i", "u", "span", "div", "h1", "h2", "h3", "h4", "h5", "h6"
    };

    private static readonly HashSet<string> AllowedStandardTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "br", "strong", "em", "b", "i", "u", "span", "div", "h1", "h2", "h3", "h4", "h5", "h6",
        "ul", "ol", "li", "blockquote", "pre", "code", "a", "img", "table", "tr", "td", "th", "thead", "tbody"
    };

    public InputSanitizer(ILogger<InputSanitizer> logger)
    {
        _logger = logger;
        _htmlEncoder = HtmlEncoder.Default;
        _injectionPatterns = InitializeInjectionPatterns();
    }

    public string SanitizeHtml(string input, HtmlSanitizationLevel level = HtmlSanitizationLevel.Strict)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        try
        {
            return level switch
            {
                HtmlSanitizationLevel.Strip => StripAllHtml(input),
                HtmlSanitizationLevel.Basic => SanitizeWithAllowedTags(input, AllowedBasicTags),
                HtmlSanitizationLevel.Standard => SanitizeWithAllowedTags(input, AllowedStandardTags),
                HtmlSanitizationLevel.Relaxed => SanitizeRelaxed(input),
                HtmlSanitizationLevel.Strict => SanitizeStrict(input),
                _ => SanitizeStrict(input)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sanitizing HTML input");
            return StripAllHtml(input);
        }
    }

    public string SanitizeSql(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        try
        {
            // Remove dangerous SQL characters and keywords
            var sanitized = input
                .Replace("'", "''") // Escape single quotes
                .Replace("\"", "&quot;") // Replace double quotes
                .Replace(";", "") // Remove semicolons
                .Replace("--", "") // Remove SQL comments
                .Replace("/*", "") // Remove block comment start
                .Replace("*/", "") // Remove block comment end
                .Replace("xp_", "") // Remove extended procedures
                .Replace("sp_", ""); // Remove stored procedures

            // Remove SQL injection patterns
            sanitized = SqlInjectionPattern.Replace(sanitized, "");

            return sanitized.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sanitizing SQL input");
            return string.Empty;
        }
    }

    public string SanitizeJavaScript(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        try
        {
            // Remove JavaScript code patterns
            var sanitized = input;

            // Remove script tags
            sanitized = Regex.Replace(sanitized, @"<\s*script\b[^<]*(?:(?!<\/script>)<[^<]*)*<\/script>", "", RegexOptions.IgnoreCase);

            // Remove event handlers
            sanitized = Regex.Replace(sanitized, @"\bon\w+\s*=\s*['""][^'""]*['""]", "", RegexOptions.IgnoreCase);

            // Remove javascript: URLs
            sanitized = Regex.Replace(sanitized, @"javascript\s*:", "", RegexOptions.IgnoreCase);

            // Remove eval and similar functions
            sanitized = Regex.Replace(sanitized, @"\b(eval|setTimeout|setInterval|Function|execScript)\s*\(", "", RegexOptions.IgnoreCase);

            return sanitized;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sanitizing JavaScript input");
            return HttpUtility.HtmlEncode(input);
        }
    }

    public string SanitizeFilePath(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        try
        {
            // Remove path traversal patterns
            var sanitized = PathTraversalPattern.Replace(input, "");

            // Remove dangerous characters
            var invalidChars = Path.GetInvalidFileNameChars().Concat(Path.GetInvalidPathChars()).ToArray();
            foreach (var invalidChar in invalidChars)
            {
                sanitized = sanitized.Replace(invalidChar.ToString(), "");
            }

            // Remove leading/trailing dots and spaces
            sanitized = sanitized.Trim('.', ' ');

            // Limit length
            if (sanitized.Length > 255)
            {
                sanitized = sanitized[..255];
            }

            return sanitized;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sanitizing file path");
            return Regex.Replace(input, @"[^\w\-.]", "");
        }
    }

    public string SanitizeUrl(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        try
        {
            // Check if it's a valid URI
            if (!Uri.TryCreate(input, UriKind.Absolute, out var uri))
            {
                return string.Empty;
            }

            // Only allow safe schemes
            var allowedSchemes = new[] { "http", "https", "ftp", "ftps", "mailto" };
            if (!allowedSchemes.Contains(uri.Scheme.ToLowerInvariant()))
            {
                return string.Empty;
            }

            // Remove dangerous JavaScript
            var sanitized = SanitizeJavaScript(uri.ToString());

            return sanitized;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sanitizing URL");
            return string.Empty;
        }
    }

    public ValidationResult<string> SanitizeEmail(string input)
    {
        if (string.IsNullOrEmpty(input))
            return ValidationResult<string>.Failure("Email address is required");

        try
        {
            // Basic format validation
            var trimmed = input.Trim();

            if (trimmed.Length > 254) // RFC 5321 limit
                return ValidationResult<string>.Failure("Email address is too long");

            // Use MailAddress for basic validation
            var mailAddress = new MailAddress(trimmed);
            var normalized = mailAddress.Address.ToLowerInvariant();

            // Additional security checks
            if (normalized.Contains("..") || normalized.StartsWith(".") || normalized.EndsWith("."))
                return ValidationResult<string>.Failure("Invalid email format");

            // Check for suspicious patterns
            var suspiciousPatterns = new[]
            {
                @"[<>'""]", // HTML/quote characters
                @"javascript:", // JavaScript protocol
                @"\s", // Whitespace
                @"[\x00-\x1F\x7F]" // Control characters
            };

            foreach (var pattern in suspiciousPatterns)
            {
                if (Regex.IsMatch(normalized, pattern))
                    return ValidationResult<string>.Failure("Email contains invalid characters");
            }

            return ValidationResult<string>.Success(normalized);
        }
        catch (FormatException)
        {
            return ValidationResult<string>.Failure("Invalid email format");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating email address");
            return ValidationResult<string>.Failure("Email validation failed");
        }
    }

    public ValidationResult<string> SanitizePhoneNumber(string input)
    {
        if (string.IsNullOrEmpty(input))
            return ValidationResult<string>.Failure("Phone number is required");

        try
        {
            // Remove all non-digit characters except + and spaces
            var digitsOnly = Regex.Replace(input, @"[^\d\+\s\-\(\)\.]", "");

            // Remove formatting characters, keep only digits and +
            var normalized = Regex.Replace(digitsOnly, @"[\s\-\(\)\.]", "");

            // Basic validation
            if (normalized.Length < 7 || normalized.Length > 15)
                return ValidationResult<string>.Failure("Phone number length is invalid");

            // Check for valid international format
            if (normalized.StartsWith("+"))
            {
                if (normalized.Length < 8)
                    return ValidationResult<string>.Failure("International phone number is too short");
            }
            else
            {
                // Add default country code if not present
                normalized = "+1" + normalized; // US default, should be configurable
            }

            return ValidationResult<string>.Success(normalized);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sanitizing phone number");
            return ValidationResult<string>.Failure("Phone number sanitization failed");
        }
    }

    public string SanitizeText(string input, TextSanitizationOptions? options = null)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        options ??= new TextSanitizationOptions();

        try
        {
            var sanitized = input;

            // Remove control characters
            if (options.RemoveControlCharacters)
            {
                sanitized = Regex.Replace(sanitized, @"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", "");
            }

            // Handle HTML
            if (!options.AllowHtml)
            {
                sanitized = StripAllHtml(sanitized);
            }
            else
            {
                sanitized = SanitizeHtml(sanitized, options.HtmlLevel);
            }

            // Remove line breaks if specified
            if (options.RemoveLineBreaks)
            {
                sanitized = sanitized.Replace("\n", " ").Replace("\r", " ");
            }

            // Normalize whitespace
            if (options.NormalizeWhitespace)
            {
                sanitized = Regex.Replace(sanitized, @"\s+", " ").Trim();
            }

            // Remove blacklisted characters
            foreach (var blacklistedChar in options.BlacklistedCharacters)
            {
                sanitized = sanitized.Replace(blacklistedChar.ToString(), "");
            }

            // Remove blacklisted patterns
            foreach (var pattern in options.BlacklistedPatterns)
            {
                try
                {
                    sanitized = Regex.Replace(sanitized, pattern, "", RegexOptions.IgnoreCase);
                }
                catch (ArgumentException ex)
                {
                    _logger.LogWarning(ex, "Invalid regex pattern: {Pattern}", pattern);
                }
            }

            // Truncate if necessary
            if (options.MaxLength.HasValue && sanitized.Length > options.MaxLength.Value)
            {
                sanitized = sanitized[..options.MaxLength.Value];
            }

            return sanitized;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sanitizing text input");
            return _htmlEncoder.Encode(input);
        }
    }

    public InjectionDetectionResult DetectInjectionAttempt(string input)
    {
        if (string.IsNullOrEmpty(input))
            return new InjectionDetectionResult { InjectionDetected = false };

        var result = new InjectionDetectionResult();
        var detectedPatterns = new List<string>();
        var maxRiskLevel = RiskLevel.None;
        var totalConfidence = 0;
        var patternCount = 0;

        foreach (var kvp in _injectionPatterns)
        {
            var injectionType = kvp.Key;
            var patterns = kvp.Value;

            foreach (var pattern in patterns)
            {
                try
                {
                    var matches = pattern.Matches(input);
                    if (matches.Count > 0)
                    {
                        result.InjectionDetected = true;
                        result.InjectionType = injectionType;

                        foreach (Match match in matches)
                        {
                            detectedPatterns.Add($"{injectionType}: {match.Value}");
                        }

                        var riskLevel = CalculateRiskLevel(injectionType, matches.Count);
                        if (riskLevel > maxRiskLevel)
                        {
                            maxRiskLevel = riskLevel;
                        }

                        totalConfidence += Math.Min(95, matches.Count * 20);
                        patternCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error executing injection detection pattern");
                }
            }
        }

        result.DetectedPatterns = detectedPatterns;
        result.RiskLevel = maxRiskLevel;
        result.ConfidenceScore = patternCount > 0 ? Math.Min(100, totalConfidence / patternCount) : 0;

        return result;
    }

    public T SanitizeObject<T>(T obj, SanitizationOptions? options = null) where T : class
    {
        if (obj == null)
            return obj!;

        options ??= new SanitizationOptions();

        try
        {
            return SanitizeObjectRecursive(obj, options, 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sanitizing object");
            return obj;
        }
    }

    public ValidationResult ValidateInput(string input, InputValidationRules rules)
    {
        var result = new ValidationResult { IsValid = true };

        if (string.IsNullOrEmpty(input))
        {
            if (rules.MinLength > 0)
            {
                result.IsValid = false;
                result.Errors.Add("Input is required");
            }
            return result;
        }

        // Length validation
        if (rules.MinLength.HasValue && input.Length < rules.MinLength.Value)
        {
            result.IsValid = false;
            result.Errors.Add($"Input must be at least {rules.MinLength.Value} characters long");
        }

        if (rules.MaxLength.HasValue && input.Length > rules.MaxLength.Value)
        {
            result.IsValid = false;
            result.Errors.Add($"Input must not exceed {rules.MaxLength.Value} characters");
        }

        // Pattern validation
        if (!string.IsNullOrEmpty(rules.RequiredPattern))
        {
            try
            {
                if (!Regex.IsMatch(input, rules.RequiredPattern))
                {
                    result.IsValid = false;
                    result.Errors.Add("Input does not match required format");
                }
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid required pattern: {Pattern}", rules.RequiredPattern);
            }
        }

        // Forbidden patterns
        foreach (var forbiddenPattern in rules.ForbiddenPatterns)
        {
            try
            {
                if (Regex.IsMatch(input, forbiddenPattern, RegexOptions.IgnoreCase))
                {
                    result.IsValid = false;
                    result.Errors.Add("Input contains forbidden content");
                    break;
                }
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid forbidden pattern: {Pattern}", forbiddenPattern);
            }
        }

        // Character validation
        if (!string.IsNullOrEmpty(rules.AllowedCharacters))
        {
            var allowedSet = new HashSet<char>(rules.AllowedCharacters);
            if (input.Any(c => !allowedSet.Contains(c)))
            {
                result.IsValid = false;
                result.Errors.Add("Input contains invalid characters");
            }
        }

        if (!string.IsNullOrEmpty(rules.ForbiddenCharacters))
        {
            var forbiddenSet = new HashSet<char>(rules.ForbiddenCharacters);
            if (input.Any(c => forbiddenSet.Contains(c)))
            {
                result.IsValid = false;
                result.Errors.Add("Input contains forbidden characters");
            }
        }

        // Format validation
        if (rules.RequiredFormat.HasValue)
        {
            var formatResult = ValidateFormat(input, rules.RequiredFormat.Value);
            if (!formatResult.IsValid)
            {
                result.IsValid = false;
                result.Errors.AddRange(formatResult.Errors);
            }
        }

        // Custom validation
        if (rules.CustomValidator != null)
        {
            try
            {
                var customResult = rules.CustomValidator(input);
                if (!customResult.IsValid)
                {
                    result.IsValid = false;
                    result.Errors.AddRange(customResult.Errors);
                }
                result.Warnings.AddRange(customResult.Warnings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in custom validator");
                result.IsValid = false;
                result.Errors.Add("Validation failed");
            }
        }

        return result;
    }

    #region Private Methods

    private Dictionary<InjectionType, List<Regex>> InitializeInjectionPatterns()
    {
        return new Dictionary<InjectionType, List<Regex>>
        {
            [InjectionType.SqlInjection] = new List<Regex>
            {
                SqlInjectionPattern,
                new Regex(@"\b(union|select|insert|update|delete|drop|create|alter|exec|execute)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new Regex(@"[';].*(\-\-|\/\*)", RegexOptions.IgnoreCase | RegexOptions.Compiled)
            },
            [InjectionType.XssInjection] = new List<Regex>
            {
                XssPattern,
                new Regex(@"<\s*script[^>]*>.*?<\s*\/\s*script\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new Regex(@"on\w+\s*=\s*['""][^'""]*['""]", RegexOptions.IgnoreCase | RegexOptions.Compiled)
            },
            [InjectionType.CommandInjection] = new List<Regex>
            {
                CommandInjectionPattern,
                new Regex(@"[\|&;`$(){}[\]<>]", RegexOptions.Compiled),
                new Regex(@"\b(cmd|bash|sh|powershell|exec|system|eval)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)
            },
            [InjectionType.PathTraversal] = new List<Regex>
            {
                PathTraversalPattern,
                new Regex(@"(\.\.\/|\.\.\\)", RegexOptions.Compiled)
            }
        };
    }

    private static string StripAllHtml(string input)
    {
        return Regex.Replace(input, @"<[^>]*>", "");
    }

    private string SanitizeWithAllowedTags(string input, HashSet<string> allowedTags)
    {
        // Remove all tags except allowed ones
        return Regex.Replace(input, @"<(/?)(\w+)[^>]*>", match =>
        {
            var isClosing = !string.IsNullOrEmpty(match.Groups[1].Value);
            var tagName = match.Groups[2].Value.ToLowerInvariant();

            if (allowedTags.Contains(tagName))
            {
                return isClosing ? $"</{tagName}>" : $"<{tagName}>";
            }

            return "";
        });
    }

    private string SanitizeRelaxed(string input)
    {
        // Remove only the most dangerous elements
        var sanitized = input;

        foreach (var dangerousTag in DangerousHtmlTags)
        {
            sanitized = Regex.Replace(sanitized, $@"<\s*{dangerousTag}\b[^>]*>.*?<\s*\/\s*{dangerousTag}\s*>", "", RegexOptions.IgnoreCase);
            sanitized = Regex.Replace(sanitized, $@"<\s*{dangerousTag}\b[^>]*\/?>", "", RegexOptions.IgnoreCase);
        }

        // Remove dangerous attributes
        sanitized = Regex.Replace(sanitized, @"\s*on\w+\s*=\s*['""][^'""]*['""]", "", RegexOptions.IgnoreCase);
        sanitized = Regex.Replace(sanitized, @"\s*style\s*=\s*['""][^'""]*['""]", "", RegexOptions.IgnoreCase);

        return sanitized;
    }

    private string SanitizeStrict(string input)
    {
        return _htmlEncoder.Encode(input);
    }

    private static RiskLevel CalculateRiskLevel(InjectionType injectionType, int matchCount)
    {
        var baseRisk = injectionType switch
        {
            InjectionType.SqlInjection => RiskLevel.High,
            InjectionType.XssInjection => RiskLevel.High,
            InjectionType.CommandInjection => RiskLevel.Critical,
            InjectionType.PathTraversal => RiskLevel.Medium,
            InjectionType.ScriptInjection => RiskLevel.High,
            _ => RiskLevel.Low
        };

        // Increase risk based on match count
        if (matchCount > 3)
            return RiskLevel.Critical;
        if (matchCount > 1 && baseRisk >= RiskLevel.Medium)
            return RiskLevel.High;

        return baseRisk;
    }

    private T SanitizeObjectRecursive<T>(T obj, SanitizationOptions options, int depth) where T : class
    {
        if (obj == null || depth >= options.MaxRecursionDepth)
            return obj!;

        var type = obj.GetType();

        // Handle strings
        if (type == typeof(string))
        {
            var stringValue = obj as string;
            var sanitized = SanitizeText(stringValue ?? "", new TextSanitizationOptions());
            return (T)(object)sanitized;
        }

        // Handle collections
        if (options.SanitizeCollections && obj is System.Collections.IEnumerable enumerable && type != typeof(string))
        {
            // This is a simplified implementation - a full implementation would handle specific collection types
            return obj;
        }

        // Handle complex objects
        if (options.SanitizeNestedObjects)
        {
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite && !options.SkipProperties.Contains(p.Name));

            foreach (var property in properties)
            {
                try
                {
                    var value = property.GetValue(obj);
                    if (value != null)
                    {
                        if (property.PropertyType == typeof(string))
                        {
                            var stringValue = value as string;
                            var sanitizationOptions = options.PropertyRules.GetValueOrDefault(
                                property.Name,
                                new TextSanitizationOptions());

                            var sanitized = SanitizeText(stringValue ?? "", sanitizationOptions);
                            property.SetValue(obj, sanitized);
                        }
                        else if (property.PropertyType.IsClass && property.PropertyType != typeof(string))
                        {
                            var sanitizedValue = SanitizeObjectRecursive(value, options, depth + 1);
                            property.SetValue(obj, sanitizedValue);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error sanitizing property {Property}", property.Name);
                }
            }
        }

        return obj;
    }

    private ValidationResult ValidateFormat(string input, InputFormat format)
    {
        return format switch
        {
            InputFormat.Email => SanitizeEmail(input),
            InputFormat.PhoneNumber => SanitizePhoneNumber(input),
            InputFormat.Url => ValidateUrl(input),
            InputFormat.IpAddress => ValidateIpAddress(input),
            InputFormat.AlphaNumeric => ValidateAlphaNumeric(input),
            InputFormat.Numeric => ValidateNumeric(input),
            InputFormat.Alpha => ValidateAlpha(input),
            InputFormat.Base64 => ValidateBase64(input),
            InputFormat.Json => ValidateJson(input),
            _ => ValidationResult.Success()
        };
    }

    private ValidationResult ValidateUrl(string input)
    {
        try
        {
            var sanitized = SanitizeUrl(input);
            return string.IsNullOrEmpty(sanitized) ?
                ValidationResult.Failure("Invalid URL format") :
                ValidationResult.Success();
        }
        catch
        {
            return ValidationResult.Failure("Invalid URL format");
        }
    }

    private static ValidationResult ValidateIpAddress(string input)
    {
        return System.Net.IPAddress.TryParse(input, out _) ?
            ValidationResult.Success() :
            ValidationResult.Failure("Invalid IP address format");
    }

    private static ValidationResult ValidateAlphaNumeric(string input)
    {
        return Regex.IsMatch(input, @"^[a-zA-Z0-9]+$") ?
            ValidationResult.Success() :
            ValidationResult.Failure("Input must contain only letters and numbers");
    }

    private static ValidationResult ValidateNumeric(string input)
    {
        return Regex.IsMatch(input, @"^[0-9]+$") ?
            ValidationResult.Success() :
            ValidationResult.Failure("Input must contain only numbers");
    }

    private static ValidationResult ValidateAlpha(string input)
    {
        return Regex.IsMatch(input, @"^[a-zA-Z]+$") ?
            ValidationResult.Success() :
            ValidationResult.Failure("Input must contain only letters");
    }

    private static ValidationResult ValidateBase64(string input)
    {
        try
        {
            Convert.FromBase64String(input);
            return ValidationResult.Success();
        }
        catch
        {
            return ValidationResult.Failure("Invalid Base64 format");
        }
    }

    private static ValidationResult ValidateJson(string input)
    {
        try
        {
            JsonDocument.Parse(input);
            return ValidationResult.Success();
        }
        catch
        {
            return ValidationResult.Failure("Invalid JSON format");
        }
    }

    #endregion
}