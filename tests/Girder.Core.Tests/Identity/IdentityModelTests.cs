using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Girder.Core.Identity;

namespace Girder.Core.Tests.Identity;

[Trait("Category", "Unit")]
public class TypedIdTests
{
    [Fact]
    public void SubjectId_LehntLeerenGuidAb()
    {
        var act = () => new SubjectId(Guid.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TenantId_LehntLeerenGuidAb()
    {
        var act = () => new TenantId(Guid.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TenantId_None_IstDerEinzigeLeereWert()
    {
        TenantId.None.IsNone.Should().BeTrue();
        TenantId.New().IsNone.Should().BeFalse();
        TenantId.None.Should().NotBe(TenantId.New());
    }

    [Fact]
    public void Default_IstDasEinzigeSchlupfloch_UndErkennbar()
    {
        // C# laesst sich default(struct) nicht verbieten. Der Zustand ist
        // deshalb erkennbar gemacht, statt so zu tun, als gaebe es ihn nicht.
        default(SubjectId).IsEmpty.Should().BeTrue();
        default(TenantId).IsNone.Should().BeTrue();
    }

    [Fact]
    public void SubjectId_UndTenantId_SindVerschiedeneTypen()
    {
        // Der eigentliche Zweck: der Compiler bemerkt das Vertauschen. Zur
        // Laufzeit laesst sich nur belegen, dass keiner in den anderen passt.
        typeof(SubjectId).Should().NotBe(typeof(TenantId));
        typeof(SubjectId).IsAssignableFrom(typeof(TenantId)).Should().BeFalse();
        typeof(TenantId).IsAssignableFrom(typeof(SubjectId)).Should().BeFalse();
    }

    [Fact]
    public void Json_SerialisiertAlsSchlichteZeichenkette()
    {
        var subject = SubjectId.New();
        var tenant = TenantId.New();

        var subjectJson = JsonSerializer.Serialize(subject);
        var tenantJson = JsonSerializer.Serialize(tenant);

        subjectJson.Should().Be($"\"{subject.Value}\"");
        tenantJson.Should().Be($"\"{tenant.Value}\"");

        JsonSerializer.Deserialize<SubjectId>(subjectJson).Should().Be(subject);
        JsonSerializer.Deserialize<TenantId>(tenantJson).Should().Be(tenant);
    }

    [Fact]
    public void TryParse_LehntLeerenGuidAb()
    {
        SubjectId.TryParse(Guid.Empty.ToString(), out _).Should().BeFalse();
        TenantId.TryParse(Guid.Empty.ToString(), out _).Should().BeFalse();
        SubjectId.TryParse("kein guid", out _).Should().BeFalse();
        SubjectId.TryParse(null, out _).Should().BeFalse();
    }
}

[Trait("Category", "Unit")]
public class CapacityTests
{
    [Fact]
    public void ForCompany_OhneMandant_IstEinWiderspruch()
    {
        var act = () => new Capacity.ForCompany(TenantId.None);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*AsSelf*");
    }

    [Fact]
    public void Hierarchie_IstGeschlossen()
    {
        // Das ist der Test, der den Entwurf traegt: solange niemand von aussen
        // einen dritten Fall ergaenzen kann, ist jedes Mustern vollstaendig.
        var faelle = typeof(Capacity).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Capacity)))
            .ToList();

        faelle.Should().HaveCount(2);
        faelle.Should().OnlyContain(t => t.IsSealed, "kein Fall darf weiter abgeleitet werden");
        faelle.Should().OnlyContain(t => t.IsNested, "die Faelle gehoeren in die Basisklasse");

        // Der einzige echte Konstruktor ist privat. Der geschuetzte Kopier-
        // konstruktor entsteht automatisch bei jedem record und zaehlt nicht.
        var echteKonstruktoren = typeof(Capacity)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(c => c.GetParameters() is not [{ ParameterType.Name: nameof(Capacity) }])
            .ToList();

        echteKonstruktoren.Should().OnlyContain(c => c.IsPrivate);
    }

    [Fact]
    public void KeinFallTraegtEineRolle()
    {
        // ADR-0018: Das Token sagt, FUER WELCHE Firma jemand handelt, nie MIT
        // WELCHEM Recht. Stuende eine Rolle hier, waere sie faelschbar.
        var eigenschaften = typeof(Capacity).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Capacity)))
            .SelectMany(t => t.GetProperties())
            .Select(p => p.Name.ToLowerInvariant());

        eigenschaften.Should().NotContain(n =>
            n.Contains("role") || n.Contains("rolle") ||
            n.Contains("permission") || n.Contains("recht") ||
            n.Contains("claim") || n.Contains("scope"));
    }

    [Fact]
    public void AsSelf_HatWertgleichheit()
    {
        Capacity.AsSelf.Instance.Should().Be(Capacity.AsSelf.Instance);
        ((Capacity)Capacity.AsSelf.Instance).Should()
            .NotBe(new Capacity.ForCompany(TenantId.New()));
    }
}

[Trait("Category", "Unit")]
public class PrincipalTests
{
    [Fact]
    public void Person_HatKeinenMandanten_AberEineEigenschaft()
    {
        var subject = SubjectId.New();

        var principal = Principal.Person(subject);

        principal.Subject.Should().Be(subject);
        principal.Acting.Should().BeOfType<Capacity.AsSelf>();
        principal.TryGetTenant(out _).Should().BeFalse();
    }

    [Fact]
    public void Company_LiefertDenMandantenGarantiert()
    {
        var subject = SubjectId.New();
        var tenant = TenantId.New();

        var principal = Principal.Company(subject, tenant);

        principal.TryGetTenant(out var gefunden).Should().BeTrue();
        gefunden.Should().Be(tenant);

        // So liest es sich am Aufrufer - ohne ?., ohne !, ohne ?? throw.
        if (principal.Acting is not Capacity.ForCompany firma)
        {
            Assert.Fail("Muster haette treffen muessen.");
            return;
        }

        firma.Tenant.Should().Be(tenant);
    }

    [Fact]
    public void EinePersonVergleichtGegenNone_UndTrifftDamitKeineFirmenzeile()
    {
        // Die Sicherheitseigenschaft faellt aus dem Vergleich, nicht aus einem if:
        // keine gespeicherte Zeile traegt None, also findet eine Person nichts.
        var person = Principal.Person(SubjectId.New());
        var firma = Principal.Company(SubjectId.New(), TenantId.New());

        person.TenantForQueryFilter.Should().Be(TenantId.None);
        firma.TenantForQueryFilter.Should().NotBe(TenantId.None);

        var zeileEinerFirma = TenantId.New();
        (person.TenantForQueryFilter == zeileEinerFirma).Should().BeFalse();
    }

    [Fact]
    public void Principal_ErzwingtBeideFelder()
    {
        // required auf beiden Feldern - ein halber Prinzipal ist nicht baubar.
        var pflicht = typeof(Principal)
            .GetProperties()
            .Where(p => p.GetCustomAttributes()
                .Any(a => a.GetType().Name == "RequiredMemberAttribute"))
            .Select(p => p.Name);

        pflicht.Should().Contain(["Subject", "Acting"]);
    }
}
