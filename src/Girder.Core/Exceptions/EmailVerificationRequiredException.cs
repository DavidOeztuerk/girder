namespace Core.Common.Exceptions;

/// <summary>
/// Exception thrown when email verification is required to proceed
/// </summary>
public class EmailVerificationRequiredException : DomainException
{
    public string Email { get; }
    public DateTime AccountCreatedAt { get; }
    public bool CanResendVerification { get; }

    public EmailVerificationRequiredException(
        string email,
        DateTime accountCreatedAt,
        bool canResendVerification = true)
        : base(
            ErrorCodes.AccountNotVerified,
            "Email verification required. Please check your email for the verification link.",
            canResendVerification 
                ? "Please verify your email address to continue. Click 'Resend Verification' if you haven't received the email."
                : "Email verification pending. Please check your spam folder or contact support.",
            null,
            new Dictionary<string, object>
            {
                // Don't expose email - it's sensitive
                ["CanResendVerification"] = canResendVerification,
                ["DaysSinceCreation"] = Math.Round((DateTime.UtcNow - accountCreatedAt).TotalDays, 1)
            })
    {
        Email = email;
        AccountCreatedAt = accountCreatedAt;
        CanResendVerification = canResendVerification;
    }

    public override int GetHttpStatusCode() => 403; // Forbidden - need to verify email first
}