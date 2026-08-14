namespace CQRS.Models;

public record UserSummary(
    string UserId,
    string FirstName,
    string LastName,
    string UserName,
    string Email,
    string? ProfilePictureUrl = null)
{
    public string DisplayName => !string.IsNullOrEmpty(FirstName) && !string.IsNullOrEmpty(LastName) 
        ? $"{FirstName} {LastName}" 
        : UserName;
}
