using Girder.Abstractions.Security.Sessions;
using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;

namespace Girder.Data.EntityFrameworkCore.Sessions;

/// <summary>
/// Keeps refresh tokens in the database the application already runs.
/// </summary>
/// <remarks>
/// Which is the point: ending a session should cost a row, not a deployment.
/// Nothing here needs a server of its own.
/// </remarks>
public sealed class EntityFrameworkRefreshTokenStore<TContext>(TContext context) : IRefreshTokenStore
    where TContext : DbContext
{
    private DbSet<GirderRefreshToken> Tokens => context.Set<GirderRefreshToken>();

    /// <inheritdoc />
    public async Task CreateAsync(
        RefreshTokenRecord record,
        CancellationToken cancellationToken = default)
    {
        Tokens.Add(ToRow(record));
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The transition is a conditional update whose row count decides the
    /// outcome: exactly one caller can move a row out of the unconsumed state,
    /// and everyone else reads zero and takes the loser path. Reading the row
    /// first is only to build the successor — the guard is re-evaluated by the
    /// database, so a concurrent winner still costs this caller its update.
    /// <para>
    /// Runs inside a transaction the caller already opened, and opens one only
    /// when there is none.
    /// </para>
    /// </remarks>
    public async Task<ConsumeResult> TryConsumeAsync(
        byte[] tokenHash,
        RefreshTokenRecord successor,
        DateTimeOffset now,
        TimeSpan grace,
        int maxConcurrentPerSession,
        CancellationToken cancellationToken = default)
    {
        var current = await Tokens.AsNoTracking()
            .FirstOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

        if (current is null)
        {
            return new ConsumeResult(ConsumeOutcome.NotFound, null);
        }

        if (current.ReplacedBy is not null)
        {
            return await AlreadyRotatedAsync(current, successor, now, grace, maxConcurrentPerSession, cancellationToken);
        }

        if (current.RevokedAt is not null)
        {
            return new ConsumeResult(ConsumeOutcome.Revoked, null);
        }

        // The ceiling first: once the sign-in is over both are true, because
        // successors are clamped to it.
        if (now.UtcDateTime >= current.SessionExpiresAt)
        {
            return new ConsumeResult(ConsumeOutcome.SessionExpired, null);
        }

        if (now.UtcDateTime >= current.ExpiresAt)
        {
            return new ConsumeResult(ConsumeOutcome.Expired, null);
        }

        var issued = Inherit(successor, current, now);

        if (await ClaimAsync(current, issued, now, maxConcurrentPerSession, cancellationToken))
        {
            return new ConsumeResult(ConsumeOutcome.Rotated, issued);
        }

        // Someone else moved the row between the read and the update. Answering
        // is itself a write — a sibling, or the end of the session — so it
        // happens out here, where no transaction is about to be left behind.
        var reread = await Tokens.AsNoTracking()
            .FirstAsync(token => token.Id == current.Id, cancellationToken);
        return await AlreadyRotatedAsync(reread, successor, now, grace, maxConcurrentPerSession, cancellationToken);
    }

    /// <summary>
    /// Moves one row out of the unconsumed state and writes its successor, both
    /// or neither. False means the row was already gone.
    /// </summary>
    /// <remarks>
    /// A transaction is opened only when none is open. A caller may run the
    /// store inside a unit of work of its own — writing an audit row in the same
    /// transaction as the change it records is the usual reason — and a
    /// connection admits no second transaction. Where one is open this joins it,
    /// so the rotation commits and rolls back with the caller's work.
    /// </remarks>
    private async Task<bool> ClaimAsync(
        GirderRefreshToken current,
        RefreshTokenRecord issued,
        DateTimeOffset now,
        int maxConcurrentPerSession,
        CancellationToken cancellationToken)
    {
        await using var transaction = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var claimed = await Tokens
            .Where(token => token.Id == current.Id
                            && token.RevokedAt == null
                            && token.ReplacedBy == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(token => token.RevokedAt, now.UtcDateTime)
                    .SetProperty(token => token.ReplacedBy, issued.Id.Value),
                cancellationToken);

        if (claimed > 0)
        {
            Tokens.Add(ToRow(issued));
            await context.SaveChangesAsync(cancellationToken);
            await CapAsync(issued.Session, maxConcurrentPerSession, now, cancellationToken);
        }

        // Also when nothing was claimed: the conditional update matched no row,
        // so there is nothing to undo, and rolling back would reach past this
        // method into work that is not its own.
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return claimed > 0;
    }

    /// <summary>
    /// Decides whether a second presentation is a race or a theft.
    /// </summary>
    private async Task<ConsumeResult> AlreadyRotatedAsync(
        GirderRefreshToken current,
        RefreshTokenRecord successor,
        DateTimeOffset now,
        TimeSpan grace,
        int maxConcurrentPerSession,
        CancellationToken cancellationToken)
    {
        if (current.RevokedAt is null || now.UtcDateTime > current.RevokedAt.Value + grace)
        {
            await CloseSessionAsync(new SessionId(current.SessionId), now, cancellationToken);
            return new ConsumeResult(ConsumeOutcome.ReuseDetected, null);
        }

        // Inside the window: a fresh sibling for the same sign-in. The recorded
        // successor is not handed back — only its hash was kept, and keeping the
        // token would give up the reason for hashing it.
        var sibling = Inherit(successor, current, now);

        Tokens.Add(ToRow(sibling));
        await context.SaveChangesAsync(cancellationToken);
        await CapAsync(sibling.Session, maxConcurrentPerSession, now, cancellationToken);

        return new ConsumeResult(ConsumeOutcome.RotatedWithinGrace, sibling);
    }

    /// <inheritdoc />
    public async Task<int> CloseSessionAsync(
        SessionId session,
        DateTimeOffset at,
        CancellationToken cancellationToken = default) =>
        await Tokens
            .Where(token => token.SessionId == session.Value
                            && token.RevokedAt == null
                            && token.ReplacedBy == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.RevokedAt, at.UtcDateTime),
                cancellationToken);

    /// <inheritdoc />
    public async Task<int> CloseAllSessionsAsync(
        SubjectId subject,
        DateTimeOffset at,
        CancellationToken cancellationToken = default) =>
        await Tokens
            .Where(token => token.SubjectId == subject.Value
                            && token.RevokedAt == null
                            && token.ReplacedBy == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.RevokedAt, at.UtcDateTime),
                cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<SessionSummary>> ActiveSessionsAsync(
        SubjectId subject,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var open = await Tokens.AsNoTracking()
            .Where(token => token.SubjectId == subject.Value
                            && token.RevokedAt == null
                            && token.ReplacedBy == null
                            && token.ExpiresAt > now.UtcDateTime
                            && token.SessionExpiresAt > now.UtcDateTime)
            .ToListAsync(cancellationToken);

        return open
            .GroupBy(token => token.SessionId)
            .Select(group => new SessionSummary(
                new SessionId(group.Key),
                Utc(group.Min(token => token.SessionStartedAt)),
                Utc(group.Max(token => token.IssuedAt)),
                Utc(group.Min(token => token.SessionExpiresAt)),
                group.OrderByDescending(token => token.IssuedAt).First().ClientFingerprint))
            .OrderByDescending(summary => summary.LastUsedAt)
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<int> PurgeAsync(
        DateTimeOffset olderThan,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);

        // In batches, because a single unbounded delete competes with
        // TryConsumeAsync for the same pages — on a single-writer database that
        // blocks the sign-in path.
        var finished = await Tokens
            .Where(token => (token.RevokedAt != null && token.RevokedAt < olderThan.UtcDateTime)
                            || (token.ReplacedBy != null && token.IssuedAt < olderThan.UtcDateTime)
                            || token.ExpiresAt < olderThan.UtcDateTime)
            .OrderBy(token => token.IssuedAt)
            .Take(batchSize)
            .Select(token => token.Id)
            .ToListAsync(cancellationToken);

        if (finished.Count == 0)
        {
            return 0;
        }

        return await Tokens
            .Where(token => finished.Contains(token.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>Keeps a session's open tokens within its allowance, oldest first.</summary>
    private async Task CapAsync(
        SessionId session,
        int allowance,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(allowance, 1);

        var open = await Tokens.AsNoTracking()
            .Where(token => token.SessionId == session.Value
                            && token.RevokedAt == null
                            && token.ReplacedBy == null)
            .OrderBy(token => token.IssuedAt)
            .Select(token => token.Id)
            .ToListAsync(cancellationToken);

        var surplus = open.Take(Math.Max(0, open.Count - allowance)).ToList();
        if (surplus.Count == 0)
        {
            return;
        }

        await Tokens
            .Where(token => surplus.Contains(token.Id))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.RevokedAt, at.UtcDateTime),
                cancellationToken);
    }

    /// <summary>
    /// Gives the successor the sign-in's own attributes, so a caller cannot
    /// extend a session past its ceiling by asking for a longer token.
    /// </summary>
    private static RefreshTokenRecord Inherit(
        RefreshTokenRecord successor,
        GirderRefreshToken current,
        DateTimeOffset now) =>
        successor with
        {
            Session = new SessionId(current.SessionId),
            Subject = new SubjectId(current.SubjectId),
            IssuedAt = now,
            SessionStartedAt = Utc(current.SessionStartedAt),
            SessionExpiresAt = Utc(current.SessionExpiresAt),
            ExpiresAt = successor.ExpiresAt > Utc(current.SessionExpiresAt)
                ? Utc(current.SessionExpiresAt)
                : successor.ExpiresAt,
            RevokedAt = null,
            ReplacedBy = null,
            ClientFingerprint = current.ClientFingerprint
        };

    /// <summary>Reads a stored UTC value back as an offset-carrying one.</summary>
    private static DateTimeOffset Utc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static GirderRefreshToken ToRow(RefreshTokenRecord record) => new()
    {
        Id = record.Id.Value,
        SessionId = record.Session.Value,
        SubjectId = record.Subject.Value,
        TokenHash = record.TokenHash,
        IssuedAt = record.IssuedAt.UtcDateTime,
        ExpiresAt = record.ExpiresAt.UtcDateTime,
        SessionStartedAt = record.SessionStartedAt.UtcDateTime,
        SessionExpiresAt = record.SessionExpiresAt.UtcDateTime,
        RevokedAt = record.RevokedAt?.UtcDateTime,
        ReplacedBy = record.ReplacedBy?.Value,
        ClientFingerprint = record.ClientFingerprint
    };
}

public static class EntityFrameworkSessionRegistration
{
    /// <summary>
    /// Keeps refresh tokens in <typeparamref name="TContext"/>.
    /// </summary>
    /// <remarks>
    /// Call <c>ConfigureGirderRefreshTokens</c> from the context's
    /// <c>OnModelCreating</c> and add a migration; the table is the
    /// application's, in the application's database.
    /// </remarks>
    public static Microsoft.Extensions.DependencyInjection.IServiceCollection
        AddEntityFrameworkRefreshTokens<TContext>(
            this Microsoft.Extensions.DependencyInjection.IServiceCollection services)
        where TContext : DbContext =>
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions
            .AddScoped<IRefreshTokenStore, EntityFrameworkRefreshTokenStore<TContext>>(services);
}
