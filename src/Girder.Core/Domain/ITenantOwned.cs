using Girder.Core.Identity;

namespace Girder.Core.Domain;

/// <summary>
/// Markiert eine Entitaet als Eigentum einer Firma.
///
/// <para>
/// Mehrmandantenfaehigkeit ist damit pro ENTITAET einschaltbar, nicht pro
/// Anwendung. Genau das war die Anforderung: nicht fuer alle, sondern fuer die,
/// die es wirklich brauchen.
/// </para>
/// <para>
/// Firmendaten tragen den Marker: Stellenanzeigen, Team, Firmenprofil.
/// </para>
/// <para>
/// Personendaten tragen ihn NICHT: Profil, Lebenslauf, Portfolio,
/// Einwilligungen. Die sind nach dem Subjekt geschnitten und folgen der Person
/// ueber Arbeitgeberwechsel hinweg. Eine Einwilligung gehoert dem Menschen, der
/// sie gegeben hat - nicht der Firma, bei der er gerade arbeitet. Wuerde eine
/// solche Entitaet den Marker bekommen, verschwaende sie beim Firmenwechsel
/// oder waere fuer den neuen Arbeitgeber sichtbar; beides waere falsch.
/// </para>
/// <para>
/// Girder liefert dazu nur den Marker und die Filter-Erweiterung. Den
/// <c>DbContext</c> besitzt der jeweilige Dienst - so wie hier auch der Outbox-
/// und der Ereignisspeicher gebaut sind.
/// </para>
/// </summary>
public interface ITenantOwned
{
    /// <summary>
    /// Die besitzende Firma. Niemals <see cref="TenantId.None"/> - das ist
    /// zusaetzlich per Check-Constraint zu sichern, denn auf diesem Versprechen
    /// beruht, dass eine Privatperson keine Firmenzeile findet.
    /// </summary>
    TenantId Tenant { get; }
}
