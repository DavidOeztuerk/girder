namespace Girder.Core.Exceptions;

/// <summary>
/// Exception thrown when user provides invalid credentials
/// </summary>
public class InvalidCredentialsException : DomainException
{
    public string? Email { get; }
    public bool IsLocked { get; }
    public int? RemainingAttempts { get; }

    public InvalidCredentialsException(
        string? email = null,
        bool isLocked = false,
        int? remainingAttempts = null)
        : base(
            ErrorCodes.InvalidCredentials,
            isLocked
                ? "Ihr Konto wurde wegen mehrerer fehlgeschlagener Anmeldeversuche gesperrt. Bitte versuchen Sie es später erneut oder setzen Sie Ihr Passwort zurück."
                : "Ungültige E-Mail-Adresse oder ungültiges Passwort. Bitte überprüfen Sie Ihre Eingaben und versuchen Sie es erneut.",
            isLocked 
                ? "Account locked for security. Wait 30 minutes or reset password."
                : $"Invalid credentials{(remainingAttempts.HasValue ? $". {remainingAttempts} attempts remaining." : "")}",
            null,
            new Dictionary<string, object>
            {
                // Don't expose email in AdditionalData - it's sensitive information
                ["IsLocked"] = isLocked,
                ["RemainingAttempts"] = remainingAttempts ?? 0
            })
    {
        Email = email;
        IsLocked = isLocked;
        RemainingAttempts = remainingAttempts;
    }

    public override int GetHttpStatusCode() => 401; // Unauthorized
}