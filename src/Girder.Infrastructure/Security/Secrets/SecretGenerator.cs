using System.Security.Cryptography;
using System.Text;

namespace Infrastructure.Security.Secrets;

/// <summary>
/// Generates secure secrets and keys
/// </summary>
public static class SecretGenerator
{
    private const string UppercaseChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string LowercaseChars = "abcdefghijklmnopqrstuvwxyz";
    private const string Digits = "0123456789";
    private const string SpecialChars = "!@#$%^&*()_+-=[]{}|;:,.<>?";
    private const string AlphanumericChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    
    /// <summary>
    /// Generates a secure random secret
    /// </summary>
    public static string GenerateSecret(int length = 32, SecretType type = SecretType.AlphanumericWithSpecial)
    {
        if (length < 12)
        {
            throw new ArgumentException("Secret length must be at least 12 characters", nameof(length));
        }

        var charset = GetCharset(type);
        var secret = new StringBuilder(length);
        
        using var rng = RandomNumberGenerator.Create();
        var randomBytes = new byte[length * 4]; // Overkill to ensure enough randomness
        rng.GetBytes(randomBytes);

        // Ensure complexity requirements for passwords
        if (type == SecretType.Password || type == SecretType.AlphanumericWithSpecial)
        {
            // Ensure at least one of each required character type
            secret.Append(GetRandomChar(UppercaseChars, randomBytes, 0));
            secret.Append(GetRandomChar(LowercaseChars, randomBytes, 1));
            secret.Append(GetRandomChar(Digits, randomBytes, 2));
            secret.Append(GetRandomChar(SpecialChars, randomBytes, 3));
            
            // Fill the rest randomly
            for (int i = 4; i < length; i++)
            {
                secret.Append(GetRandomChar(charset, randomBytes, i));
            }
            
            // Shuffle the result
            return ShuffleString(secret.ToString(), randomBytes);
        }
        else
        {
            for (int i = 0; i < length; i++)
            {
                secret.Append(GetRandomChar(charset, randomBytes, i));
            }
            return secret.ToString();
        }
    }

    /// <summary>
    /// Generates a secure API key
    /// </summary>
    public static string GenerateApiKey(string prefix = "sk")
    {
        var keyBytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(keyBytes);
        
        var key = Convert.ToBase64String(keyBytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
        
        return $"{prefix}_{key}";
    }

    /// <summary>
    /// Generates a JWT secret
    /// </summary>
    public static string GenerateJwtSecret()
    {
        var keyBytes = new byte[64]; // 512-bit key
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(keyBytes);
        
        return Convert.ToBase64String(keyBytes);
    }

    /// <summary>
    /// Generates a hex key
    /// </summary>
    public static string GenerateHexKey(int byteLength = 32)
    {
        var keyBytes = new byte[byteLength];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(keyBytes);
        
        return BitConverter.ToString(keyBytes).Replace("-", "").ToLowerInvariant();
    }

    /// <summary>
    /// Generates a refresh token
    /// </summary>
    public static string GenerateRefreshToken()
    {
        var tokenBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(tokenBytes);
        
        return Convert.ToBase64String(tokenBytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }

    /// <summary>
    /// Generates a TOTP secret
    /// </summary>
    public static string GenerateTotpSecret()
    {
        const string base32Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var secretLength = 32; // 160 bits
        var secret = new StringBuilder(secretLength);
        
        using var rng = RandomNumberGenerator.Create();
        var randomBytes = new byte[secretLength];
        rng.GetBytes(randomBytes);
        
        for (int i = 0; i < secretLength; i++)
        {
            secret.Append(base32Chars[randomBytes[i] % base32Chars.Length]);
        }
        
        return secret.ToString();
    }

    /// <summary>
    /// Generates an encryption key
    /// </summary>
    public static byte[] GenerateEncryptionKey(int bitLength = 256)
    {
        if (bitLength % 8 != 0)
        {
            throw new ArgumentException("Bit length must be divisible by 8", nameof(bitLength));
        }
        
        var keyBytes = new byte[bitLength / 8];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(keyBytes);
        
        return keyBytes;
    }

    /// <summary>
    /// Generates a salt for password hashing
    /// </summary>
    public static byte[] GenerateSalt(int byteLength = 32)
    {
        var salt = new byte[byteLength];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(salt);
        
        return salt;
    }

    private static string GetCharset(SecretType type)
    {
        return type switch
        {
            SecretType.Alphanumeric => AlphanumericChars,
            SecretType.AlphanumericWithSpecial => AlphanumericChars + SpecialChars,
            SecretType.Password => AlphanumericChars + SpecialChars,
            SecretType.Hex => "0123456789abcdef",
            SecretType.Numeric => Digits,
            _ => AlphanumericChars
        };
    }

    private static char GetRandomChar(string charset, byte[] randomBytes, int index)
    {
        var randomIndex = BitConverter.ToUInt32(randomBytes, index * 4) % (uint)charset.Length;
        return charset[(int)randomIndex];
    }

    private static string ShuffleString(string input, byte[] randomBytes)
    {
        var array = input.ToCharArray();
        var n = array.Length;
        
        for (int i = n - 1; i > 0; i--)
        {
            var j = BitConverter.ToUInt32(randomBytes, i * 4) % (uint)(i + 1);
            (array[i], array[j]) = (array[j], array[i]);
        }
        
        return new string(array);
    }
}

/// <summary>
/// Type of secret to generate
/// </summary>
public enum SecretType
{
    Alphanumeric,
    AlphanumericWithSpecial,
    Password,
    Hex,
    Numeric
}