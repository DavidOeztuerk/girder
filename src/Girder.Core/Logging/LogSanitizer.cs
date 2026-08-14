using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Core.Common.Logging;

/// <summary>
/// Default implementation of log sanitizer for removing sensitive data
/// </summary>
public class LogSanitizer : ILogSanitizer
{
    private readonly HashSet<string> _sensitiveProperties;
    private readonly Regex _emailRegex = new(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}", RegexOptions.Compiled);
    private readonly Regex _creditCardRegex = new(@"\b(?:\d[ -]*?){13,19}\b", RegexOptions.Compiled);
    private readonly Regex _ssnRegex = new(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled);
    private readonly Regex _sensitiveUrlParamRegex = new(@"[\?&](code|token|email|access_token|verification_?[Tt]oken|verificationCode)=[^&\s]*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private const string MaskedValue = "[REDACTED]";
    private const int MaxDepth = 5;

    public LogSanitizer()
    {
        _sensitiveProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "password", "pwd", "pass", "passwd",
            "secret", "token", "apikey", "api_key", "api-key",
            "authorization", "auth", "bearer",
            "creditcard", "credit_card", "credit-card", "cc", "cardnumber", "card_number",
            "cvv", "cvc", "securitycode", "security_code",
            "ssn", "socialsecuritynumber", "social_security_number",
            "email", "emailaddress", "email_address",
            "phone", "phonenumber", "phone_number", "mobile",
            "birthdate", "birth_date", "dob", "dateofbirth",
            "bankaccount", "bank_account", "accountnumber", "account_number",
            "routingnumber", "routing_number",
            "connectionstring", "connection_string",
            "privatekey", "private_key", "publickey", "public_key",
            "accesstoken", "access_token", "refreshtoken", "refresh_token",
            "otp", "verificationcode", "verification_code"
        };
    }

    public object? Sanitize(object? data)
    {
        if (data == null)
            return null;

        return SanitizeCore(data, 0);
    }

    private object? SanitizeCore(object? data, int depth)
    {
        if (data == null || depth > MaxDepth)
            return data;

        var type = data.GetType();

        // Handle primitive types and strings
        if (type.IsPrimitive || type == typeof(decimal))
            return data;

        if (type == typeof(string))
            return SanitizeString(data.ToString()!);

        // Handle DateTime and similar types
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset) || 
            type == typeof(TimeSpan) || type == typeof(Guid))
            return data;

        // Handle collections
        if (data is IEnumerable enumerable && !(data is string))
            return SanitizeEnumerable(enumerable, depth);

        // Handle objects with properties
        return SanitizeObject(data, depth);
    }

    private object SanitizeObject(object obj, int depth)
    {
        var type = obj.GetType();
        var sanitized = new Dictionary<string, object?>();

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead)
                continue;

            try
            {
                var value = property.GetValue(obj);
                var propertyName = property.Name;

                if (ShouldSanitizeProperty(propertyName))
                {
                    sanitized[propertyName] = MaskedValue;
                }
                else
                {
                    sanitized[propertyName] = SanitizeCore(value, depth + 1);
                }
            }
            catch
            {
                // Skip properties that throw exceptions
            }
        }

        return sanitized;
    }

    private object SanitizeEnumerable(IEnumerable enumerable, int depth)
    {
        var result = new List<object?>();
        
        foreach (var item in enumerable)
        {
            result.Add(SanitizeCore(item, depth + 1));
        }

        return result;
    }

    private string SanitizeString(string value)
    {
        // Check for patterns that look like sensitive data
        if (_emailRegex.IsMatch(value))
            return _emailRegex.Replace(value, MaskedValue);

        if (_creditCardRegex.IsMatch(value))
            return _creditCardRegex.Replace(value, MaskedValue);

        if (_ssnRegex.IsMatch(value))
            return _ssnRegex.Replace(value, MaskedValue);

        // Check for JWT tokens
        if (value.StartsWith("eyJ") && value.Length > 100 && value.Count(c => c == '.') >= 2)
            return MaskedValue;

        // Check for URLs containing sensitive query parameters (verification links, etc.)
        if (_sensitiveUrlParamRegex.IsMatch(value))
        {
            return _sensitiveUrlParamRegex.Replace(value, m =>
            {
                var paramName = m.Value.Split('=')[0];
                return $"{paramName}=[REDACTED]";
            });
        }

        // Check for base64 encoded potential secrets
        if (value.Length > 40 && IsBase64String(value))
            return MaskedValue;

        return value;
    }

    private static bool IsBase64String(string value)
    {
        try
        {
            if (value.Length % 4 != 0)
                return false;

            return Regex.IsMatch(value, @"^[a-zA-Z0-9\+/]*={0,2}$");
        }
        catch
        {
            return false;
        }
    }

    public bool ShouldSanitizeProperty(string propertyName)
    {
        return _sensitiveProperties.Contains(propertyName);
    }

    public void RegisterSensitiveProperty(params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            _sensitiveProperties.Add(name);
        }
    }
}