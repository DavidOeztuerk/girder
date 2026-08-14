using Girder.Core.Domain;
using Girder.Core.Identity;
using Girder.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Girder.Infrastructure.Tests.Data;

[Trait("Category", "Unit")]
public sealed class TenantFilterTests : IDisposable
{
    private readonly SqliteConnection _connection;

    private readonly TenantId _acme = TenantId.New();
    private readonly TenantId _globex = TenantId.New();
    private readonly SubjectId _person = SubjectId.New();

    public TenantFilterTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var seed = NewContext(Principal.Company(_person, _acme));
        seed.Database.EnsureCreated();
        seed.Jobs.AddRange(
            new Job { Id = 1, Title = "Acme role", Tenant = _acme },
            new Job { Id = 2, Title = "Globex role", Tenant = _globex });
        seed.Notes.AddRange(
            new Note { Id = 1, Text = "mine", Subject = _person },
            new Note { Id = 2, Text = "someone else", Subject = SubjectId.New() });
        seed.SaveChanges();
    }

    private TestContext NewContext(Principal? principal) =>
        new(new DbContextOptionsBuilder<TestContext>().UseSqlite(_connection).Options, principal);

    [Fact]
    public void CompanyCaller_SeesOnlyItsOwnRows()
    {
        using var context = NewContext(Principal.Company(_person, _acme));

        var jobs = context.Jobs.ToList();

        jobs.Should().ContainSingle().Which.Title.Should().Be("Acme role");
    }

    [Fact]
    public void OtherCompany_SeesOnlyItsOwnRows()
    {
        using var context = NewContext(Principal.Company(_person, _globex));

        context.Jobs.Select(j => j.Title).Should().BeEquivalentTo(["Globex role"]);
    }

    [Fact]
    public void PersonCaller_SeesNoTenantOwnedRows()
    {
        using var context = NewContext(Principal.Person(_person));

        context.Jobs.Should().BeEmpty();
    }

    [Fact]
    public void AnonymousCaller_SeesNoTenantOwnedRows()
    {
        using var context = NewContext(principal: null);

        context.Jobs.Should().BeEmpty();
    }

    [Fact]
    public void EntitiesWithoutTheMarker_AreNotFiltered()
    {
        // Notes are keyed by subject, so a company caller must still see all of
        // them; the tenant filter has no business touching personal data.
        using var context = NewContext(Principal.Company(_person, _acme));

        context.Notes.Should().HaveCount(2);
    }

    [Fact]
    public void FilterIsNamed_SoItCanBeDisabledOnItsOwn()
    {
        using var context = NewContext(Principal.Person(_person));

        var all = context.Jobs
            .IgnoreQueryFilters([TenantFilterExtensions.TenantFilterName])
            .ToList();

        all.Should().HaveCount(2);
    }

    [Fact]
    public void NamedFilterCoexistsWithAnotherFilterOnTheSameEntity()
    {
        using var seed = NewContext(Principal.Company(_person, _acme));
        seed.Jobs.Add(new Job { Id = 3, Title = "Acme archived", Tenant = _acme, IsDeleted = true });
        seed.SaveChanges();

        using var context = NewContext(Principal.Company(_person, _acme));

        context.Jobs.Should().ContainSingle().Which.Title.Should().Be("Acme role");

        context.Jobs
            .IgnoreQueryFilters(["SoftDeletion"])
            .Should().HaveCount(2);
    }

    public void Dispose() => _connection.Dispose();

    private sealed class Job : ITenantOwned
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public TenantId Tenant { get; set; }
        public bool IsDeleted { get; set; }
    }

    private sealed class Note
    {
        public int Id { get; set; }
        public string Text { get; set; } = string.Empty;
        public SubjectId Subject { get; set; }
    }

    private sealed class TestContext : DbContext
    {
        private readonly Principal? _principal;

        public TestContext(DbContextOptions<TestContext> options, Principal? principal)
            : base(options) => _principal = principal;

        public DbSet<Job> Jobs => Set<Job>();

        public DbSet<Note> Notes => Set<Note>();

        // Read through a context member so EF re-evaluates it per query.
        private TenantId CurrentTenant => _principal?.TenantForQueryFilter ?? TenantId.None;

        protected override void ConfigureConventions(ModelConfigurationBuilder builder) =>
            builder.AddGirderIdConverters();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Job>().HasQueryFilter("SoftDeletion", j => !j.IsDeleted);
            modelBuilder.ApplyTenantFilters(() => CurrentTenant);
        }
    }
}
