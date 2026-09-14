using MediatR;
using Noelia.Application.Models;

namespace Noelia.Application.Interfaces;

/// <summary>
/// Marker interface for all queries
/// </summary>
public interface IQuery<TResponse>
    : IRequest<ApiResponse<TResponse>>
{ }
