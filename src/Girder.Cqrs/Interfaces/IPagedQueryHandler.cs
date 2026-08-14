using Girder.Cqrs.Models;
using MediatR;

namespace Girder.Cqrs.Interfaces;

/// <summary>
/// Paged query handler interface
/// </summary>
/// <typeparam name="TQuery">Query type</typeparam>
/// <typeparam name="TResponse">Response item type</typeparam>
public interface IPagedQueryHandler<TQuery, TResponse>
    : IRequestHandler<TQuery, PagedResponse<TResponse>>
    where TQuery : IPagedQuery<TResponse>
{ }
