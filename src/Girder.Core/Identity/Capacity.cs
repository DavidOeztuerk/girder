namespace Girder.Core.Identity;

/// <summary>
/// In welcher Eigenschaft jemand gerade handelt.
///
/// <para>
/// Die Trennung ist der Kern: <see cref="Principal.Subject"/> sagt, WER handelt -
/// das aendert sich nie. Die Eigenschaft sagt, ALS WAS - und das wechselt.
/// Dieselbe Person handelt heute fuer sich, morgen fuer Firma A, uebermorgen
/// fuer Firma B. Ihr Profil, ihr Lebenslauf und ihre Einwilligungen folgen ihr
/// dabei, denn die haengen am Subjekt, nicht an der Firma.
/// </para>
/// <para>
/// Deshalb gibt es hier kein nullbares Mandantenfeld. Eine Privatperson ist
/// nicht "ein Firmenmitglied ohne Firma", sondern ein anderer Fall. Wer den
/// Mandanten braucht, muss den Fall pruefen - und bekommt ihn dann garantiert.
/// </para>
/// <para>
/// Die Hierarchie ist geschlossen: der Konstruktor ist privat, also kann
/// ausserhalb dieser Datei niemand einen dritten Fall ergaenzen. Sobald C# 15
/// echte Union-Typen liefert (GA November 2026), ist der Wechsel eine
/// Umbenennung und kein Neuentwurf.
/// </para>
/// </summary>
public abstract record Capacity
{
    private Capacity() { }

    /// <summary>
    /// Handelt fuer sich selbst. Kein Mandant - kein fehlender, sondern keiner.
    /// </summary>
    public sealed record AsSelf : Capacity
    {
        internal AsSelf() { }

        public static AsSelf Instance { get; } = new();

        public override string ToString() => "als Person";
    }

    /// <summary>
    /// Handelt fuer eine Firma. Der Mandant ist hier Pflicht, nicht Beiwerk.
    ///
    /// <para>
    /// Traegt bewusst KEINE Rolle. Das Token sagt, fuer welche Firma jemand
    /// handelt - nie, mit welchem Recht. Die Rolle wird pro Vorgang aus der
    /// Mitgliedschaft gelesen. Stuende sie hier, waere "Admin" faelschbar,
    /// sobald irgendwer ein Token ausstellt.
    /// </para>
    /// </summary>
    public sealed record ForCompany : Capacity
    {
        public ForCompany(TenantId tenant)
        {
            if (tenant.IsNone)
            {
                throw new ArgumentException(
                    "ForCompany ohne Mandant ist ein Widerspruch. Fuer 'handelt fuer "
                    + "keine Firma' gibt es Capacity.AsSelf.",
                    nameof(tenant));
            }

            Tenant = tenant;
        }

        public TenantId Tenant { get; }

        public override string ToString() => $"fuer Firma {Tenant}";
    }
}
