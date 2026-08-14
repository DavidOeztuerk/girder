using MediatR;
using Girder.Cqrs.Models;

namespace Girder.Cqrs.Interfaces;

/// <summary>
/// Marker interface for all queries
/// </summary>
public interface IQuery<TResponse>
    : IRequest<ApiResponse<TResponse>>
{ }
