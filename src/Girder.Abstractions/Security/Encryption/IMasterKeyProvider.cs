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
/// Prefer <see cref="SecretStoreMasterKeyProvider"/>, which reads it from a
/// secret store you run. <see cref="ConfiguredMasterKeyProvider"/> is the
/// simpler option and puts the key in the process environment instead.
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
