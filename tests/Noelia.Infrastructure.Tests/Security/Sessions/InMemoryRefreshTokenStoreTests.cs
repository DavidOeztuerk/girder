using Noelia.Abstractions.Security.Sessions;
using Noelia.Core.Identity;
using Noelia.InMemory.Sessions;

namespace Noelia.Infrastructure.Tests.Security.Sessions;

/// <summary>The conformance suite against the in-process store.</summary>
[Trait("Category", "Unit")]
public class InMemoryRefreshTokenStoreTests : RefreshTokenStoreConformance
{
    private readonly InMemoryRefreshTokenStore _store = new();

    protected override IRefreshTokenStore Store => _store;

    protected override Task<int> CountOpenAsync(SessionId session) =>
        Task.FromResult(_store.OpenTokenCount(session));
}
