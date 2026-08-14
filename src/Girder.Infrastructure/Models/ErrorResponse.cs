namespace Infrastructure.Models;

public class ErrorResponse
{
    public string Title { get; set; } = string.Empty;
    public int Status { get; set; }
    public string Detail { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public Dictionary<string, string[]>? Errors { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? CorrelationId { get; set; }
    public string TraceId { get; set; } = Guid.NewGuid().ToString();
    public string? HelpUrl { get; set; }
    public Dictionary<string, object>? AdditionalData { get; set; }
}
