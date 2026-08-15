using Girder.Application.Models;
using MediatR;

namespace Girder.Application.Interfaces;

/// <summary>
/// Command with typed response
/// </summary>
/// <typeparam name="TResponse">Response type</typeparam>
public interface ICommand<TResponse>
    : IRequest<ApiResponse<TResponse>>
{ }
