using Girder.Core.Logging;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace Girder.Infrastructure.Logging;

/// <summary>
/// Keeps the values of sensitive properties out of the log, however deeply they
/// are nested.
/// </summary>
/// <remarks>
/// The third path a value can take into a log. The other two — the CQRS
/// behaviour writing a whole command, and the HTTP middleware writing a whole
/// body — already read <see cref="SensitiveFieldNames"/>, and this reads the
/// same list for the same reason: three lists would agree on the day they were
/// written and never again.
/// <para>
/// Matched <b>exactly</b>, never as a substring. A secret's <em>name</em> is not
/// a secret and a token's <em>id</em> is not a token; both are how an operator
/// tells which one is meant, and a log with those redacted is one nobody can
/// follow.
/// </para>
/// </remarks>
public sealed class DataMaskingEnricher : ILogEventEnricher
{
    /// <summary>What replaces a sensitive value. The same mask as everywhere else.</summary>
    public const string Mask = SensitiveValuePatterns.Masked;

    /// <summary>
    /// Whether a property of this name carries a value that must not be logged.
    /// </summary>
    public static bool IsSensitiveKey(string? name) =>
        !string.IsNullOrWhiteSpace(name) && SensitiveFieldNames.All.Contains(name);

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
