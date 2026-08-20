using Girder.Infrastructure.Security.Sessions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Girder.Infrastructure.Builder.Modules;

public static class TokenSessionModule
{
    /// <summary>
    /// Adds signing in, refreshing and signing out over refresh tokens.
    /// </summary>
    /// <remarks>
    /// Named for tokens rather than <c>AddSessions()</c>, which would sit
    /// beside ASP.NET Core's own <c>AddSession()</c> for cookie state and be
    /// mistaken for it.
    /// <para>
    /// Needs somewhere to keep the tokens. <c>AddInMemoryRefreshTokens()</c> to
    /// start, or <c>AddEntityFrameworkRefreshTokens&lt;TContext&gt;()</c> for the
    /// database the application already runs — this needs no server of its own,
    /// and ending a session is a row, not a deployment.
    /// </para>
    /// </remarks>
    public static InfrastructureBuilder AddTokenSessions(this InfrastructureBuilder builder)
    {
        builder.RequiresProvider<Girder.Abstractions.Security.Sessions.IRefreshTokenStore>(
            "AddTokenSessions()",
            "AddInMemoryRefreshTokens() or AddEntityFrameworkRefreshTokens<TContext>()");

        builder.Services.Configure<TokenSessionOptions>(
            builder.Configuration.GetSection(TokenSessionOptions.SectionName));

        builder.Services.TryAddSingletonTimeProvider();
        builder.Services.AddScoped<ITokenSessionService, TokenSessionService>();

        return builder;
    }

    private static void TryAddSingletonTimeProvider(this IServiceCollection services)
    {
        if (services.All(descriptor => descriptor.ServiceType != typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
    }
}
