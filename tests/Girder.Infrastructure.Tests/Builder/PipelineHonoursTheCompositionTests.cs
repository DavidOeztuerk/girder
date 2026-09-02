using Girder.Abstractions.Hosting;
using Girder.Infrastructure.Builder;
using Girder.Infrastructure.Builder.Modules;
using Girder.Infrastructure.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Girder.Infrastructure.Tests.Builder;

/// <summary>
/// The pipeline honours the same decision the service side recorded.
/// </summary>
/// <remarks>
/// <para>Before, <c>Without(module, reason)</c> only reached the container. The
/// chain called every step regardless, so leaving a module out either threw at
/// startup or forced the caller to write Girder's chain out themselves, minus
/// the lines they wanted gone. That copy is wrong the first time Girder adds a
/// step, and nothing says so — no build breaks, no test falls, the chain is
/// simply shorter.</para>
/// <para>These tests drive real requests through a real pipeline. Asserting that
/// the builder skipped something would only restate the implementation; what has
/// to be true is that the middleware is not in the request's way.</para>
/// </remarks>
[Trait("Category", "Unit")]
public class PipelineHonoursTheCompositionTests
{
    /// <summary>
    /// The shortest documented way to stand a service up: the default set, and
    /// the default chain with no lambda.
    /// </summary>
    /// <remarks>
    /// This is what used to die at startup with <c>UseRateLimiting() needs
    /// IDistributedRateLimitStore</c>.
    /// </remarks>
    [Fact]
    public async Task The_defaults_and_the_default_chain_start_a_service()
    {
        using var host = await Start(
            girder => girder.UseDefaults(),
            (app, env) => app.UseGirder(env, "test-service"));

        // The liveness probe sits ahead of authentication in the chain, so a 200
        // here says the whole pipeline composed and runs — which is the claim,
        // not what any one endpoint answers.
        var response = await host.GetTestClient().GetAsync(new Uri("http://localhost/health/live"));

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    /// <summary>
    /// A module left out is left out of the chain too — no startup failure over
    /// a provider the service said it did not want.
    /// </summary>
    [Fact]
    public async Task A_module_left_out_does_not_break_the_chain()
    {
        using var host = await Start(
            girder => girder
                .UseDefaults()
                .Without(GirderModule.RateLimiting, "braking happens at the entrance"),
            (app, env) => app.UseGirder(env, "test-service"));

        var response = await host.GetTestClient().GetAsync(new Uri("http://localhost/health/live"));

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    /// <summary>
    /// And it holds when the caller writes the step out by hand: the decision
    /// was recorded once, and naming the step again cannot undo it.
    /// </summary>
    [Fact]
    public async Task Writing_the_step_out_by_hand_does_not_undo_the_exclusion()
    {
        using var host = await Start(
            girder => girder
                .UseDefaults()
                .Without(GirderModule.RateLimiting, "braking happens at the entrance"),
            (app, env) => app.UseGirder(env, "test-service", pipeline => pipeline
                .UseRateLimiting()
                .UseHealthCheckEndpoints()));

        var response = await host.GetTestClient().GetAsync(new Uri("http://localhost/public"));

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    /// <summary>
    /// Permission enforcement is fail-closed, and a service with a public
    /// surface can leave exactly that out — while keeping the policy provider
    /// that answers <c>[RequirePermission]</c>.
    /// </summary>
    /// <remarks>
    /// The two used to be one module, so the choice was between an authorization
    /// system that answers nothing and a blanket 401 on every path nobody had
    /// declared public.
    /// </remarks>
    [Fact]
    public async Task Without_permission_enforcement_an_anonymous_call_gets_through()
    {
        using var host = await Start(
            girder => girder
                .UseDefaults()
                .Without(GirderModule.PermissionEnforcement, "we have a public surface"),
            (app, env) => app.UseGirder(env, "test-service"));

        var response = await host.GetTestClient().GetAsync(new Uri("http://localhost/public"));

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    /// <summary>
    /// The counter-probe. With the module in — the default — the same anonymous
    /// call is refused, so the test above measures the departure and not a
    /// pipeline that never ran.
    /// </summary>
    [Fact]
    public async Task With_permission_enforcement_the_same_call_is_refused()
    {
        using var host = await Start(
            girder => girder.UseDefaults(),
            (app, env) => app.UseGirder(env, "test-service"));

        var response = await host.GetTestClient().GetAsync(new Uri("http://localhost/public"));

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// <c>Authorization</c> survives that departure — it is what answers
    /// <c>[RequirePermission]</c>, and taking it away would make the framework
    /// refuse every endpoint carrying the attribute as "policy not found".
    /// </summary>
    [Fact]
    public async Task The_policy_provider_stays_when_only_the_middleware_goes()
    {
        using var host = await Start(
            girder => girder
                .UseDefaults()
                .Without(GirderModule.PermissionEnforcement, "we have a public surface"),
            (app, env) => app.UseGirder(env, "test-service"));

        var composition = host.Services.GetRequiredService<GirderComposition>();

        composition.Included.Should().Contain(GirderModule.Authorization);
        composition.Excluded.Should().ContainKey(GirderModule.PermissionEnforcement);
    }

    /// <summary>
    /// <c>HttpResponseCaching</c> is not in the default set, so the step for it
    /// stands in the default chain and does nothing — the chain and the module
    /// set used to contradict each other here.
    /// </summary>
    [Fact]
    public async Task A_step_whose_module_is_not_a_default_does_not_demand_its_provider()
    {
        using var host = await Start(
            girder => girder.UseDefaults(),
            (app, env) => app.UseGirder(env, "test-service"));

        host.Services.GetService<Girder.Infrastructure.Caching.Http.ICachePolicyProvider>()
            .Should().BeNull("the module is not in the default set");
    }

    /// <summary>
    /// With no composition in the container — the older
    /// <c>AddSharedInfrastructure</c> path — nothing is skipped. An absent record
    /// is not a record saying no.
    /// </summary>
    [Fact]
    public async Task Without_a_composition_nothing_is_skipped()
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services => services.AddLogging())
                .Configure(app =>
                {
                    app.UseGirder(
                        app.ApplicationServices.GetRequiredService<IHostEnvironment>(),
                        "test-service",
                        pipeline => pipeline.UseCorrelationId());

                    app.Run(http => http.Response.WriteAsync("up"));
                }))
            .StartAsync();

        var response = await host.GetTestClient().GetAsync(new Uri("http://localhost/"));

        response.Headers.Should().ContainKey("X-Correlation-ID");
    }

    private static async Task<IHost> Start(
        Action<GirderBuilder> configure,
        Action<IApplicationBuilder, IHostEnvironment> pipeline) =>
        await new HostBuilder()
            .UseDefaultServiceProvider(options =>
            {
                options.ValidateOnBuild = true;
                options.ValidateScopes = true;
            })
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureAppConfiguration(config => config.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["JwtSettings:Issuer"] = "test",
                        ["JwtSettings:Audience"] = "test",
                        ["JwtSettings:Secret"] = "a-probe-secret-long-enough-for-hmac-sha256!!"
                    }))
                .ConfigureServices((context, services) =>
                    services.AddGirder(
                        context.Configuration,
                        context.HostingEnvironment,
                        "test-service",
                        girder =>
                        {
                            configure(girder);

                            // Which scheme establishes a caller is the service's
                            // call, not the module set's — so a chain with
                            // UseAuth() in it needs this line the way a real
                            // service has it.
                            girder.UseJwt(jwt => jwt.FromSharedSecret());
                        }))
                .ConfigureServices(services => services.AddRouting())
                .Configure((context, app) =>
                {
                    pipeline(app, context.HostingEnvironment);
                    app.Run(http => http.Response.WriteAsync("up"));
                }))
            .StartAsync();
}
