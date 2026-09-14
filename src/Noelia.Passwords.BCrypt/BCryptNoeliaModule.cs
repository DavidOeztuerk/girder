using Noelia.Abstractions.Hosting;
using Noelia.Abstractions.Security.Passwords;

namespace Noelia.Passwords.BCrypt;

/// <summary>Exposes the selected bcrypt writer in the Noelia composition.</summary>
public static class BCryptNoeliaModule
{
    public static NoeliaModule Module => new("Passwords.BCrypt");

    public static NoeliaBuilder UseBCryptPasswords(
        this NoeliaBuilder noelia,
        int workFactor = 12)
    {
        ArgumentNullException.ThrowIfNull(noelia);

        return noelia.Use(
            Module,
            builder => builder.Services.AddBCryptPasswords(workFactor),
            contract => contract.Provides<IPasswordHasher>(
                "Noelia.Passwords.BCrypt", "UseBCryptPasswords()"));
    }
}
