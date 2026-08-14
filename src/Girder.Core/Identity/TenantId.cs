using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Girder.Core.Identity;

/// <summary>
/// Identifies a company.
/// </summary>
/// <remarks>
/// A distinct type rather than a raw <see cref="Guid"/> so that the compiler
/// rejects swapping it with a <see cref="SubjectId"/>. Serializes as a plain
/// JSON string.
/// </remarks>
[JsonConverter(typeof(TenantIdJsonConverter))]
public readonly record struct TenantId
{
    private readonly Guid _value;

    /// <exception cref="ArgumentException">The value is empty.</exception>
    public TenantId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                "A TenantId must not be empty. Use Capacity.AsSelf to express "
                + "that a caller acts for no company.",
                nameof(value));
        }

        _value = value;
    }

    /// <summary>
    /// The value that no stored row ever carries.
    /// </summary>
    /// <remarks>
    /// Exists only so that query filters have a total value to compare against:
    /// a caller acting as a person matches no tenant-owned row. Domain code
    /// expresses "acts for no company" as <see cref="Capacity.AsSelf"/> instead.
    /// </remarks>
    public static TenantId None => default;

    public Guid Value => _value;

    public bool IsNone => _value == Guid.Empty;

    public static TenantId New() => new(Guid.NewGuid());

    /// <exception cref="ArgumentException">The value is empty.</exception>
    /// <exception cref="FormatException">The value is not a GUID.</exception>
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

    public override string ToString() => IsNone ? "(none)" : _value.ToString();
}

public sealed class TenantIdJsonConverter : JsonConverter<TenantId>
{
    public override TenantId Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        => TenantId.TryParse(reader.GetString(), out var id)
            ? id
            : throw new JsonException("Invalid TenantId.");

    public override void Write(Utf8JsonWriter writer, TenantId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value.ToString());
}
