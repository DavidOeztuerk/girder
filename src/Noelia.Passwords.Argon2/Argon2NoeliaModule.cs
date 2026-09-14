using Noelia.Abstractions.Hosting;
using Noelia.Abstractions.Security.Passwords;

namespace Noelia.Passwords.Argon2;

/// <summary>Exposes the selected Argon2id writer in the Noelia composition.</summary>
public static class Argon2NoeliaModule
{
    public static NoeliaModule Module => new("Passwords.Argon2");

    public static NoeliaBuilder UseArgon2Passwords(
        this NoeliaBuilder noelia,
        Argon2Cost cost = default)
    {
        ArgumentNullException.ThrowIfNull(noelia);

        return noelia.Use(
            Module,
            builder => builder.Services.AddArgon2Passwords(cost),
            contract => contract.Provides<IPasswordHasher>(
                "Noelia.Passwords.Argon2", "UseArgon2Passwords()"));
    }
}
