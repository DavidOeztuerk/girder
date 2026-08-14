namespace Infrastructure.Security.InputSanitization;

/// <summary>
/// Interface for input sanitization and validation
/// </summary>
public interface IInputSanitizer
{
    /// <summary>
    /// Sanitize HTML input to prevent XSS attacks
    /// </summary>
    string SanitizeHtml(string input, HtmlSanitizationLevel level = HtmlSanitizationLevel.Strict);

    /// <summary>
    /// Sanitize SQL input to prevent SQL injection
    /// </summary>
    string SanitizeSql(string input);

    /// <summary>
    /// Sanitize JavaScript to prevent script injection
    /// </summary>
    string SanitizeJavaScript(string input);

    /// <summary>
    /// Sanitize file path to prevent directory traversal
    /// </summary>
    string SanitizeFilePath(string input);

    /// <summary>
    /// Sanitize URL to prevent malicious redirects
    /// </summary>
    string SanitizeUrl(string input);

    /// <summary>
    /// Validate and sanitize email address
    /// </summary>
    ValidationResult<string> SanitizeEmail(string input);

    /// <summary>
    /// Validate and sanitize phone number
    /// </summary>
    ValidationResult<string> SanitizePhoneNumber(string input);

    /// <summary>
    /// Sanitize general text input
    /// </summary>
    string SanitizeText(string input, TextSanitizationOptions? options = null);

    /// <summary>
    /// Detect potential injection attacks
    /// </summary>
    InjectionDetectionResult DetectInjectionAttempt(string input);

    /// <summary>
    /// Sanitize object properties recursively
    /// </summary>
    T SanitizeObject<T>(T obj, SanitizationOptions? options = null) where T : class;

    /// <summary>
    /// Validate input against specific rules
    /// </summary>
    ValidationResult ValidateInput(string input, InputValidationRules rules);
}

/// <summary>
/// HTML sanitization levels
/// </summary>
public enum HtmlSanitizationLevel
{
    /// <summary>
    /// Strip all HTML tags
    /// </summary>
    Strip,

    /// <summary>
    /// Allow basic formatting tags only
    /// </summary>
    Basic,

    /// <summary>
    /// Allow standard content tags
    /// </summary>
    Standard,

    /// <summary>
    /// Allow most HTML but remove dangerous elements
    /// </summary>
    Relaxed,

    /// <summary>
    /// Most restrictive - remove all potentially dangerous content
    /// </summary>
    Strict
}

/// <summary>
/// Text sanitization options
/// </summary>
public class TextSanitizationOptions
{
    /// <summary>
    /// Maximum allowed length
    /// </summary>
    public int? MaxLength { get; set; }

    /// <summary>
    /// Allow HTML content
    /// </summary>
    public bool AllowHtml { get; set; } = false;

    /// <summary>
    /// HTML sanitization level if HTML is allowed
    /// </summary>
    public HtmlSanitizationLevel HtmlLevel { get; set; } = HtmlSanitizationLevel.Strict;

    /// <summary>
    /// Remove line breaks
    /// </summary>
    public bool RemoveLineBreaks { get; set; } = false;

    /// <summary>
    /// Normalize whitespace
    /// </summary>
    public bool NormalizeWhitespace { get; set; } = true;

    /// <summary>
    /// Remove control characters
    /// </summary>
    public bool RemoveControlCharacters { get; set; } = true;

    /// <summary>
    /// Custom character blacklist
    /// </summary>
    public HashSet<char> BlacklistedCharacters { get; set; } = new();

    /// <summary>
    /// Custom regex patterns to remove
    /// </summary>
    public List<string> BlacklistedPatterns { get; set; } = new();
}

/// <summary>
/// Object sanitization options
/// </summary>
public class SanitizationOptions
{
    /// <summary>
    /// Properties to skip during sanitization
    /// </summary>
    public HashSet<string> SkipProperties { get; set; } = new();

    /// <summary>
    /// Property-specific sanitization rules
    /// </summary>
    public Dictionary<string, TextSanitizationOptions> PropertyRules { get; set; } = new();

    /// <summary>
    /// Maximum recursion depth
    /// </summary>
    public int MaxRecursionDepth { get; set; } = 10;

    /// <summary>
    /// Sanitize nested objects
    /// </summary>
    public bool SanitizeNestedObjects { get; set; } = true;

    /// <summary>
    /// Sanitize collection elements
    /// </summary>
    public bool SanitizeCollections { get; set; } = true;
}

/// <summary>
/// Input validation rules
/// </summary>
public class InputValidationRules
{
    /// <summary>
    /// Minimum length
    /// </summary>
    public int? MinLength { get; set; }

    /// <summary>
    /// Maximum length
    /// </summary>
    public int? MaxLength { get; set; }

    /// <summary>
    /// Required pattern (regex)
    /// </summary>
    public string? RequiredPattern { get; set; }

    /// <summary>
    /// Forbidden patterns (regex)
    /// </summary>
    public List<string> ForbiddenPatterns { get; set; } = new();

    /// <summary>
    /// Allowed characters only
    /// </summary>
    public string? AllowedCharacters { get; set; }

    /// <summary>
    /// Forbidden characters
    /// </summary>
    public string? ForbiddenCharacters { get; set; }

    /// <summary>
    /// Require specific format
    /// </summary>
    public InputFormat? RequiredFormat { get; set; }

    /// <summary>
    /// Custom validation function
    /// </summary>
    public Func<string, ValidationResult>? CustomValidator { get; set; }
}

/// <summary>
/// Input format types
/// </summary>
public enum InputFormat
{
    Email,
    PhoneNumber,
    Url,
    IpAddress,
    CreditCard,
    SocialSecurityNumber,
    PostalCode,
    AlphaNumeric,
    Numeric,
    Alpha,
    Base64,
    Json,
    Xml
}

/// <summary>
/// Validation result
/// </summary>
public class ValidationResult
{
    /// <summary>
    /// Whether validation succeeded
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Validation error messages
    /// </summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// Warnings (non-blocking)
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// Create successful result
    /// </summary>
    public static ValidationResult Success() => new() { IsValid = true };

    /// <summary>
    /// Create failed result
    /// </summary>
    public static ValidationResult Failure(string error) => new() 
    { 
        IsValid = false, 
        Errors = new List<string> { error } 
    };

    /// <summary>
    /// Create failed result with multiple errors
    /// </summary>
    public static ValidationResult Failure(IEnumerable<string> errors) => new() 
    { 
        IsValid = false, 
        Errors = errors.ToList() 
    };
}

/// <summary>
/// Validation result with sanitized value
/// </summary>
public class ValidationResult<T> : ValidationResult
{
    /// <summary>
    /// Sanitized/validated value
    /// </summary>
    public T? Value { get; set; }

    /// <summary>
    /// Create successful result with value
    /// </summary>
    public static ValidationResult<T> Success(T value) => new() 
    { 
        IsValid = true, 
        Value = value 
    };

    /// <summary>
    /// Create failed result
    /// </summary>
    public static new ValidationResult<T> Failure(string error) => new() 
    { 
        IsValid = false, 
        Errors = new List<string> { error } 
    };
}

/// <summary>
/// Injection detection result
/// </summary>
public class InjectionDetectionResult
{
    /// <summary>
    /// Whether injection attempt was detected
    /// </summary>
    public bool InjectionDetected { get; set; }

    /// <summary>
    /// Type of injection detected
    /// </summary>
    public InjectionType? InjectionType { get; set; }

    /// <summary>
    /// Risk level of the injection
    /// </summary>
    public RiskLevel RiskLevel { get; set; }

    /// <summary>
    /// Detected patterns
    /// </summary>
    public List<string> DetectedPatterns { get; set; } = new();

    /// <summary>
    /// Confidence score (0-100)
    /// </summary>
    public int ConfidenceScore { get; set; }

    /// <summary>
    /// Additional details
    /// </summary>
    public Dictionary<string, object?> Details { get; set; } = new();
}

/// <summary>
/// Types of injection attacks
/// </summary>
public enum InjectionType
{
    SqlInjection,
    XssInjection,
    ScriptInjection,
    CommandInjection,
    LdapInjection,
    XPathInjection,
    CssInjection,
    TemplateInjection,
    HeaderInjection,
    PathTraversal
}

/// <summary>
/// Risk levels
/// </summary>
public enum RiskLevel
{
    None,
    Low,
    Medium,
    High,
    Critical
}