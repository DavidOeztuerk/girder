using Microsoft.Extensions.Logging;
using System.Net.Mail;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;

namespace Girder.Infrastructure.Security.InputSanitization;

/// <summary>
/// Comprehensive input sanitizer for preventing injection attacks
/// </summary>
public class InputSanitizer : IInputSanitizer
{
    private readonly ILogger<InputSanitizer> _logger;
    private readonly Dictionary<InjectionType, List<Regex>> _injectionPatterns;
    private readonly HtmlEncoder _htmlEncoder;

    // What follows detects INJECTION SYNTAX, never words.
    //
    // It used to match the bare SQL keyword at a word boundary and, worse, every
    // single punctuation mark on its own — semicolon, pipe, backtick, apostrophe,
    // quote, bracket, brace. Measured against that: `Union-Investment` (a German
    // fund manager) was refused, so were `Select-Kundenberater` and
    // `Drop-In-Zentrum`; a hyphen is a word boundary and an underscore is not, so
    // `delete-account` was an attack and `delete_account` was not. `O'Brien` was
    // an attack. And because the Referer was treated as input, every request from
    // a page called /delete-account was refused — including the one asking who is
    // signed in, so the page told a signed-in person to sign in.
    //
    // A keyword carries no information about intent. Syntax does: a quote
    // followed by an operator, a comment introducer after a quote, a statement
    // terminator followed by a keyword, a tautology, a routine that exists to
    // reach outside the database.

    /// <summary>A quote that ends a literal and starts something else.</summary>
    private static readonly Regex SqlQuoteBreakout = new(
        @"['""]\s*(?:;|--|#|/\*|\|\||\+|\b(?:OR|AND|UNION|SELECT|EXEC)\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>A condition that is true whatever the data says.</summary>
    private static readonly Regex SqlTautology = new(
        @"\b(?:OR|AND)\s+['""]?[\w\s]{1,20}['""]?\s*(?:=|<>|!=|\bLIKE\b)\s*['""]?[\w\s]{1,20}['""]?\s*(?:--|#|;|/\*|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>A second statement hung off the end of the first.</summary>
    private static readonly Regex SqlStackedStatement = new(
        @";\s*(?:ALTER|CREATE|DELETE|DROP|EXEC(?:UTE)?|GRANT|INSERT|MERGE|REVOKE|SELECT|TRUNCATE|UPDATE)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The classic read-anything shape. Both words, in this order.</summary>
    private static readonly Regex SqlUnionSelect = new(
        @"\bUNION\b(?:\s+ALL)?\s+\bSELECT\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Routines that exist to reach past the query.</summary>
    private static readonly Regex SqlDangerousRoutine = new(
        @"\b(?:xp_cmdshell|sp_executesql|sp_addlogin|sp_password|load_file\s*\(|benchmark\s*\(|pg_sleep\s*\(|waitfor\s+delay|into\s+(?:out|dump)file)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Markup and script, which is syntax and not vocabulary.
    /// </summary>
    /// <remarks>
    /// The event-handler half names the handlers instead of matching
    /// <c>on\w+\s*=</c>, which also matched <c>?onboarding=true</c>.
    /// </remarks>
    private static readonly Regex XssPattern = new(
        @"<\s*/?(?:script|object|embed|form|input|iframe|meta|link|style|img|svg|math|details|template)\b[^>]*>"
        + @"|\bon(?:abort|blur|change|click|contextmenu|copy|cut|drag|drop|error|focus|input|keydown|keypress|keyup|load|mouseover|mouseout|paste|pointerdown|scroll|submit|toggle|touchstart|wheel)\s*="
        + @"|javascript\s*:|data:[^,]*;base64|\beval\s*\(|\bexpression\s*\(",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PathTraversalPattern = new(
        @"(\.\.[/\\])|(%2e%2e[/\\])|(%252e%252e[/\\])|(\.\.%5c)|(\.\.%2f)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// A shell metacharacter <em>followed by a command</em> — never the character
    /// on its own.
    /// </summary>
    /// <remarks>
    /// The old expression matched every ampersand, parenthesis, quote and angle
    /// bracket, which is most prose.
    /// </remarks>
    private static readonly Regex CommandInjectionPattern = new(
        @"[;&|]{1,2}\s*(?:/\w+/)*\b(?:bash|cat|chmod|chown|cmd|curl|env|export|id|kill|ls|mv|nc|ncat|netcat|nslookup|perl|ping|powershell|ps|pwsh|python\d?|rm|ruby|sh|uname|wget|whoami|zsh)\b"
        + @"|`[^`]*\b(?:bash|cat|curl|id|ls|rm|sh|uname|wget|whoami)\b[^`]*`"
        + @"|\$\(\s*\w"
        + @"|/dev/tcp/"
        + @"|\bnc\s+-e\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// A value that closes one LDAP filter and opens another.
    /// </summary>
    /// <remarks>
    /// <c>)(</c> is the hallmark of every filter-injection payload —
    /// <c>test)(objectClass=*</c>, <c>admin)(&amp;)</c>,
    /// <c>*)(uid=*))(|(uid=*</c> — and appears in ordinary text essentially
    /// never.
    /// <para>
    /// This class had a name in <see cref="InjectionType"/> and no expression
    /// behind it. It was caught anyway, by the rule that matched every single
    /// parenthesis — the same rule that refused <c>Meier &amp; Söhne (Hamburg)</c>.
    /// The test for it therefore passed for a reason that had nothing to do with
    /// LDAP.
    /// </para>
    /// </remarks>
    private static readonly Regex LdapFilterBreakout = new(
        @"\)\s*\(|\(\s*[|&!]\s*\(",
        RegexOptions.Compiled);

    /// <summary>
    /// What <see cref="SanitizeSql"/> strips. Not a detector.
    /// </summary>
    /// <remarks>
    /// Deliberately blunt, and deliberately separate from everything above:
    /// stripping a character from a value someone asked to be escaped costs that
    /// value, while refusing a request over the same character costs the whole
    /// request. Sharing one expression between the two is what made a bare
    /// apostrophe an attack.
    /// </remarks>
    private static readonly Regex SqlStrippingPattern = new(
        @"(\b(ALTER|CREATE|DELETE|DROP|EXEC(UTE)?|INSERT|MERGE|SELECT|UPDATE|UNION)\b)|(\-\-|\/\*|\*\/|;|\||`)",
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
            sanitized = SqlStrippingPattern.Replace(sanitized, "");

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
            [InjectionType.SqlInjection] =
            [
                SqlQuoteBreakout,
                SqlTautology,
                SqlStackedStatement,
                SqlUnionSelect,
                SqlDangerousRoutine
            ],
            [InjectionType.XssInjection] =
            [
                XssPattern,
                new Regex(@"<\s*script[^>]*>.*?<\s*\/\s*script\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled)
            ],
            [InjectionType.CommandInjection] =
            [
                CommandInjectionPattern
            ],
            [InjectionType.LdapInjection] =
            [
                LdapFilterBreakout
            ],
            [InjectionType.PathTraversal] =
            [
                PathTraversalPattern
            ]
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