using System.ComponentModel.DataAnnotations;

namespace Contracts.Common;

/// <summary>
/// Base pagination request contract
/// </summary>
/// <param name="PageNumber">Page number (1-based)</param>
/// <param name="PageSize">Number of items per page</param>
public record PagedRequest(
    [Range(1, int.MaxValue, ErrorMessage = "Page number must be greater than 0")]
    int PageNumber = 1,
    
    [Range(1, 100, ErrorMessage = "Page size must be between 1 and 100")]
    int PageSize = 12) : IVersionedContract
{
    /// <summary>
    /// API Version this request supports
    /// </summary>
    public string ApiVersion => "v1";

    /// <summary>
    /// Calculates the number of items to skip
    /// </summary>
    public int Skip => (PageNumber - 1) * PageSize;

    /// <summary>
    /// Gets the number of items to take
    /// </summary>
    public int Take => PageSize;
}

/// <summary>
/// Pagination request with sorting support
/// </summary>
/// <param name="PageNumber">Page number (1-based)</param>
/// <param name="PageSize">Number of items per page</param>
/// <param name="SortBy">Field to sort by</param>
/// <param name="SortDirection">Sort direction (asc or desc)</param>
public record SortedPagedRequest(
    int PageNumber = 1,
    int PageSize = 12,

    [StringLength(50, ErrorMessage = "Sort field name must not exceed 50 characters")]
    string? SortBy = null,
    
    SortDirection SortDirection = SortDirection.Ascending) : PagedRequest(PageNumber, PageSize);

/// <summary>
/// Pagination request with filtering support
/// </summary>
/// <param name="PageNumber">Page number (1-based)</param>
/// <param name="PageSize">Number of items per page</param>
/// <param name="SortBy">Field to sort by</param>
/// <param name="SortDirection">Sort direction</param>
/// <param name="SearchTerm">General search term</param>
/// <param name="Filters">Field-specific filters</param>
public record FilteredPagedRequest(
    int PageNumber = 1,
    int PageSize = 10,
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Ascending,
    
    [StringLength(200, ErrorMessage = "Search term must not exceed 200 characters")]
    string? SearchTerm = null,
    
    Dictionary<string, object>? Filters = null) : SortedPagedRequest(PageNumber, PageSize, SortBy, SortDirection);

/// <summary>
/// Sort direction enumeration
/// </summary>
public enum SortDirection
{
    Ascending,
    Descending
}