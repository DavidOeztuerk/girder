using Microsoft.Extensions.Logging;
using CQRS.Interfaces;
using CQRS.Models;
using Core.Common.Exceptions;

namespace CQRS.Handlers;

/// <summary>
/// Base class for all query handlers
/// </summary>
/// <typeparam name="TQuery">Query type</typeparam>
/// <typeparam name="TResponse">Response type</typeparam>
public abstract class BaseQueryHandler<TQuery, TResponse>(
    ILogger logger) 
    : IQueryHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    protected readonly ILogger Logger = logger;

    public abstract Task<ApiResponse<TResponse>> Handle(TQuery request, CancellationToken cancellationToken);

    protected ApiResponse<TResponse> Success(TResponse data, string? message = null)
    {
        return ApiResponse<TResponse>.SuccessResult(data, message);
    }

    protected ApiResponse<TResponse> Error(string error, string? errorCode = null, string? helpUrl = null)
    {
        Logger.LogError("Query {QueryType} failed: {Error}", typeof(TQuery).Name, error);
        // Auto-populate HelpUrl if not provided but ErrorCode is
        if (helpUrl == null && errorCode != null)
        {
            helpUrl = HelpUrls.GetHelpUrl(errorCode);
        }
        return ApiResponse<TResponse>.ErrorResult(error, null, errorCode, helpUrl);
    }

    protected ApiResponse<TResponse> NotFound(string? message = null, string? errorCode = null, string? helpUrl = null)
    {
        var errorMessage = message ?? $"{typeof(TResponse).Name} not found";
        Logger.LogWarning("Query {QueryType}: {Message}", typeof(TQuery).Name, errorMessage);
        var finalErrorCode = errorCode ?? ErrorCodes.ResourceNotFound;
        // Auto-populate HelpUrl if not provided
        if (helpUrl == null)
        {
            helpUrl = HelpUrls.GetHelpUrl(finalErrorCode);
        }
        return ApiResponse<TResponse>.ErrorResult(errorMessage, null, finalErrorCode, helpUrl);
    }

    protected ApiResponse<TResponse> BadRequest(string message, string? errorCode = null, string? helpUrl = null)
    {
        Logger.LogWarning("Query {QueryType} bad request: {Message}", typeof(TQuery).Name, message);
        var finalErrorCode = errorCode ?? ErrorCodes.InvalidInput;
        // Auto-populate HelpUrl if not provided
        if (helpUrl == null)
        {
            helpUrl = HelpUrls.GetHelpUrl(finalErrorCode);
        }
        return ApiResponse<TResponse>.ErrorResult(message, null, finalErrorCode, helpUrl);
    }
}
