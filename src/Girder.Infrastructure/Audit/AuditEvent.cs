using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Girder.Infrastructure.Audit;

/// <summary>
/// A single audit event recording a state change, with SHA-256 hash chaining
/// for tamper detection.
/// </summary>
/// <typeparam name="T">The resource type the event describes.</typeparam>
public sealed record AuditEvent<T>
{
    /// <summary>Unique event identifier.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>UTC timestamp of the event.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Who performed the action (SubjectId, service account, system).</summary>
    public required string ActorId { get; init; }

    /// <summary>
    /// The capacity the actor was acting in — as self, or on behalf of a company.
    /// Maps to <c>Capacity</c> from Girder's identity model.
    /// </summary>
    public required string Capacity { get; init; }

    /// <summary>What the actor did (Created, Updated, Deleted, Accessed, …).</summary>
    public required string Action { get; init; }

    /// <summary>Which resource was affected (type name, path, or identifier).</summary>
    public required string Resource { get; init; }

    /// <summary>Correlation id for cross-service traceability.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>JSON snapshot of the resource before the change, or <c>null</c> for creates.</summary>
    public string? BeforeStateJson { get; init; }

    /// <summary>JSON snapshot of the resource after the change, or <c>null</c> for deletes.</summary>
    public string? AfterStateJson { get; init; }

    /// <summary>SHA-256 hash of the previous event in the chain.</summary>
    public string? PreviousHash { get; init; }

    /// <summary>SHA-256 hash of this event, computed over all fields including <see cref="PreviousHash"/>.</summary>
    public string Hash { get; init; } = string.Empty;

    /// <summary>
    /// Computes the SHA-256 hash for this event, chaining it to the previous hash.
    /// Returns a new <see cref="AuditEvent{T}"/> with <see cref="Hash"/> set.
    /// </summary>
    public AuditEvent<T> WithComputedHash()
    {
        var input = new StringBuilder();

        Append(input, Id);
        Append(input, Timestamp.ToString("O"));
        Append(input, ActorId);
        Append(input, Capacity);
        Append(input, Action);
        Append(input, Resource);
        Append(input, CorrelationId);
        Append(input, BeforeStateJson);
        Append(input, AfterStateJson);
        Append(input, PreviousHash);

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input.ToString()));
        return this with { Hash = Convert.ToBase64String(hashBytes) };
    }

    /// <summary>
    /// Writes one field, preceded by its length.
    /// </summary>
    /// <remarks>
    /// The length is what makes the encoding unambiguous. Joined by a separator
    /// alone, content shifted across a field boundary produces the same string
    /// and therefore the same hash — and <c>CorrelationId</c> comes from a header
    /// the caller sets, while the state snapshots are JSON. A tamper-evidence
    /// hash that accepts a forgery is not one.
    /// </remarks>
    private static void Append(StringBuilder input, string? field)
    {
        var value = field ?? string.Empty;
        input.Append(value.Length).Append(':').Append(value).Append('|');
    }

    /// <summary>
    /// Verifies this event's hash against a recomputed one.
    /// </summary>
    public bool VerifyHash() =>
        !string.IsNullOrEmpty(Hash) && Hash == WithComputedHash().Hash;

    /// <summary>
    /// Serializes <paramref name="state"/> to JSON for use in
    /// <see cref="BeforeStateJson"/> or <see cref="AfterStateJson"/>.
    /// </summary>
    public static string? ToJson(T? state) =>
        state is null ? null : JsonSerializer.Serialize(state, JsonOptions);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}
