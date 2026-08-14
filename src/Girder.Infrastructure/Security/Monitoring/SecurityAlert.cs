using Domain.Abstractions;

namespace Infrastructure.Security.Monitoring;

/// <summary>
/// Represents a security alert in the system
/// </summary>
public class SecurityAlert : AuditableEntity
{
    public SecurityAlertLevel Level { get; set; }

    public SecurityAlertType Type { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string? UserId { get; set; }

    public string? IPAddress { get; set; }

    public string? UserAgent { get; set; }

    public string? Endpoint { get; set; }

    public Dictionary<string, object> Metadata { get; set; } = new();

    public bool IsRead { get; set; } = false;

    public DateTime? ReadAt { get; set; }

    public string? ReadByAdminId { get; set; }

    public bool IsDismissed { get; set; } = false;

    public DateTime? DismissedAt { get; set; }

    public string? DismissedByAdminId { get; set; }

    public string? DismissalReason { get; set; }

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    public int OccurrenceCount { get; set; } = 1;

    public DateTime? LastOccurrenceAt { get; set; }
}
