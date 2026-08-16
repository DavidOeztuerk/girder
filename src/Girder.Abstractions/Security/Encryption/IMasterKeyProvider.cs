using Girder.Abstractions.Security.Encryption;
namespace Girder.Abstractions.Security.Encryption;

/// <summary>
/// Supplies the key that protects stored key material.
/// </summary>
/// <remarks>
/// <para>
/// Girder never generates and never stores this key. It asks for it, uses it,
/// and forgets it when the process ends. That is what makes customer key
/// sovereignty possible: the operator holds the root of the key hierarchy, and
/// nothing in the running system can hand it to anyone else.
/// </para>
/// <para>
/// Two implementations ship with Girder.Infrastructure and are named here in
/// plain text, because this package deliberately cannot see them: prefer
/// <c>AddSecretStoreMasterKey()</c>, which reads the key from a secret store
/// you run, over <c>AddConfiguredMasterKey()</c>, which takes it from
/// configuration and therefore from the process environment.
/// </para>
/// </remarks>
public interface IMasterKeyProvider
{
    /// <summary>
    /// The master key, 32 bytes.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No key is available. Startup should fail rather than continue with
    /// unprotected key material.
    /// </exception>
    byte[] GetMasterKey();
}
