using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Girder.Core.Identity;

/// <summary>
/// Die Kennung einer natuerlichen Person.
///
/// Eigener Typ und nicht Guid, damit der Compiler das Vertauschen mit einer
/// <see cref="TenantId"/> bemerkt. Zwei Guid-Parameter nebeneinander lassen sich
/// vertauschen, ohne dass irgendetwas rot wird - und genau dort entstehen
/// Fehler, die Personendaten quer sichtbar machen.
/// </summary>
[JsonConverter(typeof(SubjectIdJsonConverter))]
public readonly record struct SubjectId
{
    private readonly Guid _value;

    public SubjectId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                "Eine SubjectId darf nicht leer sein. Wer keine hat, ist kein Prinzipal.",
                nameof(value));
        }

        _value = value;
    }

    public Guid Value => _value;

    /// <summary>
    /// Nur fuer <c>default(SubjectId)</c> - der Zustand, den C# bei Strukturen
    /// nicht verhindern kann. Konstruierte Werte sind nie leer.
    /// </summary>
    public bool IsEmpty => _value == Guid.Empty;

    public static SubjectId New() => new(Guid.NewGuid());

    public static SubjectId Parse(string value) => new(Guid.Parse(value));

    public static bool TryParse([NotNullWhen(true)] string? value, out SubjectId id)
    {
        if (Guid.TryParse(value, out var guid) && guid != Guid.Empty)
        {
            id = new SubjectId(guid);
            return true;
        }

        id = default;
        return false;
    }

    public override string ToString() => _value.ToString();
}

/// <summary>Serialisiert als schlichte Zeichenkette, nicht als { "value": ... }.</summary>
public sealed class SubjectIdJsonConverter : JsonConverter<SubjectId>
{
    public override SubjectId Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        => SubjectId.TryParse(reader.GetString(), out var id)
            ? id
            : throw new JsonException("Ungueltige SubjectId.");

    public override void Write(Utf8JsonWriter writer, SubjectId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}
