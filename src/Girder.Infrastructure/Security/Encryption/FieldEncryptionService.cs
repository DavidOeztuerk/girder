using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Text.Json;

namespace Infrastructure.Security.Encryption;

/// <summary>
/// Service for field-level encryption using attributes
/// </summary>
public class FieldEncryptionService : IFieldEncryptionService
{
    private readonly IDataEncryptionService _encryptionService;
    private readonly ILogger<FieldEncryptionService> _logger;
    private readonly Dictionary<Type, FieldInfo[]> _encryptedFieldsCache = new();
    private readonly Dictionary<Type, PropertyInfo[]> _encryptedPropertiesCache = new();

    public FieldEncryptionService(
        IDataEncryptionService encryptionService,
        ILogger<FieldEncryptionService> logger)
    {
        _encryptionService = encryptionService;
        _logger = logger;
    }

    public async Task<T> EncryptFieldsAsync<T>(
        T obj,
        FieldEncryptionOptions? options = null,
        CancellationToken cancellationToken = default) where T : class
    {
        if (obj == null)
            return obj!;

        options ??= new FieldEncryptionOptions();

        try
        {
            var type = typeof(T);
            var encryptedFields = GetEncryptedFields(type);
            var encryptedProperties = GetEncryptedProperties(type);

            // Encrypt fields
            foreach (var field in encryptedFields)
            {
                if (ShouldEncryptField(field.Name, options))
                {
                    await EncryptFieldValueAsync(obj, field, options.Context, cancellationToken);
                }
            }

            // Encrypt properties
            foreach (var property in encryptedProperties)
            {
                if (ShouldEncryptField(property.Name, options))
                {
                    await EncryptPropertyValueAsync(obj, property, options.Context, cancellationToken);
                }
            }

            return obj;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error encrypting fields for type {Type}", typeof(T).Name);
            throw;
        }
    }

    public async Task<T> DecryptFieldsAsync<T>(T obj, CancellationToken cancellationToken = default) where T : class
    {
        if (obj == null)
            return obj!;

        try
        {
            var type = typeof(T);
            var encryptedFields = GetEncryptedFields(type);
            var encryptedProperties = GetEncryptedProperties(type);

            // Decrypt fields
            foreach (var field in encryptedFields)
            {
                await DecryptFieldValueAsync(obj, field, cancellationToken);
            }

            // Decrypt properties
            foreach (var property in encryptedProperties)
            {
                await DecryptPropertyValueAsync(obj, property, cancellationToken);
            }

            return obj;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error decrypting fields for type {Type}", typeof(T).Name);
            throw;
        }
    }

    public async Task<string> EncryptJsonFieldAsync(
        string json,
        string fieldPath,
        EncryptionContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var jsonDocument = JsonDocument.Parse(json);
            var root = jsonDocument.RootElement.Clone();

            var modifiedJson = await EncryptJsonElementAsync(root, fieldPath, context, cancellationToken);
            return modifiedJson;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error encrypting JSON field {FieldPath}", fieldPath);
            throw;
        }
    }

    public async Task<string> DecryptJsonFieldAsync(
        string json,
        string fieldPath,
        EncryptionContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var jsonDocument = JsonDocument.Parse(json);
            var root = jsonDocument.RootElement.Clone();

            var modifiedJson = await DecryptJsonElementAsync(root, fieldPath, context, cancellationToken);
            return modifiedJson;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error decrypting JSON field {FieldPath}", fieldPath);
            throw;
        }
    }

    public async Task<FieldEncryptionStatus> GetFieldEncryptionStatusAsync<T>(
        T obj,
        CancellationToken cancellationToken = default) where T : class
    {
        await Task.CompletedTask;
        if (obj == null)
        {
            return new FieldEncryptionStatus();
        }

        try
        {
            var type = typeof(T);
            var status = new FieldEncryptionStatus();

            var encryptedFields = GetEncryptedFields(type);
            var encryptedProperties = GetEncryptedProperties(type);
            var sensitiveFields = GetSensitiveFields(type);
            var sensitiveProperties = GetSensitiveProperties(type);

            var totalSensitiveFields = encryptedFields.Length + encryptedProperties.Length +
                                     sensitiveFields.Length + sensitiveProperties.Length;
            var encryptedCount = 0;

            // Check encrypted fields
            foreach (var field in encryptedFields)
            {
                var value = field.GetValue(obj)?.ToString();
                if (!string.IsNullOrEmpty(value))
                {
                    var isEncrypted = IsValueEncrypted(value);
                    if (isEncrypted)
                    {
                        encryptedCount++;
                        var encryptedAttr = field.GetCustomAttribute<EncryptedAttribute>();
                        status.EncryptedFields[field.Name] = new FieldEncryptionInfo
                        {
                            FieldName = field.Name,
                            EncryptionTimestamp = DateTime.UtcNow, // Would be parsed from encrypted data
                            Classification = encryptedAttr?.Classification ?? DataClassification.Confidential,
                            Algorithm = encryptedAttr?.Algorithm ?? EncryptionAlgorithm.AES256GCM
                        };
                    }
                    else
                    {
                        status.UnencryptedSensitiveFields.Add(field.Name);
                    }
                }
            }

            // Check encrypted properties
            foreach (var property in encryptedProperties)
            {
                var value = property.GetValue(obj)?.ToString();
                if (!string.IsNullOrEmpty(value))
                {
                    var isEncrypted = IsValueEncrypted(value);
                    if (isEncrypted)
                    {
                        encryptedCount++;
                        var encryptedAttr = property.GetCustomAttribute<EncryptedAttribute>();
                        status.EncryptedFields[property.Name] = new FieldEncryptionInfo
                        {
                            FieldName = property.Name,
                            EncryptionTimestamp = DateTime.UtcNow,
                            Classification = encryptedAttr?.Classification ?? DataClassification.Confidential,
                            Algorithm = encryptedAttr?.Algorithm ?? EncryptionAlgorithm.AES256GCM
                        };
                    }
                    else
                    {
                        status.UnencryptedSensitiveFields.Add(property.Name);
                    }
                }
            }

            // Check sensitive fields (should be hashed)
            foreach (var field in sensitiveFields)
            {
                var value = field.GetValue(obj)?.ToString();
                if (!string.IsNullOrEmpty(value))
                {
                    var isHashed = IsValueHashed(value);
                    if (isHashed)
                    {
                        encryptedCount++; // Count hashed as "encrypted" for coverage
                    }
                    else
                    {
                        status.UnencryptedSensitiveFields.Add(field.Name);
                    }
                }
            }

            // Check sensitive properties (should be hashed)
            foreach (var property in sensitiveProperties)
            {
                var value = property.GetValue(obj)?.ToString();
                if (!string.IsNullOrEmpty(value))
                {
                    var isHashed = IsValueHashed(value);
                    if (isHashed)
                    {
                        encryptedCount++;
                    }
                    else
                    {
                        status.UnencryptedSensitiveFields.Add(property.Name);
                    }
                }
            }

            // Calculate coverage
            status.EncryptionCoverage = totalSensitiveFields > 0 ?
                (double)encryptedCount / totalSensitiveFields * 100 : 100;

            // Check compliance
            status.IsCompliant = status.UnencryptedSensitiveFields.Count == 0;
            if (!status.IsCompliant)
            {
                status.ComplianceViolations.Add($"Found {status.UnencryptedSensitiveFields.Count} unencrypted sensitive fields");
            }

            return status;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting field encryption status for type {Type}", typeof(T).Name);
            return new FieldEncryptionStatus();
        }
    }

    #region Private Methods

    private FieldInfo[] GetEncryptedFields(Type type)
    {
        if (_encryptedFieldsCache.TryGetValue(type, out var cachedFields))
        {
            return cachedFields;
        }

        var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(f => f.GetCustomAttribute<EncryptedAttribute>() != null &&
                       f.GetCustomAttribute<ExcludeFromEncryptionAttribute>() == null)
            .ToArray();

        _encryptedFieldsCache[type] = fields;
        return fields;
    }

    private PropertyInfo[] GetEncryptedProperties(Type type)
    {
        if (_encryptedPropertiesCache.TryGetValue(type, out var cachedProperties))
        {
            return cachedProperties;
        }

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<EncryptedAttribute>() != null &&
                       p.GetCustomAttribute<ExcludeFromEncryptionAttribute>() == null &&
                       p.CanRead && p.CanWrite)
            .ToArray();

        _encryptedPropertiesCache[type] = properties;
        return properties;
    }

    private FieldInfo[] GetSensitiveFields(Type type)
    {
        return type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(f => f.GetCustomAttribute<SensitiveAttribute>() != null)
            .ToArray();
    }

    private PropertyInfo[] GetSensitiveProperties(Type type)
    {
        return type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<SensitiveAttribute>() != null &&
                       p.CanRead && p.CanWrite)
            .ToArray();
    }

    private bool ShouldEncryptField(string fieldName, FieldEncryptionOptions options)
    {
        // Check exclusion list
        if (options.ExcludedFields.Contains(fieldName))
            return false;

        // If specific fields are specified, only encrypt those
        if (options.FieldsToEncrypt != null && options.FieldsToEncrypt.Any())
            return options.FieldsToEncrypt.Contains(fieldName);

        // Otherwise, encrypt all attributed fields
        return true;
    }

    private async Task EncryptFieldValueAsync(
        object obj,
        FieldInfo field,
        EncryptionContext context,
        CancellationToken cancellationToken)
    {
        var value = field.GetValue(obj)?.ToString();
        if (string.IsNullOrEmpty(value) || IsValueEncrypted(value))
            return;

        var encryptedAttr = field.GetCustomAttribute<EncryptedAttribute>();
        if (encryptedAttr != null)
        {
            // Create context from attribute
            var fieldContext = CreateContextFromAttribute(encryptedAttr, context);

            var encryptionResult = await _encryptionService.EncryptAsync(value, fieldContext, cancellationToken);
            if (encryptionResult.Success)
            {
                field.SetValue(obj, encryptionResult.EncryptedData);
            }
            else
            {
                _logger.LogError("Failed to encrypt field {FieldName}: {Error}", field.Name, encryptionResult.ErrorMessage);
            }
        }
    }

    private async Task EncryptPropertyValueAsync(
        object obj,
        PropertyInfo property,
        EncryptionContext context,
        CancellationToken cancellationToken)
    {
        var value = property.GetValue(obj)?.ToString();
        if (string.IsNullOrEmpty(value) || IsValueEncrypted(value))
            return;

        var encryptedAttr = property.GetCustomAttribute<EncryptedAttribute>();
        if (encryptedAttr != null)
        {
            var fieldContext = CreateContextFromAttribute(encryptedAttr, context);

            var encryptionResult = await _encryptionService.EncryptAsync(value, fieldContext, cancellationToken);
            if (encryptionResult.Success)
            {
                property.SetValue(obj, encryptionResult.EncryptedData);
            }
            else
            {
                _logger.LogError("Failed to encrypt property {PropertyName}: {Error}", property.Name, encryptionResult.ErrorMessage);
            }
        }
    }

    private async Task DecryptFieldValueAsync(
        object obj,
        FieldInfo field,
        CancellationToken cancellationToken)
    {
        var value = field.GetValue(obj)?.ToString();
        if (string.IsNullOrEmpty(value) || !IsValueEncrypted(value))
            return;

        var encryptedAttr = field.GetCustomAttribute<EncryptedAttribute>();
        if (encryptedAttr != null)
        {
            var context = new EncryptionContext
            {
                Classification = encryptedAttr.Classification,
                Purpose = encryptedAttr.Purpose
            };

            var decryptionResult = await _encryptionService.DecryptAsync(value, context, cancellationToken);
            if (decryptionResult.Success)
            {
                field.SetValue(obj, decryptionResult.Data);
            }
            else
            {
                _logger.LogError("Failed to decrypt field {FieldName}: {Error}", field.Name, decryptionResult.ErrorMessage);
            }
        }
    }

    private async Task DecryptPropertyValueAsync(
        object obj,
        PropertyInfo property,
        CancellationToken cancellationToken)
    {
        var value = property.GetValue(obj)?.ToString();
        if (string.IsNullOrEmpty(value) || !IsValueEncrypted(value))
            return;

        var encryptedAttr = property.GetCustomAttribute<EncryptedAttribute>();
        if (encryptedAttr != null)
        {
            var context = new EncryptionContext
            {
                Classification = encryptedAttr.Classification,
                Purpose = encryptedAttr.Purpose
            };

            var decryptionResult = await _encryptionService.DecryptAsync(value, context, cancellationToken);
            if (decryptionResult.Success)
            {
                property.SetValue(obj, decryptionResult.Data);
            }
            else
            {
                _logger.LogError("Failed to decrypt property {PropertyName}: {Error}", property.Name, decryptionResult.ErrorMessage);
            }
        }
    }

    private static EncryptionContext CreateContextFromAttribute(EncryptedAttribute attr, EncryptionContext baseContext)
    {
        var context = new EncryptionContext
        {
            Classification = attr.Classification,
            Purpose = attr.Purpose,
            UserId = baseContext.UserId,
            OrganizationId = baseContext.OrganizationId,
            Metadata = new Dictionary<string, string>(baseContext.Metadata)
        };

        if (attr.ComplianceRequirements != null)
        {
            context.ComplianceRequirements.AddRange(attr.ComplianceRequirements);
        }

        if (!string.IsNullOrEmpty(attr.RetentionPeriod) && TimeSpan.TryParse(attr.RetentionPeriod, out var retention))
        {
            context.RetentionPeriod = retention;
        }

        if (!string.IsNullOrEmpty(attr.GeographicRestriction))
        {
            context.GeographicRestriction = attr.GeographicRestriction;
        }

        if (!string.IsNullOrEmpty(attr.ContextMetadata))
        {
            try
            {
                var metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(attr.ContextMetadata);
                if (metadata != null)
                {
                    foreach (var kvp in metadata)
                    {
                        context.Metadata[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch (JsonException ex)
            {
                // Log error but continue
                System.Diagnostics.Debug.WriteLine($"Failed to parse context metadata: {ex.Message}");
            }
        }

        return context;
    }

    private static bool IsValueEncrypted(string value)
    {
        try
        {
            // Check if value looks like encrypted data (Base64 JSON structure)
            if (string.IsNullOrEmpty(value))
                return false;

            var decoded = Convert.FromBase64String(value);
            var json = System.Text.Encoding.UTF8.GetString(decoded);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            return root.TryGetProperty("KeyId", out _) &&
                   root.TryGetProperty("Algorithm", out _) &&
                   root.TryGetProperty("Data", out _);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsValueHashed(string value)
    {
        try
        {
            // Check if value looks like hashed data
            if (string.IsNullOrEmpty(value))
                return false;

            // Check for common hash formats or JSON structure containing hash data
            var hashInfo = JsonSerializer.Deserialize<Dictionary<string, object>>(value);
            return hashInfo != null &&
                   hashInfo.ContainsKey("Hash") &&
                   hashInfo.ContainsKey("Salt") &&
                   hashInfo.ContainsKey("Algorithm");
        }
        catch
        {
            // Also check for simple Base64 patterns that might be hashes
            return value.Length >= 32 && value.All(c => char.IsLetterOrDigit(c) || c == '+' || c == '/' || c == '=');
        }
    }

    private async Task<string> EncryptJsonElementAsync(
        JsonElement element,
        string fieldPath,
        EncryptionContext context,
        CancellationToken cancellationToken)
    {
        // Simplified JSON field encryption - in production, implement proper JSON path handling
        var json = element.GetRawText();

        if (fieldPath.Contains('.'))
        {
            // Handle nested paths
            var parts = fieldPath.Split('.');
            // Implement nested JSON field encryption
        }
        else
        {
            // Handle simple field names
            if (element.TryGetProperty(fieldPath, out var fieldElement))
            {
                var fieldValue = fieldElement.GetString();
                if (!string.IsNullOrEmpty(fieldValue))
                {
                    var encryptionResult = await _encryptionService.EncryptAsync(fieldValue, context, cancellationToken);
                    if (encryptionResult.Success)
                    {
                        // Replace field value in JSON
                        // Simplified implementation - use proper JSON manipulation
                        json = json.Replace($"\"{fieldValue}\"", $"\"{encryptionResult.EncryptedData}\"");
                    }
                }
            }
        }

        return json;
    }

    private async Task<string> DecryptJsonElementAsync(
        JsonElement element,
        string fieldPath,
        EncryptionContext context,
        CancellationToken cancellationToken)
    {
        // Simplified JSON field decryption
        var json = element.GetRawText();

        if (element.TryGetProperty(fieldPath, out var fieldElement))
        {
            var fieldValue = fieldElement.GetString();
            if (!string.IsNullOrEmpty(fieldValue) && IsValueEncrypted(fieldValue))
            {
                var decryptionResult = await _encryptionService.DecryptAsync(fieldValue, context, cancellationToken);
                if (decryptionResult.Success)
                {
                    json = json.Replace($"\"{fieldValue}\"", $"\"{decryptionResult.Data}\"");
                }
            }
        }

        return json;
    }

    #endregion
}