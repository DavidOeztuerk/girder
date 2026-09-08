using System.Diagnostics.CodeAnalysis;
using Girder.Core.Logging;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace Girder.Infrastructure.Logging;

/// <summary>
/// Serilog enricher and destructuring policy for GDPR / PII protection.
/// Recursively masks sensitive keys (Password, Token, Secret, Authorization, IBAN)
/// with "***".
/// </summary>
public sealed class DataMaskingEnricher : ILogEventEnricher, IDestructuringPolicy
{
    public const string Mask = "***";

    private static readonly string[] SensitiveKeys =
    [
        "Password",
        "Token",
        "Secret",
        "Authorization",
        "IBAN"
    ];

    /// <summary>
    /// Checks whether the specified property name is considered sensitive.
    /// </summary>
    public static bool IsSensitiveKey(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        // Avoid false positives for common framework tokens like CancellationToken
        if (name.EndsWith("CancellationToken", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var sensitive in SensitiveKeys)
        {
            if (name.Contains(sensitive, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        foreach (var (key, value) in logEvent.Properties.ToList())
        {
            var maskedValue = MaskPropertyValue(key, value);
            if (!ReferenceEquals(maskedValue, value))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(key, maskedValue));
            }
        }
    }

    /// <inheritdoc />
    public bool TryDestructure(
        object value,
        ILogEventPropertyValueFactory propertyValueFactory,
        [NotNullWhen(true)] out LogEventPropertyValue? result)
    {
        // Allow Serilog's standard destructuring to build the StructureValue / DictionaryValue / SequenceValue.
        // The enricher will then recursively traverse and mask all sensitive keys on the resulting log event.
        result = null;
        return false;
    }

    /// <summary>
    /// Recursively masks a property value.
    /// </summary>
    public static LogEventPropertyValue MaskPropertyValue(string key, LogEventPropertyValue value)
    {
        if (IsSensitiveKey(key))
        {
            return new ScalarValue(Mask);
        }

        return value switch
        {
            StructureValue structure => MaskStructure(structure),
            DictionaryValue dictionary => MaskDictionary(dictionary),
            SequenceValue sequence => MaskSequence(sequence),
            _ => value
        };
    }

    private static StructureValue MaskStructure(StructureValue structure)
    {
        var modified = false;
        var maskedProperties = new List<LogEventProperty>(structure.Properties.Count);

        foreach (var prop in structure.Properties)
        {
            var maskedPropValue = MaskPropertyValue(prop.Name, prop.Value);
            if (!ReferenceEquals(maskedPropValue, prop.Value))
            {
                modified = true;
                maskedProperties.Add(new LogEventProperty(prop.Name, maskedPropValue));
            }
            else
            {
                maskedProperties.Add(prop);
            }
        }

        return modified ? new StructureValue(maskedProperties, structure.TypeTag) : structure;
    }

    private static DictionaryValue MaskDictionary(DictionaryValue dictionary)
    {
        var modified = false;
        var maskedElements = new Dictionary<ScalarValue, LogEventPropertyValue>(dictionary.Elements.Count);

        foreach (var (keyScalar, elementValue) in dictionary.Elements)
        {
            var keyName = keyScalar.Value?.ToString() ?? string.Empty;
            var maskedVal = MaskPropertyValue(keyName, elementValue);

            if (!ReferenceEquals(maskedVal, elementValue))
            {
                modified = true;
            }

            maskedElements[keyScalar] = maskedVal;
        }

        return modified ? new DictionaryValue(maskedElements) : dictionary;
    }

    private static SequenceValue MaskSequence(SequenceValue sequence)
    {
        var modified = false;
        var maskedElements = new List<LogEventPropertyValue>(sequence.Elements.Count);

        foreach (var item in sequence.Elements)
        {
            var maskedItem = MaskPropertyValue(string.Empty, item);
            if (!ReferenceEquals(maskedItem, item))
            {
                modified = true;
                maskedElements.Add(maskedItem);
            }
            else
            {
                maskedElements.Add(item);
            }
        }

        return modified ? new SequenceValue(maskedElements) : sequence;
    }
}

/// <summary>
/// Extension methods for registering <see cref="DataMaskingEnricher"/>.
/// </summary>
public static class DataMaskingExtensions
{
    /// <summary>
    /// Registers the <see cref="DataMaskingEnricher"/> to mask sensitive keys recursively.
    /// </summary>
    public static LoggerConfiguration WithDataMasking(this LoggerEnrichmentConfiguration enrichmentConfiguration)
    {
        ArgumentNullException.ThrowIfNull(enrichmentConfiguration);
        return enrichmentConfiguration.With<DataMaskingEnricher>();
    }
}
