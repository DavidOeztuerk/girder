using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Girder.Core.Identity;

/// <summary>
/// Identifies one sign-in — one device, one browser — for as long as it lasts.
/// </summary>
/// <remarks>
/// Stable across every token rotation within that sign-in, which is what makes
/// "sign this device out" and a list of a person's active sessions possible at
/// all. It is the <c>sid</c> claim, and the identity both the revocation store
/// and the refresh-token store point at.
/// <para>
/// Distinct from the identifier of a single refresh token, which changes on
/// every rotation. Conflating the two makes the <c>sid</c> claim change every
/// few minutes and quietly breaks anything that named a session.
/// </para>
/// </remarks>
[JsonConverter(typeof(SessionIdJsonConverter))]
public readonly record struct SessionId
{
    private readonly Guid _value;

    /// <exception cref="ArgumentException">The value is empty.</exception>
    public SessionId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A SessionId must not be empty.", nameof(value));
        }

        _value = value;
    }

    public Guid Value => _value;

    /// <summary>
    /// True only for <c>default(SessionId)</c>, which the language permits for
    /// any struct. Constructed values are never empty.
    /// </summary>
    public bool IsEmpty => _value == Guid.Empty;

    public static SessionId New() => new(Guid.NewGuid());

    /// <exception cref="ArgumentException">The value is empty.</exception>
    /// <exception cref="FormatException">The value is not a GUID.</exception>
    public static SessionId Parse(string value) => new(Guid.Parse(value));

    public static bool TryParse([NotNullWhen(true)] string? value, out SessionId id)
    {
        if (Guid.TryParse(value, out var guid) && guid != Guid.Empty)
        {
            id = new SessionId(guid);
            return true;
        }

        id = default;
        return false;
    }

    public override string ToString() => _value.ToString();
}

public sealed class SessionIdJsonConverter : JsonConverter<SessionId>
{
    public override SessionId Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        => SessionId.TryParse(reader.GetString(), out var id)
            ? id
            : throw new JsonException("Invalid SessionId.");

    public override void Write(Utf8JsonWriter writer, SessionId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}
