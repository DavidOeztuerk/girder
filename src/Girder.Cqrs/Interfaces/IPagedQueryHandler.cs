using CQRS.Models;
using MediatR;

namespace CQRS.Interfaces;

/// <summary>
/// Paged query handler interface
/// </summary>
/// <typeparam name="TQuery">Query type</typeparam>
/// <typeparam name="TResponse">Response item type</typeparam>
public interface IPagedQueryHandler<TQuery, TResponse>
    : IRequestHandler<TQuery, PagedResponse<TResponse>>
    where TQuery : IPagedQuery<TResponse>
{ }
