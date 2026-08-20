using System.Diagnostics.CodeAnalysis;
using Girder.Core.Identity;

namespace Girder.Abstractions.Security.Sessions;

/// <summary>Identifies one refresh token within a session.</summary>
/// <remarks>
/// Changes on every rotation, unlike <see cref="SessionId"/>, which does not.
/// </remarks>
public readonly record struct RefreshTokenId
{
    private readonly Guid _value;

    /// <exception cref="ArgumentException">The value is empty.</exception>
    public RefreshTokenId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A RefreshTokenId must not be empty.", nameof(value));
        }

        _value = value;
    }

    public Guid Value => _value;

    public bool IsEmpty => _value == Guid.Empty;

    public static RefreshTokenId New() => new(Guid.NewGuid());

    public static bool TryParse([NotNullWhen(true)] string? value, out RefreshTokenId id)
    {
        if (Guid.TryParse(value, out var guid) && guid != Guid.Empty)
        {
            id = new RefreshTokenId(guid);
            return true;
        }

        id = default;
        return false;
    }

    public override string ToString() => _value.ToString();
}

/// <summary>
/// One refresh token as it is stored.
/// </summary>
/// <remarks>
/// A value with no ORM attributes and no base class: the entity stays the
/// application's. Session attributes are repeated on every row rather than kept
/// in a second table, so that consuming a token remains a single conditional
/// update — a join would turn the atomicity this contract promises back into an
/// intention.
/// </remarks>
/// <param name="Id">This token. New on every rotation.</param>
/// <param name="Session">The sign-in. Unchanged across the whole rotation chain.</param>
/// <param name="Subject">Whose session it is.</param>
/// <param name="TokenHash">
/// SHA-256 of the token, never the token. The store ends up in backups and on
/// screens; a refresh token is a bearer credential, and in the clear a leaked
/// dump is every session.
/// </param>
/// <param name="IssuedAt">When this token was written.</param>
/// <param name="ExpiresAt">When this token stops working.</param>
/// <param name="SessionStartedAt">When the sign-in happened. Copied on rotation.</param>
/// <param name="SessionExpiresAt">
/// The hard ceiling for the whole sign-in, also copied on rotation. Without it,
/// sliding expiration plus rotation is a session that never ends — which is
/// exactly the state an undetected stolen token wants.
/// </param>
/// <param name="RevokedAt">When this token stopped counting, if it has.</param>
/// <param name="ReplacedBy">The successor, if this token was rotated.</param>
/// <param name="ClientFingerprint">
/// Optional, and only ever to show a person their own sessions. Never a
/// criterion for a decision: "the user agent changed, end the session" fires on
/// every browser update and signs out people who did nothing wrong.
/// </param>
public readonly record struct RefreshTokenRecord(
    RefreshTokenId Id,
    SessionId Session,
    SubjectId Subject,
    byte[] TokenHash,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset SessionStartedAt,
    DateTimeOffset SessionExpiresAt,
    DateTimeOffset? RevokedAt,
    RefreshTokenId? ReplacedBy,
    string? ClientFingerprint);

/// <summary>What happened when a refresh token was presented.</summary>
public enum ConsumeOutcome
{
    /// <summary>Presented once, rotated, successor written.</summary>
    Rotated,

    /// <summary>
    /// Already rotated, but so recently that this is a race rather than a
    /// theft — two tabs, or a client retry. A fresh token is issued for the
    /// same session and the caller is not signed out.
    /// </summary>
    /// <remarks>
    /// Worth recording. A single occurrence is ordinary; a run of them on one
    /// session is the shape a guessing attack makes.
    /// </remarks>
    RotatedWithinGrace,

    /// <summary>
    /// Already rotated, and long enough ago that the token must have been
    /// copied. The whole session ends — the legitimate holder signs in again
    /// with a password the attacker does not have.
    /// </summary>
    ReuseDetected,

    /// <summary>No such token.</summary>
    NotFound,

    /// <summary>This token's own lifetime ran out.</summary>
    Expired,

    /// <summary>The session was ended deliberately.</summary>
    Revoked,

    /// <summary>The sign-in reached its absolute ceiling. Sliding does not extend it.</summary>
    SessionExpired
}

/// <summary>The outcome, and what the caller needs in order to answer.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Issued">
/// The record now in force, present only for <see cref="ConsumeOutcome.Rotated"/>
/// and <see cref="ConsumeOutcome.RotatedWithinGrace"/>.
/// </param>
public readonly record struct ConsumeResult(ConsumeOutcome Outcome, RefreshTokenRecord? Issued)
{
    /// <summary>Whether the caller may hand out a new pair of tokens.</summary>
    public bool Succeeded =>
        Outcome is ConsumeOutcome.Rotated or ConsumeOutcome.RotatedWithinGrace;
}
