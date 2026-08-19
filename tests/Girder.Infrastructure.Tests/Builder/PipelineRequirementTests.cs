using Girder.Abstractions.Caching;
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

        var compose = () => app.UseSharedInfrastructure(
            app.Environment, "test", pipeline => pipeline.UseHttpCaching());

        compose.Should().Throw<InvalidOperationException>()
            .WithMessage("*UseHttpCaching()*AddCaching()*");
    }

    [Fact]
    public void UseRateLimiting_without_a_store_provider_fails_at_composition()
    {
        var app = BuildApp(infrastructure => infrastructure.AddDistributedRateLimiting());

        var compose = () => app.UseSharedInfrastructure(
            app.Environment, "test", pipeline => pipeline.UseRateLimiting());

        compose.Should().Throw<InvalidOperationException>()
            .WithMessage("*IDistributedRateLimitStore*AddInMemoryCache*");
    }

    [Fact]
    public void UseSecurityHeaders_without_AddSecurityHeaders_fails_at_composition()
    {
        var app = BuildApp();

        var compose = () => app.UseSharedInfrastructure(
            app.Environment, "test", pipeline => pipeline.UseSecurityHeaders());

        compose.Should().Throw<InvalidOperationException>()
            .WithMessage("*AddSecurityHeaders()*");
    }

    [Fact]
    public void With_the_registration_present_composition_proceeds()
    {
        var app = BuildApp(infrastructure => infrastructure.AddCaching().AddSecurityHeaders());

        var compose = () => app.UseSharedInfrastructure(
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

        var compose = () => app.UseSharedInfrastructure(app.Environment, "test", pipeline =>
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
}
