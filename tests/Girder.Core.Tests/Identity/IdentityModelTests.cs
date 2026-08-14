using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Girder.Core.Identity;

namespace Girder.Core.Tests.Identity;

[Trait("Category", "Unit")]
public class TypedIdTests
{
    [Fact]
    public void SubjectId_RejectsEmptyGuid()
    {
        var act = () => new SubjectId(Guid.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TenantId_RejectsEmptyGuid()
    {
        var act = () => new TenantId(Guid.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TenantId_None_IsTheOnlyEmptyValue()
    {
        TenantId.None.IsNone.Should().BeTrue();
        TenantId.New().IsNone.Should().BeFalse();
        TenantId.None.Should().NotBe(TenantId.New());
    }

    [Fact]
    public void Default_IsTheOnlyWayToObtainAnEmptyId_AndIsDetectable()
    {
        default(SubjectId).IsEmpty.Should().BeTrue();
        default(TenantId).IsNone.Should().BeTrue();
    }

    [Fact]
    public void SubjectId_AndTenantId_AreNotInterchangeable()
    {
        typeof(SubjectId).Should().NotBe(typeof(TenantId));
        typeof(SubjectId).IsAssignableFrom(typeof(TenantId)).Should().BeFalse();
        typeof(TenantId).IsAssignableFrom(typeof(SubjectId)).Should().BeFalse();
    }

    [Fact]
    public void Ids_SerializeAsPlainStrings()
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
    public void TryParse_RejectsEmptyGuidAndGarbage()
    {
        SubjectId.TryParse(Guid.Empty.ToString(), out _).Should().BeFalse();
        TenantId.TryParse(Guid.Empty.ToString(), out _).Should().BeFalse();
        SubjectId.TryParse("not a guid", out _).Should().BeFalse();
        SubjectId.TryParse(null, out _).Should().BeFalse();
    }
}

[Trait("Category", "Unit")]
public class CapacityTests
{
    [Fact]
    public void ForCompany_WithoutTenant_IsRejected()
    {
        var act = () => new Capacity.ForCompany(TenantId.None);

        act.Should().Throw<ArgumentException>().WithMessage("*AsSelf*");
    }

    [Fact]
    public void Hierarchy_IsClosed()
    {
        // Exhaustive pattern matching over Capacity depends on this.
        var cases = typeof(Capacity).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Capacity)))
            .ToList();

        cases.Should().HaveCount(2);
        cases.Should().OnlyContain(t => t.IsSealed);
        cases.Should().OnlyContain(t => t.IsNested);

        // Records emit a protected copy constructor; it is not a real case.
        var declaredConstructors = typeof(Capacity)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(c => c.GetParameters() is not [{ ParameterType.Name: nameof(Capacity) }])
            .ToList();

        declaredConstructors.Should().OnlyContain(c => c.IsPrivate);
    }

    [Fact]
    public void NoCaseCarriesARole()
    {
        // A token states which company a caller acts for, never with what rights.
        var properties = typeof(Capacity).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Capacity)))
            .SelectMany(t => t.GetProperties())
            .Select(p => p.Name.ToLowerInvariant());

        properties.Should().NotContain(n =>
            n.Contains("role") || n.Contains("permission") ||
            n.Contains("claim") || n.Contains("scope"));
    }

    [Fact]
    public void AsSelf_HasValueEquality()
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
    public void Person_HasNoTenantButStillHasACapacity()
    {
        var subject = SubjectId.New();

        var principal = Principal.Person(subject);

        principal.Subject.Should().Be(subject);
        principal.Acting.Should().BeOfType<Capacity.AsSelf>();
        principal.TryGetTenant(out _).Should().BeFalse();
    }

    [Fact]
    public void Company_ExposesTheTenantThroughPatternMatching()
    {
        var subject = SubjectId.New();
        var tenant = TenantId.New();

        var principal = Principal.Company(subject, tenant);

        principal.TryGetTenant(out var found).Should().BeTrue();
        found.Should().Be(tenant);

        if (principal.Acting is not Capacity.ForCompany company)
        {
            Assert.Fail("Pattern should have matched.");
            return;
        }

        company.Tenant.Should().Be(tenant);
    }

    [Fact]
    public void PersonComparesAsNone_SoMatchesNoTenantOwnedRow()
    {
        var person = Principal.Person(SubjectId.New());
        var company = Principal.Company(SubjectId.New(), TenantId.New());

        person.TenantForQueryFilter.Should().Be(TenantId.None);
        company.TenantForQueryFilter.Should().NotBe(TenantId.None);

        var someStoredRow = TenantId.New();
        (person.TenantForQueryFilter == someStoredRow).Should().BeFalse();
    }

    [Fact]
    public void Principal_RequiresBothSubjectAndCapacity()
    {
        var required = typeof(Principal)
            .GetProperties()
            .Where(p => p.GetCustomAttributes()
                .Any(a => a.GetType().Name == "RequiredMemberAttribute"))
            .Select(p => p.Name);

        required.Should().Contain(["Subject", "Acting"]);
    }
}
