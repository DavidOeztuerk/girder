using MediatR;
using CQRS.Models;

namespace CQRS.Interfaces;

/// <summary>
/// Marker interface for all queries
/// </summary>
public interface IQuery<TResponse>
    : IRequest<ApiResponse<TResponse>>
{ }
