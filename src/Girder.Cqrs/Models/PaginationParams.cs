namespace CQRS.Models;

/// <summary>
/// Standard pagination parameters for query endpoints
/// </summary>
public class PaginationParams
{
    private const int MaxPageSize = 96;
    private int _pageSize = 12;

    public int PageNumber { get; set; } = 1;

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value > MaxPageSize ? MaxPageSize : value;
    }

    public int Skip => (PageNumber - 1) * PageSize;
}
