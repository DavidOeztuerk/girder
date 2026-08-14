using OtpNet;

namespace Infrastructure.Security;

public interface ITotpService
{
    string GenerateSecret();
    string GenerateCode(string secret);
    bool VerifyCode(string secret, string code);
}

public class TotpService : ITotpService
{
    public string GenerateSecret()
    {
        var bytes = KeyGeneration.GenerateRandomKey(20);
        return Base32Encoding.ToString(bytes);
    }

    public string GenerateCode(string secret)
    {
        var totp = new Totp(Base32Encoding.ToBytes(secret));
        return totp.ComputeTotp(DateTime.UtcNow);
    }

    public bool VerifyCode(string secret, string code)
    {
        var totp = new Totp(Base32Encoding.ToBytes(secret));
        return totp.VerifyTotp(code, out _);
    }
}
