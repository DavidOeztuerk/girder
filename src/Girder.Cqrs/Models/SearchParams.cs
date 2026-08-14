namespace CQRS.Models;

/// <summary>
/// Common search and filter parameters
/// </summary>
public class SearchParams : PaginationParams
{
    public string? Query { get; set; }
    public string? SortBy { get; set; }
    public string? SortDirection { get; set; } = "asc";
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}
