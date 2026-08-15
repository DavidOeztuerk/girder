using Girder.Infrastructure.Sovereignty;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Girder.Infrastructure.Tests.Sovereignty;

[Trait("Category", "Unit")]
public class EgressPolicyTests
{
    [Fact]
    public void WithNothingDeclared_EverythingIsAllowed()
    {
        // Adding the package must not change behaviour on its own.
        var policy = new EgressPolicyBuilder().Build();

        policy.IsEnforcing.Should().BeFalse();
        policy.IsAllowed(new Uri("https://anything.example.com")).Should().BeTrue();
    }

    [Fact]
    public void DeclaredHostIsAllowed_AndEverythingElseIsNot()
    {
        var policy = new EgressPolicyBuilder().Allow("openbao.internal").Build();

        policy.IsEnforcing.Should().BeTrue();
        policy.IsAllowed(new Uri("https://openbao.internal/v1/secret/data/x")).Should().BeTrue();
        policy.IsAllowed(new Uri("https://telemetry.vendor.com")).Should().BeFalse();
    }

    [Fact]
    public void HostComparisonIgnoresCase()
    {
        var policy = new EgressPolicyBuilder().Allow("OpenBao.Internal").Build();

        policy.IsAllowed(new Uri("https://openbao.internal")).Should().BeTrue();
    }

    [Fact]
    public void SubdomainsAreAllowedOnlyWhenDeclared()
    {
        var policy = new EgressPolicyBuilder().AllowSubdomainsOf("example.eu").Build();

        policy.IsAllowed(new Uri("https://api.example.eu")).Should().BeTrue();
        policy.IsAllowed(new Uri("https://a.b.example.eu")).Should().BeTrue();
        policy.IsAllowed(new Uri("https://example.eu")).Should().BeFalse("the domain itself was not declared");
    }

    [Fact]
    public void SubdomainRuleDoesNotMatchASuffixOfAnotherName()
    {
        // "notexample.eu" ends with "example.eu" as a string but is a different domain.
        var policy = new EgressPolicyBuilder().AllowSubdomainsOf("example.eu").Build();

        policy.IsAllowed(new Uri("https://notexample.eu")).Should().BeFalse();
    }

    [Fact]
    public void LoopbackIsAllowedOnlyWhenDeclared()
    {
        var without = new EgressPolicyBuilder().Allow("openbao.internal").Build();
        var with = new EgressPolicyBuilder().Allow("openbao.internal").AllowLoopback().Build();

        without.IsAllowed(new Uri("http://localhost:4317")).Should().BeFalse();
        without.IsAllowed(new Uri("http://127.0.0.1:4317")).Should().BeFalse();

        with.IsAllowed(new Uri("http://localhost:4317")).Should().BeTrue();
        with.IsAllowed(new Uri("http://127.0.0.1:4317")).Should().BeTrue();
        with.IsAllowed(new Uri("http://[::1]:4317")).Should().BeTrue();
    }

    [Theory]
    [InlineData("http://10.0.0.5", true)]
    [InlineData("http://172.16.0.1", true)]
    [InlineData("http://172.31.255.254", true)]
    [InlineData("http://192.168.1.10", true)]
    [InlineData("http://172.32.0.1", false)]
    [InlineData("http://8.8.8.8", false)]
    public void PrivateNetworksAreRecognisedByRange(string url, bool expected)
    {
        var policy = new EgressPolicyBuilder().AllowPrivateNetworks().Build();

        policy.IsAllowed(new Uri(url)).Should().Be(expected);
    }

    [Fact]
    public void DeclaredHostsAreReportable()
    {
        var policy = new EgressPolicyBuilder()
            .Allow("openbao.internal")
            .AllowSubdomainsOf("example.eu")
            .AllowLoopback()
            .Build();

        policy.DeclaredHosts.Should().BeEquivalentTo(
            ["openbao.internal", "*.example.eu", "<loopback>"]);
    }

    [Fact]
    public void WildcardsInAllowAreRejectedWithAHint()
    {
        var act = () => new EgressPolicyBuilder().Allow("*.example.eu");

        act.Should().Throw<ArgumentException>().WithMessage("*AllowSubdomainsOf*");
    }
}

[Trait("Category", "Unit")]
public class EgressGuardHandlerTests
{
    private static HttpClient ClientWith(IEgressPolicy policy) =>
        new(new EgressGuardHandler(policy, NullLogger<EgressGuardHandler>.Instance)
        {
            InnerHandler = new NeverReachedHandler()
        });

    [Fact]
    public async Task AnUndeclaredHostIsRefusedBeforeTheCallLeaves()
    {
        var policy = new EgressPolicyBuilder().Allow("openbao.internal").Build();
        using var client = ClientWith(policy);

        var act = () => client.GetAsync("https://telemetry.vendor.com/collect");

        await act.Should().ThrowAsync<EgressDeniedException>()
            .WithMessage("*telemetry.vendor.com*");
    }

    [Fact]
    public async Task ADeclaredHostPassesThrough()
    {
        var policy = new EgressPolicyBuilder().Allow("openbao.internal").Build();
        using var client = ClientWith(policy);

        var act = () => client.GetAsync("https://openbao.internal/v1/sys/health");

        // Reaching the inner handler is the proof that the guard let it through.
        await act.Should().ThrowAsync<NotSupportedException>().WithMessage("*reached the network*");
    }

    [Fact]
    public void TheExceptionNamesWhatWasDeclared()
    {
        var policy = new EgressPolicyBuilder().Allow("a.internal", "b.internal").Build();

        var exception = new EgressDeniedException(new Uri("https://c.example.com"), policy.DeclaredHosts);

        exception.Message.Should().Contain("a.internal").And.Contain("b.internal");
        exception.Destination.Host.Should().Be("c.example.com");
    }

    /// <summary>Stands in for the network, and fails loudly if anything gets that far.</summary>
    private sealed class NeverReachedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("The request reached the network.");
    }
}

[Trait("Category", "Unit")]
public class EgressPolicyRegistrationTests
{
    [Fact]
    public void EveryFactoryClientGetsTheGuard()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient("anything");
        services.AddGirderEgressPolicy(p => p.Allow("openbao.internal"));

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("anything");

        var act = () => client.GetAsync("https://telemetry.vendor.com/collect");

        act.Should().ThrowAsync<EgressDeniedException>();
    }
}
