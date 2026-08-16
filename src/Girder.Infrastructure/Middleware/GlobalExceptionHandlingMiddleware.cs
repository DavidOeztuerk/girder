using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using FluentValidation;
using Girder.Infrastructure.Models;
using Girder.Abstractions.Diagnostics;
using Girder.Core.Exceptions;
using Microsoft.Extensions.Hosting;

namespace Girder.Infrastructure.Middleware;

public class GlobalExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionHandlingMiddleware> logger,
    IHostEnvironment environment,
    IErrorMessageService? errorMessageService = null,
    IEnumerable<IExceptionResponseMapper>? mappers = null)
{
    private readonly RequestDelegate _next = next;
    private readonly ILogger<GlobalExceptionHandlingMiddleware> _logger = logger;
    private readonly IHostEnvironment _environment = environment;
    private readonly IErrorMessageService _errorMessageService = errorMessageService ?? new ErrorMessageService();

    /// <summary>
    /// Mappers for exceptions this middleware has no reason to know — a
    /// database driver's, a broker client's. Registered by provider packages;
    /// asked before the built-in cases, so a provider can be specific where
    /// this class can only be generic.
    /// </summary>
    private readonly IReadOnlyList<IExceptionResponseMapper> _mappers =
        mappers?.ToArray() ?? [];

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            var correlationId = context.Items["CorrelationId"]?.ToString() 
                ?? context.TraceIdentifier;
                
            // Log based on exception type
            var logLevel = ex switch
            {
                DomainException => LogLevel.Information, // Business exceptions are expected
                ValidationException => LogLevel.Information, // Validation errors are expected
                UnauthorizedAccessException => LogLevel.Warning, // Security-related
                TaskCanceledException or OperationCanceledException => LogLevel.Debug, // Normal cancellations
                TimeoutException => LogLevel.Warning, // Performance issue
                HttpRequestException => LogLevel.Warning, // External service issue
                _ => LogLevel.Error // Unexpected exceptions
            };
            
            if (logLevel == LogLevel.Error)
            {
                _logger.LogError(ex, 
                    "An unhandled exception occurred. CorrelationId: {CorrelationId}, Path: {Path}", 
                    correlationId, 
                    context.Request.Path);
            }
            else if (logLevel == LogLevel.Warning)
            {
                _logger.LogWarning(
                    "Expected exception occurred: {ExceptionType}. CorrelationId: {CorrelationId}, Path: {Path}, Message: {Message}", 
                    ex.GetType().Name,
                    correlationId, 
                    context.Request.Path,
                    ex.Message);
            }
            else
            {
                _logger.LogInformation(
                    "Business exception occurred: {ExceptionType}. CorrelationId: {CorrelationId}, Path: {Path}", 
                    ex.GetType().Name,
                    correlationId, 
                    context.Request.Path);
            }
                
            await HandleExceptionAsync(context, ex, correlationId);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception, string correlationId)
    {
        var response = context.Response;
        response.ContentType = "application/json";

        // Create ApiResponse structure for consistency with normal responses
        var (statusCode, errors, message, errorCode, helpUrl) = GetErrorDetails(exception, correlationId);
        response.StatusCode = statusCode;

        var apiResponse = new
        {
            Success = false,
            Data = (object?)null,
            Message = message,
            Errors = errors,
            Timestamp = DateTime.UtcNow,
            TraceId = correlationId,
            ErrorCode = errorCode,
            HelpUrl = helpUrl
        };

        var jsonResponse = JsonSerializer.Serialize(apiResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await response.WriteAsync(jsonResponse);
    }

    private (int statusCode, List<string> errors, string message, string? errorCode, string? helpUrl) GetErrorDetails(Exception exception, string correlationId)
    {
        foreach (var mapper in _mappers)
        {
            if (mapper.Map(exception) is not { } mapped) continue;

            return ((int)mapped.Status,
                [mapped.Title],
                _errorMessageService.GetUserMessage(mapped.ErrorCode, mapped.FallbackDetail),
                mapped.ErrorCode,
                _errorMessageService.GetHelpUrl(mapped.ErrorCode));
        }

        var errorResponse = exception switch
        {
            // Domain Exceptions
            DomainException domainEx => new ErrorResponse
            {
                Title = "Business Rule Violation",
                Status = domainEx.GetHttpStatusCode(),
                Detail = _errorMessageService.GetUserMessage(domainEx.ErrorCode, domainEx.Message),
                ErrorCode = domainEx.ErrorCode,
                AdditionalData = _environment.IsDevelopment() ? domainEx.AdditionalData : null,
                CorrelationId = correlationId,
                HelpUrl = _errorMessageService.GetHelpUrl(domainEx.ErrorCode)
            },
            
            // Validation Exceptions
            ValidationException validationEx => new ErrorResponse
            {
                Title = "Validation Error",
                Status = (int)HttpStatusCode.BadRequest,
                ErrorCode = ErrorCodes.ValidationFailed,
                Detail = _errorMessageService.GetUserMessage(ErrorCodes.ValidationFailed),
                Errors = validationEx.Errors.GroupBy(x => x.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(x => x.ErrorMessage).ToArray()),
                CorrelationId = correlationId,
                HelpUrl = _errorMessageService.GetHelpUrl(ErrorCodes.ValidationFailed)
            },

            // External Service Exceptions
            ExternalServiceException serviceEx => new ErrorResponse
            {
                Title = "External Service Error",
                Status = serviceEx.GetHttpStatusCode(),
                ErrorCode = serviceEx.ErrorCode,
                Detail = _errorMessageService.GetUserMessage(serviceEx.ErrorCode, serviceEx.Message),
                AdditionalData = _environment.IsDevelopment() 
                    ? new Dictionary<string, object>
                    {
                        ["Service"] = serviceEx.ServiceName,
                        ["Endpoint"] = serviceEx.Endpoint ?? "Unknown"
                    }
                    : null,
                CorrelationId = correlationId,
                HelpUrl = _errorMessageService.GetHelpUrl(serviceEx.ErrorCode)
            },
            
            HttpRequestException httpEx => new ErrorResponse
            {
                Title = "External Service Unavailable",
                Status = (int)HttpStatusCode.ServiceUnavailable,
                ErrorCode = ErrorCodes.ServiceUnavailable,
                Detail = _errorMessageService.GetUserMessage(ErrorCodes.ServiceUnavailable, "An external service is currently unavailable. Please try again later."),
                CorrelationId = correlationId,
                HelpUrl = _errorMessageService.GetHelpUrl(ErrorCodes.ServiceUnavailable)
            },
            
            // Timeout Exceptions
            TaskCanceledException => new ErrorResponse
            {
                Title = "Operation Cancelled",
                Status = (int)HttpStatusCode.RequestTimeout,
                ErrorCode = ErrorCodes.ServiceTimeout,
                Detail = _errorMessageService.GetUserMessage(ErrorCodes.ServiceTimeout, "The operation was cancelled or timed out."),
                CorrelationId = correlationId,
                HelpUrl = _errorMessageService.GetHelpUrl(ErrorCodes.ServiceTimeout)
            },
            
            OperationCanceledException => new ErrorResponse
            {
                Title = "Request Timeout",
                Status = (int)HttpStatusCode.RequestTimeout,
                ErrorCode = ErrorCodes.ServiceTimeout,
                Detail = _errorMessageService.GetUserMessage(ErrorCodes.ServiceTimeout, "The request took too long to complete."),
                CorrelationId = correlationId,
                HelpUrl = _errorMessageService.GetHelpUrl(ErrorCodes.ServiceTimeout)
            },
            
            TimeoutException => new ErrorResponse
            {
                Title = "Request Timeout",
                Status = (int)HttpStatusCode.RequestTimeout,
                ErrorCode = ErrorCodes.ConnectionTimeout,
                Detail = _errorMessageService.GetUserMessage(ErrorCodes.ConnectionTimeout, "The request timed out"),
                CorrelationId = correlationId,
                HelpUrl = _errorMessageService.GetHelpUrl(ErrorCodes.ConnectionTimeout)
            },
            
            // Standard Exceptions
            UnauthorizedAccessException => new ErrorResponse
            {
                Title = "Unauthorized",
                Status = (int)HttpStatusCode.Unauthorized,
                ErrorCode = ErrorCodes.Unauthorized,
                Detail = _errorMessageService.GetUserMessage(ErrorCodes.Unauthorized, "You are not authorized to access this resource"),
                CorrelationId = correlationId,
                HelpUrl = _errorMessageService.GetHelpUrl(ErrorCodes.Unauthorized)
            },
            
            ArgumentException argEx => new ErrorResponse
            {
                Title = "Bad Request",
                Status = (int)HttpStatusCode.BadRequest,
                ErrorCode = ErrorCodes.InvalidInput,
                Detail = _errorMessageService.GetUserMessage(ErrorCodes.InvalidInput, argEx.Message),
                CorrelationId = correlationId,
                HelpUrl = _errorMessageService.GetHelpUrl(ErrorCodes.InvalidInput)
            },
            
            KeyNotFoundException => new ErrorResponse
            {
                Title = "Not Found",
                Status = (int)HttpStatusCode.NotFound,
                ErrorCode = ErrorCodes.ResourceNotFound,
                Detail = _errorMessageService.GetUserMessage(ErrorCodes.ResourceNotFound, "The requested resource was not found"),
                CorrelationId = correlationId,
                HelpUrl = _errorMessageService.GetHelpUrl(ErrorCodes.ResourceNotFound)
            },
            
            NotImplementedException => new ErrorResponse
            {
                Title = "Not Implemented",
                Status = (int)HttpStatusCode.NotImplemented,
                ErrorCode = ErrorCodes.FeatureNotAvailable,
                Detail = _errorMessageService.GetUserMessage(ErrorCodes.FeatureNotAvailable, "This feature is not yet implemented"),
                CorrelationId = correlationId,
                HelpUrl = _errorMessageService.GetHelpUrl(ErrorCodes.FeatureNotAvailable)
            },
            
            // Default
            _ => new ErrorResponse
            {
                Title = "Internal Server Error",
                Status = (int)HttpStatusCode.InternalServerError,
                ErrorCode = ErrorCodes.InternalError,
                Detail = _environment.IsDevelopment() 
                    ? exception.Message 
                    : _errorMessageService.GetUserMessage(ErrorCodes.InternalError, "An unexpected error occurred. Please try again later."),
                CorrelationId = correlationId,
                HelpUrl = _errorMessageService.GetHelpUrl(ErrorCodes.InternalError),
                AdditionalData = _environment.IsDevelopment() 
                    ? new Dictionary<string, object> { ["StackTrace"] = exception.StackTrace ?? "" }
                    : null
            }
        };

        // Return status code, errors list, and main message for ApiResponse format
        var errors = new List<string>();
        
        if (errorResponse.Errors != null && errorResponse.Errors.Any())
        {
            // Flatten validation errors
            foreach (var errorGroup in errorResponse.Errors)
            {
                errors.AddRange(errorGroup.Value);
            }
        }
        else if (!string.IsNullOrEmpty(errorResponse.Detail))
        {
            errors.Add(errorResponse.Detail);
        }

        return (errorResponse.Status, errors, errorResponse.Detail ?? errorResponse.Title, errorResponse.ErrorCode, errorResponse.HelpUrl);
    }
}
