using Girder.Infrastructure.Sovereignty;
using Microsoft.Extensions.Configuration;

namespace Girder.Infrastructure.Tests.Sovereignty;

[Trait("Category", "Unit")]
public class HostJurisdictionTests
{
    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("10.1.2.3")]
    [InlineData("192.168.0.9")]
    [InlineData("openbao.internal")]
    [InlineData("postgres.svc.cluster.local")]
    [InlineData("valkey")]
    public void InfrastructureYouRunYourselfIsRecognised(string host)
    {
        HostJurisdiction.Classify(host).Jurisdiction.Should().Be(Jurisdiction.SelfHosted);
    }

    [Theory]
    [InlineData("my-cache.abc.eu-central-1.cache.amazonaws.com")]
    [InlineData("mystore.vault.azure.com")]
    [InlineData("storage.googleapis.com")]
    [InlineData("app.datadoghq.com")]
    public void ThirdCountryProvidersAreRecognisedEvenOnEuropeanEndpoints(string host)
    {
        // A region in Frankfurt does not change who operates the service or
        // which law reaches it.
        var (jurisdiction, note) = HostJurisdiction.Classify(host);

        jurisdiction.Should().Be(Jurisdiction.ThirdCountryProvider);
        note.Should().Contain("third-country");
    }

    [Theory]
    [InlineData("db.example.eu")]
    [InlineData("secrets.some-provider.de")]
    [InlineData("8.8.8.8")]
    public void AnythingElseIsUndeterminedRatherThanAssumedSovereign(string host)
    {
        // Not matching the list is not evidence of anything. Saying so is the
        // difference between a report and a rubber stamp.
        HostJurisdiction.Classify(host).Jurisdiction.Should().Be(Jurisdiction.Undetermined);
    }

    [Fact]
    public void NoHostIsUndetermined()
    {
        HostJurisdiction.Classify(null).Jurisdiction.Should().Be(Jurisdiction.Undetermined);
        HostJurisdiction.Classify("  ").Jurisdiction.Should().Be(Jurisdiction.Undetermined);
    }
}

[Trait("Category", "Unit")]
public class SovereigntyReportTests
{
    private static SovereigntyReport ReportFor(
        Dictionary<string, string?> configuration,
        IEgressPolicy? egress = null,
        params DeclaredDependency[] declared) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(configuration).Build(),
            egress ?? EgressPolicy.Unrestricted,
            declared);

    [Fact]
    public void ConnectionStringsArePickedUpWithoutBeingDeclared()
    {
        var report = ReportFor(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=postgres.internal;Database=app;Username=u;Password=p",
            ["ConnectionStrings:Valkey"] = "valkey.internal:6379"
        });

        var assessment = report.Assess();

        assessment.Dependencies.Select(d => d.Name).Should().BeEquivalentTo(["Postgres", "Valkey"]);
        assessment.Dependencies.Should().OnlyContain(d => d.Jurisdiction == Jurisdiction.SelfHosted);
        assessment.NoKnownThirdCountryDependency.Should().BeTrue();
    }

    [Fact]
    public void CredentialsNeverReachTheReport()
    {
        // The report is something people paste into tickets.
        var report = ReportFor(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=db.internal;Username=admin;Password=hunter2"
        });

        var assessment = report.Assess();

        assessment.Dependencies.Single().Host.Should().Be("db.internal");
        assessment.Dependencies.Single().Host.Should().NotContain("hunter2");
    }

    [Fact]
    public void AThirdCountryDependencyIsNamed()
    {
        var report = ReportFor(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Cache"] = "my.eu-central-1.cache.amazonaws.com:6379"
        });

        var assessment = report.Assess();

        assessment.NoKnownThirdCountryDependency.Should().BeFalse();
        assessment.ThirdCountryDependencies.Single().Name.Should().Be("Cache");
    }

    [Fact]
    public void DeclaredDependenciesAppearBesideConnectionStrings()
    {
        var report = ReportFor(
            new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = "Host=db.internal" },
            egress: null,
            new DeclaredDependency("Secrets", "https://openbao.internal:8200"),
            new DeclaredDependency("Telemetry", "http://localhost:4317"));

        var assessment = report.Assess();

        assessment.Dependencies.Select(d => d.Name)
            .Should().BeEquivalentTo(["Postgres", "Secrets", "Telemetry"]);
    }

    [Fact]
    public void TheEgressPolicyIsPartOfThePicture()
    {
        var egress = new EgressPolicyBuilder().Allow("openbao.internal").Build();

        var assessment = ReportFor(new Dictionary<string, string?>(), egress).Assess();

        assessment.EgressIsEnforced.Should().BeTrue();
        assessment.DeclaredEgressHosts.Should().BeEquivalentTo(["openbao.internal"]);
    }

    [Fact]
    public void WithoutAnEgressPolicyTheReportSaysSo()
    {
        var assessment = ReportFor(new Dictionary<string, string?>()).Assess();

        assessment.EgressIsEnforced.Should().BeFalse();
    }

    [Theory]
    [InlineData("https://openbao.internal:8200/v1", "openbao.internal")]
    [InlineData("Host=db.internal;Port=5432", "db.internal")]
    [InlineData("Server=sql.internal,1433", "sql.internal")]
    [InlineData("valkey.internal:6379,abortConnect=false", "valkey.internal")]
    [InlineData("Data Source=oracle.internal", "oracle.internal")]
    public void HostsAreExtractedFromTheUsualShapes(string value, string expected)
    {
        SovereigntyReport.ExtractHost(value).Should().Be(expected);
    }
}
