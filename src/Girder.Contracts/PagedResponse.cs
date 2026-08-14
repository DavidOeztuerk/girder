// namespace Contracts.Common;

// /// <summary>
// /// Generic paged response for API endpoints that return paginated data
// /// </summary>
// /// <typeparam name="T">Type of items in the response</typeparam>
// /// <param name="Items">List of items for current page</param>
// /// <param name="PageNumber">Current page number</param>
// /// <param name="PageSize">Number of items per page</param>
// /// <param name="TotalCount">Total number of items across all pages</param>
// /// <param name="TotalPages">Total number of pages</param>
// /// <param name="HasNextPage">Whether there is a next page</param>
// /// <param name="HasPreviousPage">Whether there is a previous page</param>
// /// <param name="SortBy">Field used for sorting</param>
// /// <param name="SortDirection">Direction of sorting</param>
// /// <param name="SearchTerm">Search term applied</param>
// /// <param name="AppliedFilters">Filters applied to the query</param>
// public record PagedResponse<T>(
//     List<T> Items,
//     int PageNumber,
//     int PageSize,
//     int TotalCount,
//     int TotalPages,
//     bool HasNextPage,
//     bool HasPreviousPage,
//     string? SortBy = null,
//     SortDirection? SortDirection = null,
//     string? SearchTerm = null,
//     Dictionary<string, object>? AppliedFilters = null) : IVersionedContract
// {
//     /// <summary>
//     /// API Version this response supports
//     /// </summary>
//     public string ApiVersion => "v1";

//     /// <summary>
//     /// Creates a paged response from a paged request and data
//     /// </summary>
//     public static PagedResponse<T> Create(
//         IEnumerable<T> items,
//         int totalCount,
//         PagedRequest request,
//         string? sortBy = null,
//         SortDirection? sortDirection = null,
//         string? searchTerm = null,
//         Dictionary<string, object>? appliedFilters = null)
//     {
//         var itemsList = items.ToList();
//         var totalPages = (int)Math.Ceiling((double)totalCount / request.PageSize);

//         return new PagedResponse<T>(
//             Items: itemsList,
//             PageNumber: request.PageNumber,
//             PageSize: request.PageSize,
//             TotalCount: totalCount,
//             TotalPages: totalPages,
//             HasNextPage: request.PageNumber < totalPages,
//             HasPreviousPage: request.PageNumber > 1,
//             SortBy: sortBy,
//             SortDirection: sortDirection,
//             SearchTerm: searchTerm,
//             AppliedFilters: appliedFilters
//         );
//     }

//     /// <summary>
//     /// Creates an empty paged response
//     /// </summary>
//     public static PagedResponse<T> Empty(PagedRequest request)
//     {
//         return new PagedResponse<T>(
//             Items: new List<T>(),
//             PageNumber: request.PageNumber,
//             PageSize: request.PageSize,
//             TotalCount: 0,
//             TotalPages: 0,
//             HasNextPage: false,
//             HasPreviousPage: false
//         );
//     }

//     /// <summary>
//     /// Navigation information for pagination
//     /// </summary>
//     public PaginationLinks GetNavigationLinks(string baseUrl)
//     {
//         var links = new PaginationLinks
//         {
//             Self = $"{baseUrl}?pageNumber={PageNumber}&pageSize={PageSize}",
//             First = $"{baseUrl}?pageNumber=1&pageSize={PageSize}",
//             Last = $"{baseUrl}?pageNumber={TotalPages}&pageSize={PageSize}"
//         };

//         if (HasPreviousPage)
//         {
//             //links.Previous = $"{baseUrl}?pageNumber={PageNumber - 1}&pageSize={PageSize}";
//         }

//         if (HasNextPage)
//         {
//             //links.Next = $"{baseUrl}?pageNumber={PageNumber + 1}&pageSize={PageSize}";
//         }

//         return links;
//     }
// }

// /// <summary>
// /// Navigation links for pagination
// /// </summary>
// public record PaginationLinks
// {
//     public string? Self { get; init; }
//     public string? First { get; init; }
//     public string? Last { get; init; }
//     public string? Previous { get; init; }
//     public string? Next { get; init; }
// }