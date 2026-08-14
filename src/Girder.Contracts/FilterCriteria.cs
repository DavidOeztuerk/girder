using System.ComponentModel.DataAnnotations;

namespace Contracts.Common;

/// <summary>
/// Generic filter criteria for API requests
/// </summary>
public class FilterCriteria
{
    /// <summary>
    /// Field name to filter on
    /// </summary>
    [Required]
    [StringLength(50, ErrorMessage = "Field name must not exceed 50 characters")]
    public required string Field { get; init; }

    /// <summary>
    /// Filter operator
    /// </summary>
    public FilterOperator Operator { get; init; } = FilterOperator.Equal;

    /// <summary>
    /// Value to filter by
    /// </summary>
    public object? Value { get; init; }

    /// <summary>
    /// Values for IN and NOT_IN operations
    /// </summary>
    public object[]? Values { get; init; }

    /// <summary>
    /// Whether the filter is case sensitive (for string comparisons)
    /// </summary>
    public bool CaseSensitive { get; init; } = false;
}

/// <summary>
/// Filter operators supported in API requests
/// </summary>
public enum FilterOperator
{
    /// <summary>
    /// Equals comparison
    /// </summary>
    Equal,

    /// <summary>
    /// Not equals comparison
    /// </summary>
    NotEqual,

    /// <summary>
    /// Greater than comparison
    /// </summary>
    GreaterThan,

    /// <summary>
    /// Greater than or equal comparison
    /// </summary>
    GreaterThanOrEqual,

    /// <summary>
    /// Less than comparison
    /// </summary>
    LessThan,

    /// <summary>
    /// Less than or equal comparison
    /// </summary>
    LessThanOrEqual,

    /// <summary>
    /// Contains comparison (for strings)
    /// </summary>
    Contains,

    /// <summary>
    /// Starts with comparison (for strings)
    /// </summary>
    StartsWith,

    /// <summary>
    /// Ends with comparison (for strings)
    /// </summary>
    EndsWith,

    /// <summary>
    /// In list comparison
    /// </summary>
    In,

    /// <summary>
    /// Not in list comparison
    /// </summary>
    NotIn,

    /// <summary>
    /// Is null comparison
    /// </summary>
    IsNull,

    /// <summary>
    /// Is not null comparison
    /// </summary>
    IsNotNull,

    /// <summary>
    /// Between two values (requires Values array with 2 elements)
    /// </summary>
    Between
}

/// <summary>
/// Complex filter request with multiple criteria
/// </summary>
/// <param name="PageNumber">Page number</param>
/// <param name="PageSize">Page size</param>
/// <param name="SortBy">Field to sort by</param>
/// <param name="SortDirection">Sort direction</param>
/// <param name="SearchTerm">Global search term</param>
/// <param name="Filters">Individual filter criteria</param>
/// <param name="LogicalOperator">How to combine multiple filters</param>
public record AdvancedFilterRequest(
    int PageNumber = 1,
    int PageSize = 12,
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Ascending,
    string? SearchTerm = null,
    List<FilterCriteria>? Filters = null,
    LogicalOperator LogicalOperator = LogicalOperator.And) 
    : FilteredPagedRequest(PageNumber, PageSize, SortBy, SortDirection, SearchTerm)
{
    public new List<FilterCriteria> Filters { get; init; } = Filters ?? new List<FilterCriteria>();
    public LogicalOperator LogicalOperator { get; init; } = LogicalOperator;
}

/// <summary>
/// Logical operators for combining filters
/// </summary>
public enum LogicalOperator
{
    /// <summary>
    /// All filters must match
    /// </summary>
    And,

    /// <summary>
    /// Any filter must match
    /// </summary>
    Or
}

/// <summary>
/// Date range filter for common date filtering scenarios
/// </summary>
public record DateRangeFilter
{
    /// <summary>
    /// Start date (inclusive)
    /// </summary>
    public DateTime? From { get; init; }

    /// <summary>
    /// End date (inclusive)
    /// </summary>
    public DateTime? To { get; init; }

    /// <summary>
    /// Predefined date range
    /// </summary>
    public DateRangePreset? Preset { get; init; }
}

/// <summary>
/// Predefined date range options
/// </summary>
public enum DateRangePreset
{
    Today,
    Yesterday,
    ThisWeek,
    LastWeek,
    ThisMonth,
    LastMonth,
    ThisYear,
    LastYear
}