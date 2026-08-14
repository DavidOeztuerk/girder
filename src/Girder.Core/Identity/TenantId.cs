using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Girder.Core.Identity;

/// <summary>
/// Die Kennung einer Firma.
///
/// <para>
/// <see cref="None"/> ist KEIN "fehlender Mandant" im Sinne von null. Es ist ein
/// vollstaendiger Wert mit genau einer Aufgabe: an der Datenbankgrenze zu
/// vergleichen. Keine Zeile traegt jemals <see cref="None"/> - die Spalte ist
/// Pflicht und per Check-Constraint gegen den Leerwert gesichert. Damit findet
/// eine Privatperson auf einer Firmentabelle null Zeilen, weil der Vergleich
/// nicht zutrifft, und nicht weil irgendwo ein <c>if</c> danebensteht. Ein
/// <c>if</c> kann man vergessen; einen Vergleich nicht.
/// </para>
/// <para>
/// Im Fachmodell taucht <see cref="None"/> nicht auf. Dort sagt
/// <see cref="Capacity"/>, in welcher Eigenschaft jemand handelt.
/// </para>
/// </summary>
[JsonConverter(typeof(TenantIdJsonConverter))]
public readonly record struct TenantId
{
    private readonly Guid _value;

    public TenantId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                "Eine TenantId darf nicht leer sein. Fuer 'handelt fuer keine Firma' "
                + "gibt es Capacity.AsSelf, nicht den Leerwert.",
                nameof(value));
        }

        _value = value;
    }

    /// <summary>
    /// Der Wert, den keine gespeicherte Zeile jemals traegt. Ausschliesslich fuer
    /// den Vergleich im Query-Filter gedacht - siehe Klassenkommentar.
    /// </summary>
    public static TenantId None => default;

    public Guid Value => _value;

    public bool IsNone => _value == Guid.Empty;

    public static TenantId New() => new(Guid.NewGuid());

    public static TenantId Parse(string value) => new(Guid.Parse(value));

    public static bool TryParse([NotNullWhen(true)] string? value, out TenantId id)
    {
        if (Guid.TryParse(value, out var guid) && guid != Guid.Empty)
        {
            id = new TenantId(guid);
            return true;
        }

        id = None;
        return false;
    }

    public override string ToString() => IsNone ? "(keine)" : _value.ToString();
}

/// <summary>Serialisiert als schlichte Zeichenkette, nicht als { "value": ... }.</summary>
public sealed class TenantIdJsonConverter : JsonConverter<TenantId>
{
    public override TenantId Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        => TenantId.TryParse(reader.GetString(), out var id)
            ? id
            : throw new JsonException("Ungueltige TenantId.");

    public override void Write(Utf8JsonWriter writer, TenantId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value.ToString());
}
