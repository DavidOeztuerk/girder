using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Security.InputSanitization;

/// <summary>
/// Extension methods for input sanitization services
/// </summary>
public static class InputSanitizationExtensions
{
    /// <summary>
    /// Add input sanitization services
    /// </summary>
    public static IServiceCollection AddInputSanitization(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register the input sanitizer
        services.AddSingleton<IInputSanitizer, InputSanitizer>();

        // Register validation services
        services.AddSingleton<IInputValidator, InputValidator>();
        services.AddSingleton<IInjectionDetector, InjectionDetector>();

        // InputSanitizationMiddleware should not be registered as a service
        // It will be used directly via UseMiddleware<InputSanitizationMiddleware>()

        // Configure options
        services.Configure<InputSanitizationOptions>(configuration.GetSection("InputSanitization"));

        return services;
    }

    /// <summary>
    /// Add input sanitization middleware
    /// </summary>
    public static IServiceCollection AddInputSanitizationMiddleware(this IServiceCollection services)
    {
        // Middleware should not be registered as a service
        return services;
    }

    /// <summary>
    /// Add custom input validators
    /// </summary>
    public static IServiceCollection AddCustomInputValidators(
        this IServiceCollection services,
        Action<InputValidatorBuilder> configure)
    {
        var builder = new InputValidatorBuilder(services);
        configure(builder);
        return services;
    }
}

/// <summary>
/// Builder for configuring custom input validators
/// </summary>
public class InputValidatorBuilder
{
    private readonly IServiceCollection _services;

    public InputValidatorBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>
    /// Add a custom validator for a specific input type
    /// </summary>
    public InputValidatorBuilder AddValidator<T>(string inputType, Func<string, ValidationResult<T>> validator)
    {
        _services.AddSingleton<ICustomInputValidator>(new CustomInputValidator<T>(inputType, validator));
        return this;
    }

    /// <summary>
    /// Add a custom sanitizer for a specific field
    /// </summary>
    public InputValidatorBuilder AddFieldSanitizer(string fieldName, Func<string, string> sanitizer)
    {
        _services.AddSingleton<ICustomFieldSanitizer>(new CustomFieldSanitizer(fieldName, sanitizer));
        return this;
    }

    /// <summary>
    /// Add custom injection detection patterns
    /// </summary>
    public InputValidatorBuilder AddInjectionPatterns(InjectionType type, params string[] patterns)
    {
        _services.AddSingleton<ICustomInjectionPatterns>(new CustomInjectionPatterns(type, patterns));
        return this;
    }
}

/// <summary>
/// Interface for input validation
/// </summary>
public interface IInputValidator
{
    /// <summary>
    /// Validate input using built-in rules
    /// </summary>
    Task<ValidationResult> ValidateAsync(string input, InputValidationRules rules);

    /// <summary>
    /// Validate input using custom validators
    /// </summary>
    Task<ValidationResult<T>> ValidateAsync<T>(string input, string inputType);

    /// <summary>
    /// Get validation rules for a specific field
    /// </summary>
    InputValidationRules GetFieldValidationRules(string fieldName);
}

/// <summary>
/// Interface for injection detection
/// </summary>
public interface IInjectionDetector
{
    /// <summary>
    /// Detect injection attempts
    /// </summary>
    InjectionDetectionResult DetectInjection(string input);

    /// <summary>
    /// Add custom injection patterns
    /// </summary>
    void AddCustomPatterns(InjectionType type, params string[] patterns);

    /// <summary>
    /// Get all detection patterns
    /// </summary>
    Dictionary<InjectionType, List<string>> GetAllPatterns();
}

/// <summary>
/// Input validation implementation
/// </summary>
public class InputValidator : IInputValidator
{
    private readonly IInputSanitizer _sanitizer;
    private readonly IEnumerable<ICustomInputValidator> _customValidators;
    private readonly Dictionary<string, InputValidationRules> _fieldRules;

    public InputValidator(
        IInputSanitizer sanitizer,
        IEnumerable<ICustomInputValidator> customValidators)
    {
        _sanitizer = sanitizer;
        _customValidators = customValidators;
        _fieldRules = InitializeDefaultFieldRules();
    }

    public Task<ValidationResult> ValidateAsync(string input, InputValidationRules rules)
    {
        var result = _sanitizer.ValidateInput(input, rules);
        return Task.FromResult(result);
    }

    public Task<ValidationResult<T>> ValidateAsync<T>(string input, string inputType)
    {
        var validator = _customValidators.FirstOrDefault(v => v.InputType == inputType);
        if (validator is ICustomInputValidator<T> typedValidator)
        {
            var result = typedValidator.Validate(input);
            return Task.FromResult(result);
        }

        return Task.FromResult(ValidationResult<T>.Failure("No validator found for input type"));
    }

    public InputValidationRules GetFieldValidationRules(string fieldName)
    {
        var fieldLower = fieldName.ToLowerInvariant();
        return _fieldRules.GetValueOrDefault(fieldLower, new InputValidationRules());
    }

    private static Dictionary<string, InputValidationRules> InitializeDefaultFieldRules()
    {
        return new Dictionary<string, InputValidationRules>
        {
            ["email"] = new InputValidationRules
            {
                RequiredFormat = InputFormat.Email,
                MaxLength = 254,
                ForbiddenCharacters = "<>\"'"
            },
            ["phone"] = new InputValidationRules
            {
                RequiredFormat = InputFormat.PhoneNumber,
                MaxLength = 20,
                RequiredPattern = @"^[\+]?[\d\s\-\(\)\.]+$"
            },
            ["url"] = new InputValidationRules
            {
                RequiredFormat = InputFormat.Url,
                MaxLength = 2048
            },
            ["password"] = new InputValidationRules
            {
                MinLength = 8,
                MaxLength = 128,
                RequiredPattern = @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]",
                ForbiddenPatterns = new List<string> { @"<.*>", @"javascript:", @"<script" }
            },
            ["username"] = new InputValidationRules
            {
                MinLength = 3,
                MaxLength = 50,
                RequiredPattern = @"^[a-zA-Z0-9_\-\.]+$",
                ForbiddenPatterns = new List<string> { @"admin", @"root", @"system" }
            },
            ["name"] = new InputValidationRules
            {
                MinLength = 1,
                MaxLength = 100,
                RequiredPattern = @"^[a-zA-Z\s\-\.\']+$",
                ForbiddenCharacters = "<>\"&"
            },
            ["description"] = new InputValidationRules
            {
                MaxLength = 5000,
                ForbiddenPatterns = new List<string>
                {
                    @"<script.*?>.*?</script>",
                    @"javascript:",
                    @"on\w+\s*="
                }
            }
        };
    }
}

/// <summary>
/// Injection detection implementation
/// </summary>
public class InjectionDetector : IInjectionDetector
{
    private readonly Dictionary<InjectionType, List<string>> _patterns;
    private readonly IEnumerable<ICustomInjectionPatterns> _customPatterns;

    public InjectionDetector(IEnumerable<ICustomInjectionPatterns> customPatterns)
    {
        _customPatterns = customPatterns;
        _patterns = InitializeDefaultPatterns();
        
        // Add custom patterns
        foreach (var customPattern in _customPatterns)
        {
            AddCustomPatterns(customPattern.InjectionType, customPattern.Patterns);
        }
    }

    public InjectionDetectionResult DetectInjection(string input)
    {
        if (string.IsNullOrEmpty(input))
            return new InjectionDetectionResult { InjectionDetected = false };

        var result = new InjectionDetectionResult();
        var detectedPatterns = new List<string>();
        var maxRiskLevel = RiskLevel.None;
        var totalConfidence = 0;
        var patternCount = 0;

        foreach (var kvp in _patterns)
        {
            var injectionType = kvp.Key;
            var patterns = kvp.Value;

            foreach (var pattern in patterns)
            {
                try
                {
                    var regex = new System.Text.RegularExpressions.Regex(pattern, 
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    var matches = regex.Matches(input);
                    
                    if (matches.Count > 0)
                    {
                        result.InjectionDetected = true;
                        result.InjectionType = injectionType;
                        
                        foreach (System.Text.RegularExpressions.Match match in matches)
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
                catch (Exception)
                {
                    // Skip invalid patterns
                }
            }
        }

        result.DetectedPatterns = detectedPatterns;
        result.RiskLevel = maxRiskLevel;
        result.ConfidenceScore = patternCount > 0 ? Math.Min(100, totalConfidence / patternCount) : 0;

        return result;
    }

    public void AddCustomPatterns(InjectionType type, params string[] patterns)
    {
        if (!_patterns.ContainsKey(type))
        {
            _patterns[type] = new List<string>();
        }
        
        _patterns[type].AddRange(patterns);
    }

    public Dictionary<InjectionType, List<string>> GetAllPatterns()
    {
        return new Dictionary<InjectionType, List<string>>(_patterns);
    }

    private static Dictionary<InjectionType, List<string>> InitializeDefaultPatterns()
    {
        return new Dictionary<InjectionType, List<string>>
        {
            [InjectionType.SqlInjection] = new List<string>
            {
                @"\b(union|select|insert|update|delete|drop|create|alter|exec|execute)\b",
                @"[';].*(\-\-|\/\*)",
                @"(\-\-|\/\*|\*\/|;|\||\||`|'|""|\[|\]|\{|\}|%|_|@)"
            },
            [InjectionType.XssInjection] = new List<string>
            {
                @"<\s*script[^>]*>.*?<\s*\/\s*script\s*>",
                @"<\s*\/?(?:script|object|embed|form|input|iframe|meta|link|style|img|svg|math|details|template)\b[^>]*>",
                @"on\w+\s*=",
                @"javascript:",
                @"data:.*base64",
                @"eval\s*\(",
                @"expression\s*\("
            },
            [InjectionType.CommandInjection] = new List<string>
            {
                @"[\|&;`'\""$(){}[\]<>]",
                @"\b(cmd|bash|sh|powershell|exec|system|eval|call)\b"
            },
            [InjectionType.PathTraversal] = new List<string>
            {
                @"(\.\.[/\\])",
                @"(%2e%2e[/\\])",
                @"(%252e%252e[/\\])",
                @"(\.\.%5c)",
                @"(\.\.%2f)"
            },
            [InjectionType.LdapInjection] = new List<string>
            {
                @"[\(\)\*\\\x00]",
                @"\|\|",
                @"&&"
            },
            [InjectionType.XPathInjection] = new List<string>
            {
                @"[\'\""]\s*\[\s*\]",
                @"or\s+1\s*=\s*1",
                @"and\s+1\s*=\s*1"
            }
        };
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
            InjectionType.LdapInjection => RiskLevel.Medium,
            InjectionType.XPathInjection => RiskLevel.Medium,
            _ => RiskLevel.Low
        };

        // Increase risk based on match count
        if (matchCount > 3)
            return RiskLevel.Critical;
        if (matchCount > 1 && baseRisk >= RiskLevel.Medium)
            return RiskLevel.High;

        return baseRisk;
    }
}

/// <summary>
/// Interface for custom input validators
/// </summary>
public interface ICustomInputValidator
{
    string InputType { get; }
}

/// <summary>
/// Generic interface for custom input validators
/// </summary>
public interface ICustomInputValidator<T> : ICustomInputValidator
{
    ValidationResult<T> Validate(string input);
}

/// <summary>
/// Custom input validator implementation
/// </summary>
public class CustomInputValidator<T> : ICustomInputValidator<T>
{
    private readonly Func<string, ValidationResult<T>> _validator;

    public CustomInputValidator(string inputType, Func<string, ValidationResult<T>> validator)
    {
        InputType = inputType;
        _validator = validator;
    }

    public string InputType { get; }

    public ValidationResult<T> Validate(string input)
    {
        return _validator(input);
    }
}

/// <summary>
/// Interface for custom field sanitizers
/// </summary>
public interface ICustomFieldSanitizer
{
    string FieldName { get; }
    string Sanitize(string input);
}

/// <summary>
/// Custom field sanitizer implementation
/// </summary>
public class CustomFieldSanitizer : ICustomFieldSanitizer
{
    private readonly Func<string, string> _sanitizer;

    public CustomFieldSanitizer(string fieldName, Func<string, string> sanitizer)
    {
        FieldName = fieldName;
        _sanitizer = sanitizer;
    }

    public string FieldName { get; }

    public string Sanitize(string input)
    {
        return _sanitizer(input);
    }
}

/// <summary>
/// Interface for custom injection patterns
/// </summary>
public interface ICustomInjectionPatterns
{
    InjectionType InjectionType { get; }
    string[] Patterns { get; }
}

/// <summary>
/// Custom injection patterns implementation
/// </summary>
public class CustomInjectionPatterns : ICustomInjectionPatterns
{
    public CustomInjectionPatterns(InjectionType injectionType, string[] patterns)
    {
        InjectionType = injectionType;
        Patterns = patterns;
    }

    public InjectionType InjectionType { get; }
    public string[] Patterns { get; }
}