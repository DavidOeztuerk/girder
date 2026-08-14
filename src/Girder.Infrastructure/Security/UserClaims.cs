namespace Girder.Infrastructure.Security;

public class UserClaims
{
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public List<string> Permissions { get; set; } = new();
    public bool EmailVerified { get; set; } = false;
    public string AccountStatus { get; set; } = "Active";

    // Session management
    public string? SessionId { get; set; }

    public Dictionary<string, string>? CustomClaims { get; set; }
}
