using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Girder.Core.Logging;

/// <summary>
/// Describes data without reproducing any of it: names and sizes, never values.
/// </summary>
/// <remarks>
/// The alternative is redacting the fields we recognise, and that is
/// enumeration — a case reference, a note to a doctor, the name of a company
/// someone is leaving: none of it is on any list, and all of it went into the
/// log in full. A description is safe because of how it is built rather than
/// because of what somebody remembered to add.
/// <para>
/// And it keeps what a person debugging actually needs: which fields arrived
/// and whether they were empty.
/// </para>
/// </remarks>
public static class Shape
{
    private const int MaxDepth = 3;

    /// <summary>Describes an object by reflection.</summary>
    public static string Of(object? value)
    {
        if (value is null)
        {
            return "null";
        }

        var description = new StringBuilder();
        Describe(value, description, depth: 0);
        return description.ToString();
    }

    /// <summary>
    /// Describes JSON text, falling back to its size when it is not JSON.
    /// </summary>
    /// <param name="json">The text.</param>
    /// <param name="contentType">Named in the fallback, so the log says what it was.</param>
    public static string OfJson(string? json, string? contentType = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return "[empty]";
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var description = new StringBuilder();
            Describe(document.RootElement, description, depth: 0);
            return description.ToString();
        }
        catch (JsonException)
        {
            // Not JSON, so there is no structure to describe and no safe way to
            // show any of it.
            return $"[{json.Length} characters, {contentType ?? "unknown type"}]";
        }
    }

    private static void Describe(JsonElement element, StringBuilder into, int depth)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object when depth >= MaxDepth:
                into.Append("{…}");
                break;

            case JsonValueKind.Object:
                into.Append('{');
                var first = true;
                foreach (var property in element.EnumerateObject())
                {
                    if (!first)
                    {
                        into.Append(", ");
                    }

                    first = false;
                    into.Append(property.Name).Append(": ");
                    Describe(property.Value, into, depth + 1);
                }

                into.Append('}');
                break;

            case JsonValueKind.Array:
                into.Append('[').Append(element.GetArrayLength()).Append(" items]");
                break;

            case JsonValueKind.String:
                // The length, because "was it empty" is the question a log is
                // asked. The characters are the person's.
                into.Append("string(").Append(element.GetString()?.Length ?? 0).Append(')');
                break;

            case JsonValueKind.Number:
                // A number identifies as readily as a string — a salary, a
                // balance, a date of birth as a timestamp.
                into.Append("number");
                break;

            case JsonValueKind.Null:
                into.Append("null");
                break;

            default:
                into.Append("bool");
                break;
        }
    }

    private static void Describe(object? value, StringBuilder into, int depth)
    {
        switch (value)
        {
            case null:
                into.Append("null");
                return;

            case string text:
                into.Append("string(").Append(text.Length).Append(')');
                return;

            case IEnumerable items and not IDictionary:
                var count = 0;
                foreach (var _ in items)
                {
                    count++;
                }

                into.Append('[').Append(count).Append(" items]");
                return;
        }

        var type = value.GetType();

        if (type.IsPrimitive || value is decimal or DateTime or DateTimeOffset or TimeSpan or Guid)
        {
            into.Append(type.Name.ToLowerInvariant());
            return;
        }

        if (depth >= MaxDepth)
        {
            into.Append("{…}");
            return;
        }

        into.Append('{');
        var first = true;

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            if (!first)
            {
                into.Append(", ");
            }

            first = false;
            into.Append(property.Name).Append(": ");

            try
            {
                Describe(property.GetValue(value), into, depth + 1);
            }
            catch (TargetInvocationException)
            {
                // A property that throws describes itself well enough.
                into.Append("<threw>");
            }
        }

        into.Append('}');
    }
}
