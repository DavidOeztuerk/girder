namespace CQRS.Interfaces;

/// <summary>
/// Base audit information for commands
/// </summary>
public interface IAuditableCommand
{
    string? UserId { get; set; }
    DateTime Timestamp { get; set; }
}
