using Girder.Infrastructure.Security.Authorization;

namespace Girder.Infrastructure.Tests.Security.Authorization;

[Trait("Category", "Unit")]
public class ResourceMapTests
{
    [Fact]
    public void EmptyMap_IdentifiesNothing()
    {
        ResourceMap.Empty.FromRouteParameter("jobId").Should().BeNull();
        ResourceMap.Empty.FromPathSegment("jobs").Should().BeNull();
        ResourceMap.Empty.IdParametersFor("Job").Should().BeEmpty();
    }

    [Fact]
    public void EmptyMap_StillKnowsTheGenericIdParameter()
    {
        // Generic id resolution has to keep working without any declaration.
        ResourceMap.Empty.AllIdParameters.Should().BeEquivalentTo([ResourceMap.GenericIdParameter]);
    }

    [Fact]
    public void RouteParametersResolveToTheirResource()
    {
        var map = new ResourceMapBuilder()
            .RouteParameter("Job", "jobId", "postingId")
            .Build();

        map.FromRouteParameter("jobId").Should().Be("Job");
        map.FromRouteParameter("postingId").Should().Be("Job");
        map.FromRouteParameter("companyId").Should().BeNull();
    }

    [Fact]
    public void RouteParametersAreCaseSensitive()
    {
        // Route parameter names appear verbatim in the route template.
        var map = new ResourceMapBuilder().RouteParameter("Job", "jobId").Build();

        map.FromRouteParameter("JobId").Should().BeNull();
    }

    [Fact]
    public void PathSegmentsAreCaseInsensitive()
    {
        // A URL path may arrive in any casing.
        var map = new ResourceMapBuilder().PathSegment("Job", "jobs").Build();

        map.FromPathSegment("jobs").Should().Be("Job");
        map.FromPathSegment("Jobs").Should().Be("Job");
        map.FromPathSegment("JOBS").Should().Be("Job");
    }

    [Fact]
    public void IdParametersKeepTheirOrderAndEndWithTheGenericOne()
    {
        var map = new ResourceMapBuilder()
            .IdParameters("Job", "jobId", "postingId")
            .Build();

        map.IdParametersFor("Job").Should().Equal(["jobId", "postingId", ResourceMap.GenericIdParameter]);
    }

    [Fact]
    public void IdParametersForAnUndeclaredResourceAreEmpty()
    {
        // Empty, not ["id"]: the caller then falls back to a generic scan rather
        // than assuming a parameter that may not exist.
        var map = new ResourceMapBuilder().IdParameters("Job", "jobId").Build();

        map.IdParametersFor("Company").Should().BeEmpty();
    }

    [Fact]
    public void IdParametersAreNotDuplicatedWhenIdIsListedExplicitly()
    {
        var map = new ResourceMapBuilder().IdParameters("Job", "jobId", "id").Build();

        map.IdParametersFor("Job").Should().Equal(["jobId", "id"]);
    }

    [Fact]
    public void AllIdParametersCollectsEveryDeclaredName()
    {
        var map = new ResourceMapBuilder()
            .IdParameters("Job", "jobId")
            .IdParameters("Company", "companyId")
            .Build();

        map.AllIdParameters.Should().BeEquivalentTo(["jobId", "companyId", ResourceMap.GenericIdParameter]);
    }

    [Fact]
    public void RepeatedDeclarationsAccumulate()
    {
        var map = new ResourceMapBuilder()
            .PathSegment("Job", "jobs")
            .PathSegment("Job", "postings")
            .Build();

        map.FromPathSegment("jobs").Should().Be("Job");
        map.FromPathSegment("postings").Should().Be("Job");
    }

    [Fact]
    public void DeclaringTheSameMappingTwiceIsHarmless()
    {
        var act = () => new ResourceMapBuilder()
            .PathSegment("Job", "jobs")
            .PathSegment("Job", "jobs")
            .Build();

        act.Should().NotThrow();
    }

    [Fact]
    public void OneNameCannotIdentifyTwoResources()
    {
        // Silently overwriting would make resolution depend on declaration order.
        var conflictingSegment = () => new ResourceMapBuilder()
            .PathSegment("Job", "items")
            .PathSegment("Company", "items");

        var conflictingParameter = () => new ResourceMapBuilder()
            .RouteParameter("Job", "itemId")
            .RouteParameter("Company", "itemId");

        conflictingSegment.Should().Throw<ArgumentException>().WithMessage("*already mapped*");
        conflictingParameter.Should().Throw<ArgumentException>().WithMessage("*already mapped*");
    }

    [Fact]
    public void BlankNamesAreRejected()
    {
        var blankResource = () => new ResourceMapBuilder().PathSegment("  ", "jobs");
        var blankSegment = () => new ResourceMapBuilder().PathSegment("Job", "  ");
        var blankParameter = () => new ResourceMapBuilder().RouteParameter("Job", "  ");
        var blankIdParameter = () => new ResourceMapBuilder().IdParameters("Job", "  ");

        blankResource.Should().Throw<ArgumentException>();
        blankSegment.Should().Throw<ArgumentException>();
        blankParameter.Should().Throw<ArgumentException>();
        blankIdParameter.Should().Throw<ArgumentException>();
    }
}
