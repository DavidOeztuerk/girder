using Girder.Abstractions.Security.Passwords;
using Girder.Infrastructure.Security.Passwords;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Girder.Infrastructure.Builder.Modules;

public static class PasswordHashingModule
{
    /// <summary>
    /// Wires password hashing: one algorithm writes, every registered format
    /// can still be read.
    /// </summary>
    /// <remarks>
    /// What an application injects is <see cref="IPasswordHasher"/>, and it is
    /// the chain over everything registered. Which algorithm writes is the
    /// deployment's decision — Girder picks none, exactly as it picks no
    /// database:
    /// <code>
    /// infra.AddPasswordHashing();                  // PBKDF2 unless told otherwise
    /// services.AddArgon2Passwords();               // ...or Argon2id writes
    /// services.AddBCryptPasswordReader();          // ...and bcrypt entries still verify
    /// </code>
    /// PBKDF2 is the fallback only because it needs no package and no licence.
    /// Replacing it is one registration, and every entry already written stays
    /// readable — a successful sign-in against an older format reports
    /// <see cref="PasswordVerification.SuccessRehashNeeded"/> so the
    /// application can rewrite it.
    /// </remarks>
    public static InfrastructureBuilder AddPasswordHashing(this InfrastructureBuilder builder)
    {
        builder.Services.Configure<PasswordHashingOptions>(
            builder.Configuration.GetSection(PasswordHashingOptions.SectionName));

        // TryAdd: a provider package registering itself first stays the writer.
        builder.Services.TryAddKeyedSingleton<IPasswordHasher>(
            PasswordHashing.PrimaryKey,
            (provider, _) => new Pbkdf2PasswordHasher(
                provider.GetRequiredService<IOptions<PasswordHashingOptions>>()));

        builder.Services.AddSingleton<IPasswordHasher>(provider =>
        {
            var primary = provider.GetRequiredKeyedService<IPasswordHasher>(PasswordHashing.PrimaryKey);
            var readers = provider.GetServices<IPasswordHashReader>().ToArray();

            return readers.Length == 0 ? primary : new PasswordHasherChain(primary, readers);
        });

        return builder;
    }
}
