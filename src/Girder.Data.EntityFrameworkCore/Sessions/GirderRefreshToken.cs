using Microsoft.EntityFrameworkCore;

namespace Girder.Data.EntityFrameworkCore.Sessions;

/// <summary>
/// One refresh token, as a row.
/// </summary>
/// <remarks>
/// Infrastructure, in the same sense an outbox table is: it holds what the
/// mechanism needs, not what the application means. Nothing in the application's
/// own model has to inherit from it or know it exists.
/// <para>
/// Session attributes are repeated on every row rather than kept in a second
/// table. That is deliberate: consuming a token has to be one conditional
/// update, and a join would turn the atomicity the contract promises back into
/// an intention.
/// </para>
/// </remarks>
public sealed class GirderRefreshToken
{
    public Guid Id { get; set; }

    /// <summary>The sign-in. Unchanged across the whole rotation chain.</summary>
    public Guid SessionId { get; set; }

    public Guid SubjectId { get; set; }

    /// <summary>SHA-256 of the token. Never the token.</summary>
    public byte[] TokenHash { get; set; } = [];

    /// <summary>
    /// Stored as UTC <see cref="DateTime"/>, not <see cref="DateTimeOffset"/>.
    /// </summary>
    /// <remarks>
    /// SQLite cannot order by a <c>DateTimeOffset</c> at all, and every value
    /// here comes from <c>TimeProvider.GetUtcNow()</c>, so the offset is always
    /// zero and nothing is lost. Ordering by these columns is not optional —
    /// the cap and the purge both depend on it.
    /// </remarks>
    public DateTime IssuedAt { get; set; }

    /// <inheritdoc cref="IssuedAt" />
    public DateTime ExpiresAt { get; set; }

    /// <inheritdoc cref="IssuedAt" />
    public DateTime SessionStartedAt { get; set; }

    /// <summary>The ceiling for the whole sign-in, however often it is refreshed.</summary>
    /// <inheritdoc cref="IssuedAt" />
    public DateTime SessionExpiresAt { get; set; }

    /// <inheritdoc cref="IssuedAt" />
    public DateTime? RevokedAt { get; set; }

    public Guid? ReplacedBy { get; set; }

    /// <summary>
    /// Optional, and only ever shown back to the person whose session it is.
    /// </summary>
    public string? ClientFingerprint { get; set; }
}

public static class GirderRefreshTokenModelBuilderExtensions
{
    /// <summary>
    /// Maps the refresh token table. Call it from <c>OnModelCreating</c>.
    /// </summary>
    /// <remarks>
    /// The two indexes are not optional. Every refresh looks a token up by its
    /// hash, and every "my sessions" and every sign-out scans by subject — both
    /// on paths a person waits for.
    /// </remarks>
    /// <param name="modelBuilder">The model being built.</param>
    /// <param name="tableName">Rename it if the application's conventions differ.</param>
    public static ModelBuilder ConfigureGirderRefreshTokens(
        this ModelBuilder modelBuilder,
        string tableName = "girder_refresh_tokens")
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<GirderRefreshToken>(entity =>
        {
            entity.ToTable(tableName);
            entity.HasKey(token => token.Id);

            entity.Property(token => token.TokenHash).IsRequired();
            entity.Property(token => token.ClientFingerprint).HasMaxLength(256);

            entity.HasIndex(token => token.TokenHash).IsUnique();
            entity.HasIndex(token => new { token.SubjectId, token.RevokedAt });
            entity.HasIndex(token => token.SessionId);
        });

        return modelBuilder;
    }
}
