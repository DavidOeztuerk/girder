using Girder.Abstractions.Security.Sessions;
using Microsoft.Extensions.DependencyInjection;

namespace Girder.InMemory.Sessions;

public static class InMemorySessionRegistration
{
    /// <summary>
    /// Keeps refresh tokens in this process.
    /// </summary>
    /// <remarks>
    /// Enough to run on the first day without deciding where anything lives.
    /// A restart signs everyone out and a second instance shares nothing, so
    /// move to the database the application already runs before either matters.
    /// </remarks>
    public static IServiceCollection AddInMemoryRefreshTokens(this IServiceCollection services) =>
        services.AddSingleton<IRefreshTokenStore, InMemoryRefreshTokenStore>();
}
