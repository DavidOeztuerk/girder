using Girder.Application.Models;
using MediatR;

namespace Girder.Application.Interfaces;

/// <summary>
/// Command handler with typed response
/// </summary>
/// <typeparam name="TCommand">Command type</typeparam>
/// <typeparam name="TResponse">Response type</typeparam>
public interface ICommandHandler<TCommand, TResponse>
    : IRequestHandler<TCommand, ApiResponse<TResponse>>
    where TCommand : ICommand<TResponse>
{ }
