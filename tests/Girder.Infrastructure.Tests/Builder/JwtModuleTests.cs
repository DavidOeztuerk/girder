using Girder.Abstractions.Security;
using Girder.InMemory.Security;
using Girder.Infrastructure.Builder;
using Girder.Infrastructure.Builder.Modules;
using Girder.Infrastructure.Extensions;
using Girder.Core.Identity;
using Girder.Infrastructure.Security;
using Girder.Infrastructure.Security.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Girder.Infrastructure.Tests.Builder;

/// <summary>
/// A service that turns on JWT authentication must be able to issue tokens as
/// well as verify them.
/// </summary>
/// <remarks>
/// Both sides read the same <c>JwtSettings</c> and sign with the same key, so
/// they belong to one module: split across two, a service can mint tokens it
/// then refuses itself.
/// </remarks>
[Trait("Category", "Unit")]
public class JwtModuleTests
{
    private static readonly Dictionary<string, string?> Settings = new()
    {
        ["JwtSettings:Secret"] = "a-test-secret-that-is-long-enough-to-sign",
        ["JwtSettings:Issuer"] = "girder-tests",
        ["JwtSettings:Audience"] = "girder-tests"
    };

    private static WebApplication BuildApp(Action<IServiceCollection>? providers = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(Settings);
        builder.Services.AddSharedInfrastructure(
            builder.Configuration, builder.Environment, "test",
            infrastructure => infrastructure.AddJwtAuthentication().AddPrincipal());
        providers?.Invoke(builder.Services);
        return builder.Build();
    }

    private static Action ConfigurePipeline(WebApplication app) => () =>
    {
        foreach (var filter in app.Services.GetServices<IStartupFilter>())
        {
            filter.Configure(_ => { })(app);
        }
    };

    [Fact]
    public void The_module_provides_the_service_that_issues_tokens()
    {
        var app = BuildApp(services => services.AddInMemoryTokenRevocation());

        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetService<IJwtService>().Should().NotBeNull();
    }

    /// <summary>
    /// A service that registers no revocation store still issues and verifies.
    /// </summary>
    /// <remarks>
    /// Revocation used to be demanded here, so the first thing a developer met
    /// was a decision about Redis — before issuing a single token. It is the
    /// upgrade for deployments that need the window closed to zero, not the
    /// entry price. Sessions end through the refresh-token store.
    /// </remarks>
    [Fact]
    public void Without_a_revocation_store_startup_proceeds()
    {
        var app = BuildApp();

        ConfigurePipeline(app).Should().NotThrow();
    }

    /// <summary>
    /// The module wires issuing and verifying to the same settings, so a token
    /// this service mints is one this service accepts.
    /// </summary>
    [Fact]
    public async Task A_token_this_service_issues_is_one_it_accepts()
    {
        var subject = SubjectId.New();
        var app = BuildApp(services => services.AddInMemoryTokenRevocation());
        using var scope = app.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtService>();

        var issued = await jwt.GenerateTokenAsync(new UserClaims
        {
            UserId = subject.ToString(),
            Email = "someone@example.test"
        });

        var claims = await jwt.ValidateTokenAsync(issued.AccessToken);
        var principal = scope.ServiceProvider.GetRequiredService<IPrincipalFactory>()
            .Create(claims!).Principal;

        principal!.Subject.Should().Be(subject);
    }
}
