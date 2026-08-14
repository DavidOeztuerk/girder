using Girder.Cqrs.Models;
using MediatR;

namespace Girder.Cqrs.Interfaces;

/// <summary>
/// Query handler interface
/// </summary>
/// <typeparam name="TQuery">Query type</typeparam>
/// <typeparam name="TResponse">Response type</typeparam>
public interface IQueryHandler<TQuery, TResponse>
    : IRequestHandler<TQuery, ApiResponse<TResponse>>
    where TQuery : IQuery<TResponse>
{ }
