using Girder.Abstractions.Caching;
using Girder.InMemory.Security;
using Girder.Infrastructure.Builder;
using Girder.Infrastructure.Builder.Modules;
using Girder.Infrastructure.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Girder.Infrastructure.Tests.Builder;

/// <summary>
/// A pipeline step whose registration is missing must say so while the pipeline
/// is being composed.
/// </summary>
/// <remarks>
/// Middleware is constructed on the first request that reaches it, so the
/// failure used to arrive in production as a 500 naming a Girder-internal type
/// the reader never wrote. Composition is the moment both halves are known.
/// </remarks>
[Trait("Category", "Unit")]
public class PipelineRequirementTests
{
    private static WebApplication BuildApp(Action<InfrastructureBuilder>? modules = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSharedInfrastructure(
            builder.Configuration, builder.Environment, "test", modules ?? (_ => { }));
        return builder.Build();
    }

    [Fact]
    public void UseHttpCaching_without_AddCaching_fails_at_composition()
    {
        var app = BuildApp();

        var compose = () => app.UseGirder(
            app.Environment, "test", pipeline => pipeline.UseHttpCaching());

        compose.Should().Throw<InvalidOperationException>()
            .WithMessage("*UseHttpCaching()*AddCaching()*");
    }

    /// <summary>
    /// The module brings a counter, so the step composes.
    /// </summary>
    /// <remarks>
    /// It used to register none, which made rate limiting the one thing in the
    /// default set that could not run: the step refused to compose, and the
    /// shortest documented way to stand a service up died at startup. Anyone who
    /// worked around it by dropping the step ended up with no rate limiting at
    /// all.
    /// </remarks>
    [Fact]
    public void UseRateLimiting_composes_because_the_module_brings_a_counter()
    {
        var app = BuildApp(infrastructure => infrastructure.AddDistributedRateLimiting());

        var compose = () => app.UseGirder(
            app.Environment, "test", pipeline => pipeline.UseRateLimiting());

        compose.Should().NotThrow();
    }

    /// <summary>
    /// And without the module at all it still names the call that is missing.
    /// </summary>
    [Fact]
    public void UseRateLimiting_without_the_module_names_the_remedy()
    {
        var app = BuildApp();

        var compose = () => app.UseGirder(
            app.Environment, "test", pipeline => pipeline.UseRateLimiting());

        compose.Should().Throw<InvalidOperationException>()
            .WithMessage("*IDistributedRateLimitStore*AddInMemoryCache*");
    }

    [Fact]
    public void UseSecurityHeaders_without_AddSecurityHeaders_fails_at_composition()
    {
        var app = BuildApp();

        var compose = () => app.UseGirder(
            app.Environment, "test", pipeline => pipeline.UseSecurityHeaders());

        compose.Should().Throw<InvalidOperationException>()
            .WithMessage("*AddSecurityHeaders()*");
    }

    [Fact]
    public void With_the_registration_present_composition_proceeds()
    {
        var app = BuildApp(infrastructure => infrastructure.AddCaching().AddSecurityHeaders());

        var compose = () => app.UseGirder(
            app.Environment, "test",
            pipeline => pipeline.UseHttpCaching().UseSecurityHeaders());

        compose.Should().NotThrow();
    }

    /// <summary>
    /// Steps that need nothing beyond the framework stay usable on their own.
    /// </summary>
    [Theory]
    [InlineData("UseCorrelationId")]
    [InlineData("UseExceptionHandling")]
    [InlineData("UseHealthCheckEndpoints")]
    public void A_step_without_requirements_composes_alone(string step)
    {
        var app = BuildApp(infrastructure => infrastructure.AddHealthChecks());

        var compose = () => app.UseGirder(app.Environment, "test", pipeline =>
        {
            switch (step)
            {
                case "UseCorrelationId": pipeline.UseCorrelationId(); break;
                case "UseExceptionHandling": pipeline.UseExceptionHandling(); break;
                case "UseHealthCheckEndpoints": pipeline.UseHealthCheckEndpoints(); break;
                default: throw new ArgumentOutOfRangeException(nameof(step), step, null);
            }
        });

        compose.Should().NotThrow();
    }

    /// <summary>
    /// Token revocation belongs on the same builder as every other step.
    /// </summary>
    /// <remarks>
    /// It was reachable only as a bare <c>IApplicationBuilder</c> extension, so
    /// a service composing through the pipeline builder had to break out of it
    /// for one line.
    /// </remarks>
    [Fact]
    public void UseTokenRevocation_without_an_evaluator_fails_at_composition()
    {
        var app = BuildApp();

        var compose = () => app.UseGirder(
            app.Environment, "test", pipeline => pipeline.UseTokenRevocation());

        compose.Should().Throw<InvalidOperationException>()
            .WithMessage("*ITokenRevocationEvaluator*");
    }

    [Fact]
    public void UseTokenRevocation_with_a_store_composes()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSharedInfrastructure(
            builder.Configuration, builder.Environment, "test", _ => { });
        builder.Services.AddInMemoryTokenRevocation();
        var app = builder.Build();

        var compose = () => app.UseGirder(
            app.Environment, "test", pipeline => pipeline.UseTokenRevocation());

        compose.Should().NotThrow();
    }
}
