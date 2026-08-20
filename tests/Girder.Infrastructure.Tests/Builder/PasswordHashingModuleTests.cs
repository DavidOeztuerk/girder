using Girder.Abstractions.Security.Passwords;
using Girder.Infrastructure.Builder.Modules;
using Girder.Infrastructure.Extensions;
using Girder.Infrastructure.Security.Passwords;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Girder.Infrastructure.Tests.Builder;

[Trait("Category", "Unit")]
public class PasswordHashingModuleTests
{
    [Fact]
    public void The_module_provides_a_hasher()
    {
        var app = Build([]);

        app.Services.GetService<IPasswordHasher>().Should().NotBeNull();
    }

    /// <summary>
    /// The cost is an operational decision, so it comes from configuration.
    /// </summary>
    [Fact]
    public void The_cost_comes_from_configuration()
    {
        var app = Build(new Dictionary<string, string?>
        {
            ["PasswordHashing:Iterations"] = "210000"
        });

        var stored = app.Services.GetRequiredService<IPasswordHasher>().Hash("a password");

        stored.Should().Contain("i=210000");
    }

    /// <summary>
    /// An application that brings its own — Argon2id, or a reader for entries
    /// arriving from another system — replaces it by registering afterwards.
    /// </summary>
    [Fact]
    public void An_application_can_replace_it()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSharedInfrastructure(
            builder.Configuration, builder.Environment, "test",
            infrastructure => infrastructure.AddPasswordHashing());
        builder.Services.AddSingleton<IPasswordHasher>(new RefusingHasher());

        builder.Build().Services.GetRequiredService<IPasswordHasher>()
            .Should().BeOfType<RefusingHasher>();
    }

    private static WebApplication Build(Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddSharedInfrastructure(
            builder.Configuration, builder.Environment, "test",
            infrastructure => infrastructure.AddPasswordHashing());
        return builder.Build();
    }

    private sealed class RefusingHasher : IPasswordHasher
    {
        public string Hash(string password) => throw new NotSupportedException();

        public PasswordVerification Verify(string password, string? encodedHash) =>
            PasswordVerification.Failed;
    }
}
