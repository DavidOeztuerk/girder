using Microsoft.Extensions.Configuration;

namespace Girder.Core.Exceptions;

/// <summary>
/// Service for providing user-friendly error messages and help information
/// </summary>
public class ErrorMessageService : IErrorMessageService
{
  private readonly Dictionary<string, ErrorInfo> _errorMappings;
  private readonly string _baseHelpUrl;

  public ErrorMessageService(IConfiguration? configuration = null)
  {
    _baseHelpUrl = configuration?["ErrorHandling:HelpUrl"] ?? "https://docs.girder.com/errors/";
    _errorMappings = InitializeErrorMappings();
  }

  public string GetUserMessage(string errorCode, string? defaultMessage = null)
  {
    if (_errorMappings.TryGetValue(errorCode, out var errorInfo))
    {
      return errorInfo.UserMessage;
    }

    return defaultMessage ?? "Ein unerwarteter Fehler ist aufgetreten. Bitte versuchen Sie es später erneut.";
  }

  public string? GetHelpUrl(string errorCode)
  {
    if (_errorMappings.TryGetValue(errorCode, out var errorInfo) && !string.IsNullOrEmpty(errorInfo.HelpPath))
    {
      return $"{_baseHelpUrl}{errorInfo.HelpPath}";
    }

    return null;
  }

  public string[]? GetSuggestedActions(string errorCode)
  {
    if (_errorMappings.TryGetValue(errorCode, out var errorInfo))
    {
      return errorInfo.SuggestedActions;
    }

    return null;
  }

  public bool IsUserFacingError(string errorCode)
  {
    if (_errorMappings.TryGetValue(errorCode, out var errorInfo))
    {
      return errorInfo.IsUserFacing;
    }

    // Default to not showing technical errors to users
    return false;
  }

  private Dictionary<string, ErrorInfo> InitializeErrorMappings()
  {
    return new Dictionary<string, ErrorInfo>
    {
      // Domain Errors
      [ErrorCodes.BusinessRuleViolation] = new ErrorInfo
      {
        UserMessage = "Diese Aktion verstößt gegen Geschäftsregeln. Bitte überprüfen Sie Ihre Eingaben und versuchen Sie es erneut.",
        IsUserFacing = true,
        SuggestedActions = new[] { "Überprüfen Sie die Anforderungen", "Kontaktieren Sie den Support, falls das Problem weiterhin besteht" }
      },

      [ErrorCodes.ResourceNotFound] = new ErrorInfo
      {
        UserMessage = "Das angeforderte Element wurde nicht gefunden. Es wurde möglicherweise gelöscht oder Sie haben keinen Zugriff darauf.",
        IsUserFacing = true,
        HelpPath = "resource-not-found",
        SuggestedActions = new[] { "Überprüfen Sie die URL oder ID", "Laden Sie die Seite neu", "Kontaktieren Sie den Eigentümer der Ressource" }
      },

      [ErrorCodes.ResourceAlreadyExists] = new ErrorInfo
      {
        UserMessage = "Ein Element mit demselben Bezeichner existiert bereits. Bitte verwenden Sie einen anderen Namen oder Bezeichner.",
        IsUserFacing = true,
        SuggestedActions = new[] { "Wählen Sie einen anderen Namen", "Aktualisieren Sie stattdessen das vorhandene Element" }
      },

      // Authentication & Authorization
      [ErrorCodes.Unauthorized] = new ErrorInfo
      {
        UserMessage = "Sie müssen sich anmelden, um auf diese Ressource zuzugreifen.",
        IsUserFacing = true,
        HelpPath = "authentication",
        SuggestedActions = new[] { "Melden Sie sich in Ihrem Konto an", "Überprüfen Sie, ob Ihre Sitzung abgelaufen ist" }
      },

      [ErrorCodes.InsufficientPermissions] = new ErrorInfo
      {
        UserMessage = "Sie haben keine Berechtigung, diese Aktion auszuführen.",
        IsUserFacing = true,
        HelpPath = "permissions",
        SuggestedActions = new[] { "Kontaktieren Sie Ihren Administrator", "Fordern Sie die erforderlichen Berechtigungen an" }
      },

      [ErrorCodes.TokenExpired] = new ErrorInfo
      {
        UserMessage = "Ihre Sitzung ist abgelaufen. Bitte melden Sie sich erneut an.",
        IsUserFacing = true,
        SuggestedActions = new[] { "Erneut anmelden", "Bleiben Sie aktiv, um ein Sitzungs-Timeout zu vermeiden" }
      },

      [ErrorCodes.InvalidCredentials] = new ErrorInfo
      {
        UserMessage = "Ungültige E-Mail-Adresse oder ungültiges Passwort. Bitte überprüfen Sie Ihre Eingaben und versuchen Sie es erneut.",
        IsUserFacing = true,
        HelpPath = "login-issues",
        SuggestedActions = new[] { "Überprüfen Sie E-Mail-Adresse und Passwort", "Nutzen Sie 'Passwort vergessen', falls nötig", "Stellen Sie sicher, dass die Feststelltaste nicht aktiv ist" }
      },

      [ErrorCodes.AccountNotVerified] = new ErrorInfo
      {
        UserMessage = "Bitte bestätigen Sie Ihre E-Mail-Adresse, um fortzufahren. Prüfen Sie Ihren Posteingang auf den Bestätigungslink.",
        IsUserFacing = true,
        HelpPath = "email-verification",
        SuggestedActions = new[] { "Prüfen Sie Ihren E-Mail-Posteingang", "Prüfen Sie Ihren Spam-Ordner", "Klicken Sie auf 'Bestätigung erneut senden', falls nötig" }
      },

      [ErrorCodes.TwoFactorRequired] = new ErrorInfo
      {
        UserMessage = "Für diese Aktion ist eine Zwei-Faktor-Authentifizierung erforderlich.",
        IsUserFacing = true,
        HelpPath = "two-factor-auth",
        SuggestedActions = new[] { "Geben Sie Ihren 2FA-Code ein", "Richten Sie 2FA ein, falls noch nicht geschehen" }
      },

      // Validation Errors
      [ErrorCodes.ValidationFailed] = new ErrorInfo
      {
        UserMessage = "Bitte überprüfen Sie Ihre Eingaben und korrigieren Sie eventuelle Fehler.",
        IsUserFacing = true,
        SuggestedActions = new[] { "Überprüfen Sie alle Pflichtfelder", "Achten Sie auf Formatfehler" }
      },

      [ErrorCodes.RequiredFieldMissing] = new ErrorInfo
      {
        UserMessage = "Bitte füllen Sie alle Pflichtfelder aus.",
        IsUserFacing = true,
        SuggestedActions = new[] { "Achten Sie auf Felder mit *", "Vervollständigen Sie alle Pflichtangaben" }
      },

      [ErrorCodes.InvalidEmail] = new ErrorInfo
      {
        UserMessage = "Bitte geben Sie eine gültige E-Mail-Adresse ein.",
        IsUserFacing = true,
        SuggestedActions = new[] { "Überprüfen Sie das E-Mail-Format (z.B. user@example.com)" }
      },

      [ErrorCodes.InvalidPhoneNumber] = new ErrorInfo
      {
        UserMessage = "Bitte geben Sie eine gültige Telefonnummer ein.",
        IsUserFacing = true,
        SuggestedActions = new[] { "Geben Sie ggf. die Ländervorwahl mit an", "Entfernen Sie Sonderzeichen" }
      },

      // External Service Errors
      [ErrorCodes.ServiceUnavailable] = new ErrorInfo
      {
        UserMessage = "Der Dienst ist vorübergehend nicht verfügbar. Bitte versuchen Sie es in wenigen Augenblicken erneut.",
        IsUserFacing = true,
        HelpPath = "service-status",
        SuggestedActions = new[] { "Warten Sie einige Minuten und versuchen Sie es erneut", "Prüfen Sie unsere Statusseite" }
      },

      [ErrorCodes.ServiceTimeout] = new ErrorInfo
      {
        UserMessage = "Die Anfrage hat zu lange gedauert. Bitte versuchen Sie es erneut.",
        IsUserFacing = true,
        SuggestedActions = new[] { "Überprüfen Sie Ihre Internetverbindung", "Probieren Sie eine einfachere Anfrage", "Wiederholen Sie den Vorgang" }
      },

      [ErrorCodes.RateLimitExceeded] = new ErrorInfo
      {
        UserMessage = "Sie haben zu viele Anfragen gesendet. Bitte warten Sie einen Moment, bevor Sie es erneut versuchen.",
        IsUserFacing = true,
        HelpPath = "rate-limits",
        SuggestedActions = new[] { "Warten Sie 60 Sekunden, bevor Sie es erneut versuchen", "Reduzieren Sie die Anfragefrequenz" }
      },

      [ErrorCodes.PaymentFailed] = new ErrorInfo
      {
        UserMessage = "Die Zahlung konnte nicht verarbeitet werden. Bitte überprüfen Sie Ihre Zahlungsinformationen und versuchen Sie es erneut.",
        IsUserFacing = true,
        HelpPath = "payment-issues",
        SuggestedActions = new[] { "Überprüfen Sie Ihre Zahlungsmethode", "Wenden Sie sich an Ihre Bank", "Probieren Sie eine andere Zahlungsmethode" }
      },

      // File & Storage Errors
      [ErrorCodes.FileTooLarge] = new ErrorInfo
      {
        UserMessage = "Die Datei ist zu groß. Bitte wählen Sie eine kleinere Datei.",
        IsUserFacing = true,
        SuggestedActions = new[] { "Komprimieren Sie die Datei", "Wählen Sie eine Datei unterhalb der Größenbegrenzung" }
      },

      [ErrorCodes.InvalidFileType] = new ErrorInfo
      {
        UserMessage = "Dieser Dateityp wird nicht unterstützt. Bitte wählen Sie eine andere Datei.",
        IsUserFacing = true,
        SuggestedActions = new[] { "Prüfen Sie die unterstützten Dateitypen", "Konvertieren Sie Ihre Datei in ein unterstütztes Format" }
      },

      [ErrorCodes.StorageQuotaExceeded] = new ErrorInfo
      {
        UserMessage = "Sie haben Ihr Speicherlimit erreicht. Bitte löschen Sie einige Dateien oder erweitern Sie Ihren Plan.",
        IsUserFacing = true,
        HelpPath = "storage-limits",
        SuggestedActions = new[] { "Löschen Sie nicht benötigte Dateien", "Erweitern Sie Ihren Speicherplan" }
      },

      // Business Logic Errors
      [ErrorCodes.InsufficientBalance] = new ErrorInfo
      {
        UserMessage = "Sie haben nicht genügend Guthaben für diese Aktion.",
        IsUserFacing = true,
        SuggestedActions = new[] { "Kaufen Sie weiteres Guthaben", "Prüfen Sie Ihren aktuellen Kontostand" }
      },

      [ErrorCodes.SubscriptionExpired] = new ErrorInfo
      {
        UserMessage = "Ihr Abonnement ist abgelaufen. Bitte verlängern Sie es, um fortzufahren.",
        IsUserFacing = true,
        HelpPath = "subscription",
        SuggestedActions = new[] { "Verlängern Sie Ihr Abonnement", "Wählen Sie einen anderen Plan" }
      },

      [ErrorCodes.MaxAttemptsExceeded] = new ErrorInfo
      {
        UserMessage = "Maximale Anzahl an Versuchen überschritten. Bitte warten Sie, bevor Sie es erneut versuchen.",
        IsUserFacing = true,
        SuggestedActions = new[] { "Warten Sie 30 Minuten, bevor Sie es erneut versuchen", "Kontaktieren Sie den Support, falls Sie sofortige Hilfe benötigen" }
      },

      // Database Errors (usually not user-facing)
      [ErrorCodes.DatabaseError] = new ErrorInfo
      {
        UserMessage = "Ein technischer Fehler ist aufgetreten. Unser Team wurde benachrichtigt.",
        IsUserFacing = false
      },

      [ErrorCodes.DeadlockDetected] = new ErrorInfo
      {
        UserMessage = "Der Vorgang konnte aufgrund eines Konflikts nicht abgeschlossen werden. Bitte versuchen Sie es erneut.",
        IsUserFacing = true,
        SuggestedActions = new[] { "Wiederholen Sie den Vorgang", "Warten Sie einen Moment, falls mehrere Benutzer gleichzeitig dieselben Daten ändern" }
      },

      // System Errors (not user-facing)
      [ErrorCodes.InternalError] = new ErrorInfo
      {
        UserMessage = "Ein unerwarteter Fehler ist aufgetreten. Unser Team wurde benachrichtigt.",
        IsUserFacing = false
      },

      // Network Errors
      [ErrorCodes.ConnectionTimeout] = new ErrorInfo
      {
        UserMessage = "Zeitüberschreitung der Verbindung. Bitte überprüfen Sie Ihre Internetverbindung und versuchen Sie es erneut.",
        IsUserFacing = true,
        SuggestedActions = new[] { "Überprüfen Sie Ihre Internetverbindung", "Versuchen Sie es in wenigen Augenblicken erneut" }
      },

      [ErrorCodes.SslError] = new ErrorInfo
      {
        UserMessage = "Sichere Verbindung fehlgeschlagen. Bitte stellen Sie sicher, dass Sie einen aktuellen Browser verwenden.",
        IsUserFacing = true,
        SuggestedActions = new[] { "Aktualisieren Sie Ihren Browser", "Überprüfen Sie Datum und Uhrzeit Ihres Systems" }
      }
    };
  }

  private class ErrorInfo
  {
    public string UserMessage { get; set; } = string.Empty;
    public bool IsUserFacing { get; set; } = true;
    public string? HelpPath { get; set; }
    public string[]? SuggestedActions { get; set; }
  }
}
