namespace Girder.Core.Identity;

/// <summary>
/// Wer eine Anfrage ausloest, und in welcher Eigenschaft.
///
/// <para>
/// Beide Felder sind Pflicht. Es gibt keinen Prinzipal ohne Subjekt und keinen
/// ohne Eigenschaft - der Zustand "irgendwas fehlt" existiert nicht.
/// </para>
/// <para>
/// Wird einmal an der Middleware-Grenze aus dem geprueften Token gebaut. Danach
/// fragt niemand mehr Claims ab: ein zweiter Leseweg ist ein zweiter
/// Vertrauensweg, und einer davon ist irgendwann falsch.
/// </para>
/// </summary>
public sealed record Principal
{
    public required SubjectId Subject { get; init; }

    public required Capacity Acting { get; init; }

    /// <summary>Handelt fuer sich selbst.</summary>
    public static Principal Person(SubjectId subject) =>
        new() { Subject = subject, Acting = Capacity.AsSelf.Instance };

    /// <summary>Handelt fuer eine Firma.</summary>
    public static Principal Company(SubjectId subject, TenantId tenant) =>
        new() { Subject = subject, Acting = new Capacity.ForCompany(tenant) };

    /// <summary>
    /// Der Mandant, wenn fuer eine Firma gehandelt wird - sonst
    /// <see cref="TenantId.None"/>.
    ///
    /// <para>
    /// AUSSCHLIESSLICH fuer die Datenbankgrenze gedacht, wo ein Query-Filter
    /// einen totalen Wert zum Vergleichen braucht. In Fachlogik hat das hier
    /// nichts verloren - dort wird auf <see cref="Capacity.ForCompany"/>
    /// gemustert, damit der Compiler den anderen Fall einfordert.
    /// </para>
    /// </summary>
    public TenantId TenantForQueryFilter =>
        Acting is Capacity.ForCompany company ? company.Tenant : TenantId.None;

    public bool TryGetTenant(out TenantId tenant)
    {
        if (Acting is Capacity.ForCompany company)
        {
            tenant = company.Tenant;
            return true;
        }

        tenant = TenantId.None;
        return false;
    }

    public override string ToString() => $"{Subject} ({Acting})";
}
