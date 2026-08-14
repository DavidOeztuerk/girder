using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Girder.Core.Identity;

/// <summary>
/// Identifies a natural person.
/// </summary>
/// <remarks>
/// A distinct type rather than a raw <see cref="Guid"/> so that the compiler
/// rejects swapping it with a <see cref="TenantId"/>. Serializes as a plain
/// JSON string.
/// </remarks>
[JsonConverter(typeof(SubjectIdJsonConverter))]
public readonly record struct SubjectId
{
    private readonly Guid _value;

    /// <exception cref="ArgumentException">The value is empty.</exception>
    public SubjectId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A SubjectId must not be empty.", nameof(value));
        }

        _value = value;
    }

    public Guid Value => _value;

    /// <summary>
    /// True only for <c>default(SubjectId)</c>, which the language permits for
    /// any struct. Constructed values are never empty.
    /// </summary>
    public bool IsEmpty => _value == Guid.Empty;

    public static SubjectId New() => new(Guid.NewGuid());

    /// <exception cref="ArgumentException">The value is empty.</exception>
    /// <exception cref="FormatException">The value is not a GUID.</exception>
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

public sealed class SubjectIdJsonConverter : JsonConverter<SubjectId>
{
    public override SubjectId Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        => SubjectId.TryParse(reader.GetString(), out var id)
            ? id
            : throw new JsonException("Invalid SubjectId.");

    public override void Write(Utf8JsonWriter writer, SubjectId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}
