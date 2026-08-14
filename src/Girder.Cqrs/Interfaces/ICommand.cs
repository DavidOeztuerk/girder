using CQRS.Models;
using MediatR;

namespace CQRS.Interfaces;

/// <summary>
/// Command with typed response
/// </summary>
/// <typeparam name="TResponse">Response type</typeparam>
public interface ICommand<TResponse>
    : IRequest<ApiResponse<TResponse>>
{ }
