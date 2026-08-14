using System.Diagnostics.CodeAnalysis;

namespace Girder.Core.Identity;

/// <summary>
/// Zugriff auf den Prinzipal der laufenden Anfrage.
///
/// <para>
/// Hier ist <c>null</c> ausdruecklich RICHTIG, und der Unterschied zum
/// verworfenen <c>TenantId?</c> ist der ganze Punkt: eine anonyme Anfrage hat
/// keinen Prinzipal - das ist echte Abwesenheit. Eine Privatperson hat dagegen
/// sehr wohl eine Eigenschaft, naemlich <see cref="Capacity.AsSelf"/>. Das eine
/// ist "nichts da", das andere "etwas Bestimmtes". Nur das erste ist null.
/// </para>
/// </summary>
public interface ICurrentPrincipal
{
    /// <summary><c>null</c>, wenn die Anfrage nicht authentifiziert ist.</summary>
    Principal? Current { get; }
}

public static class CurrentPrincipalExtensions
{
    public static bool IsAuthenticated(this ICurrentPrincipal accessor) =>
        accessor.Current is not null;

    /// <summary>
    /// Der Prinzipal, oder eine Ausnahme. Fuer Stellen hinter
    /// <c>[Authorize]</c>, wo Anonymitaet ein Programmierfehler waere und kein
    /// Laufzeitfall.
    /// </summary>
    public static Principal Require(this ICurrentPrincipal accessor) =>
        accessor.Current ?? throw new InvalidOperationException(
            "Kein Prinzipal in dieser Anfrage. Fehlt [Authorize] oder die "
            + "Middleware, die das Token uebersetzt?");

    /// <summary>
    /// Der Mandant, wenn fuer eine Firma gehandelt wird. Anonym oder als Person
    /// gibt <c>false</c> - beides bedeutet fuer den Aufrufer dasselbe: kein
    /// Firmenzugriff.
    /// </summary>
    public static bool TryGetTenant(this ICurrentPrincipal accessor, out TenantId tenant)
    {
        if (accessor.Current is { } principal)
        {
            return principal.TryGetTenant(out tenant);
        }

        tenant = TenantId.None;
        return false;
    }

    public static bool TryGetSubject(this ICurrentPrincipal accessor, [NotNullWhen(true)] out SubjectId subject)
    {
        if (accessor.Current is { } principal)
        {
            subject = principal.Subject;
            return true;
        }

        subject = default;
        return false;
    }
}
