using Girder.Infrastructure.Communication;

namespace Girder.Infrastructure.Tests.Communication;

[Trait("Category", "Unit")]
public class ServiceEndpointInfoTests
{
    [Fact]
    public void ServiceEndpointInfo_Construction_SetsAllRequiredProperties()
    {
        var info = new ServiceEndpointInfo(
            Name: "GetUser",
            Path: "/api/users/{id}",
            Method: "GET",
            RequiresAuth: true
        );

        info.Name.Should().Be("GetUser");
        info.Path.Should().Be("/api/users/{id}");
        info.Method.Should().Be("GET");
        info.RequiresAuth.Should().BeTrue();
        info.Metadata.Should().BeNull();
    }

    [Fact]
    public void ServiceEndpointInfo_WithMetadata_SetsMetadata()
    {
        var metadata = new Dictionary<string, string> { ["version"] = "v1", ["tag"] = "users" };
        var info = new ServiceEndpointInfo(
            Name: "CreateUser",
            Path: "/api/users",
            Method: "POST",
            RequiresAuth: false,
            Metadata: metadata
        );

        info.Metadata.Should().ContainKey("version");
        info.Metadata!["version"].Should().Be("v1");
        info.Metadata.Should().ContainKey("tag");
    }

    [Fact]
    public void ServiceEndpointInfo_PublicEndpoint_RequiresAuthIsFalse()
    {
        var info = new ServiceEndpointInfo("Health", "/health", "GET", false);

        info.RequiresAuth.Should().BeFalse();
    }

    [Fact]
    public void ServiceEndpointInfo_IsRecord_EqualityBasedOnValues()
    {
        var info1 = new ServiceEndpointInfo("Test", "/test", "GET", true);
        var info2 = new ServiceEndpointInfo("Test", "/test", "GET", true);

        info1.Should().Be(info2);
    }

    [Fact]
    public void ServiceEndpointInfo_DifferentValues_AreNotEqual()
    {
        var info1 = new ServiceEndpointInfo("Test1", "/test", "GET", true);
        var info2 = new ServiceEndpointInfo("Test2", "/test", "GET", true);

        info1.Should().NotBe(info2);
    }

    [Fact]
    public void ServiceEndpointInfo_ToString_ContainsPropertyValues()
    {
        var info = new ServiceEndpointInfo("CreateInvoice", "/api/jobs", "POST", true);

        var str = info.ToString();

        str.Should().Contain("CreateInvoice");
    }
}
