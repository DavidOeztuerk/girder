using Girder.Abstractions.Security.Passwords;
using Girder.Infrastructure.Security.Passwords;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Girder.Infrastructure.Builder.Modules;

public static class PasswordHashingModule
{
    /// <summary>
    /// Registers password hashing, bound from the <c>PasswordHashing</c>
    /// configuration section.
    /// </summary>
    /// <remarks>
    /// PBKDF2-HMAC-SHA256 by default, from the runtime's own cryptography — no
    /// package, no licence. Register a different <see cref="IPasswordHasher"/>
    /// after this call to override it; the last registration wins.
    /// </remarks>
    public static InfrastructureBuilder AddPasswordHashing(this InfrastructureBuilder builder)
    {
        builder.Services.Configure<PasswordHashingOptions>(
            builder.Configuration.GetSection(PasswordHashingOptions.SectionName));

        builder.Services.AddSingleton<IPasswordHasher>(provider =>
            new Pbkdf2PasswordHasher(
                provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PasswordHashingOptions>>()));

        return builder;
    }
}
