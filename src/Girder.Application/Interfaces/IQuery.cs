using MediatR;
using Girder.Application.Models;

namespace Girder.Application.Interfaces;

/// <summary>
/// Marker interface for all queries
/// </summary>
public interface IQuery<TResponse>
    : IRequest<ApiResponse<TResponse>>
{ }
