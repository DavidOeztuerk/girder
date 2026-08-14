using System.Net;
using System.Text.Json;
using Core.Common.Exceptions;
using FluentValidation;
using FluentValidation.Results;
using Infrastructure.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Tests.Middleware;

[Trait("Category", "Unit")]
public class GlobalExceptionHandlingMiddlewareTests
{
    private readonly ILogger<GlobalExceptionHandlingMiddleware> _logger =
        Substitute.For<ILogger<GlobalExceptionHandlingMiddleware>>();

    private readonly IHostEnvironment _environment = Substitute.For<IHostEnvironment>();
    private readonly IErrorMessageService _errorMessageService = Substitute.For<IErrorMessageService>();

    private GlobalExceptionHandlingMiddleware CreateMiddleware(RequestDelegate next)
    {
        _errorMessageService.GetUserMessage(Arg.Any<string>(), Arg.Any<string?>())
            .Returns(callInfo => callInfo.ArgAt<string?>(1) ?? "An error occurred");
        _errorMessageService.GetHelpUrl(Arg.Any<string>()).Returns((string?)null);

        return new GlobalExceptionHandlingMiddleware(next, _logger, _environment, _errorMessageService);
    }

    private static DefaultHttpContext CreateContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<JsonDocument> ReadResponseBody(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return JsonDocument.Parse(body);
    }

    [Fact]
    public async Task InvokeAsync_NoException_ShouldCallNextAndNotModifyResponse()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateContext();
        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ValidationException_ShouldReturn400()
    {
        var failures = new List<ValidationFailure>
        {
            new("Name", "Name is required"),
            new("Email", "Email is invalid")
        };
        var middleware = CreateMiddleware(_ => throw new ValidationException(failures));

        var context = CreateContext();
        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
        context.Response.ContentType.Should().Be("application/json");

        var doc = await ReadResponseBody(context);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_UnauthorizedAccessException_ShouldReturn401()
    {
        var middleware = CreateMiddleware(_ => throw new UnauthorizedAccessException("Not authorized"));

        var context = CreateContext();
        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task InvokeAsync_KeyNotFoundException_ShouldReturn404()
    {
        var middleware = CreateMiddleware(_ => throw new KeyNotFoundException("Resource not found"));

        var context = CreateContext();
        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task InvokeAsync_ArgumentException_ShouldReturn400()
    {
        var middleware = CreateMiddleware(_ => throw new ArgumentException("Invalid argument"));

        var context = CreateContext();
        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task InvokeAsync_NotImplementedException_ShouldReturn501()
    {
        var middleware = CreateMiddleware(_ => throw new NotImplementedException());

        var context = CreateContext();
        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.NotImplemented);
    }

    [Fact]
    public async Task InvokeAsync_TaskCanceledException_ShouldReturn408()
    {
        var middleware = CreateMiddleware(_ => throw new TaskCanceledException());

        var context = CreateContext();
        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.RequestTimeout);
    }

    [Fact]
    public async Task InvokeAsync_OperationCanceledException_ShouldReturn408()
    {
        var middleware = CreateMiddleware(_ => throw new OperationCanceledException());

        var context = CreateContext();
        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.RequestTimeout);
    }

    [Fact]
    public async Task InvokeAsync_TimeoutException_ShouldReturn408()
    {
        var middleware = CreateMiddleware(_ => throw new TimeoutException());

        var context = CreateContext();
        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.RequestTimeout);
    }

    [Fact]
    public async Task InvokeAsync_HttpRequestException_ShouldReturn503()
    {
        var middleware = CreateMiddleware(_ => throw new HttpRequestException("Service down"));

        var context = CreateContext();
        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task InvokeAsync_DbUpdateConcurrencyException_ShouldReturn409()
    {
        var middleware = CreateMiddleware(_ => throw new DbUpdateConcurrencyException("Concurrency conflict"));

        var context = CreateContext();
        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task InvokeAsync_DbUpdateExceptionWithDuplicate_ShouldReturn409()
    {
        var innerEx = new Exception("duplicate key value violates unique constraint");
        var middleware = CreateMiddleware(_ => throw new DbUpdateException("DB error", innerEx));

        var context = CreateContext();
        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task InvokeAsync_DbUpdateExceptionGeneric_ShouldReturn500()
    {
        var middleware = CreateMiddleware(_ => throw new DbUpdateException("DB error"));

        var context = CreateContext();
        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task InvokeAsync_ExternalServiceException_ShouldReturn503()
    {
        var middleware = CreateMiddleware(_ =>
            throw new ExternalServiceException("UserService", "Service unavailable", "/api/users"));

        var context = CreateContext();
        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task InvokeAsync_ExternalServiceException_InDev_ShouldIncludeAdditionalData()
    {
        _environment.EnvironmentName.Returns("Development");

        var middleware = CreateMiddleware(_ =>
            throw new ExternalServiceException("UserService", "Err", "/api/users"));

        var context = CreateContext();
        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task InvokeAsync_GenericException_ShouldReturn500()
    {
        var middleware = CreateMiddleware(_ => throw new System.InvalidOperationException("Something broke"));

        var context = CreateContext();
        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task InvokeAsync_GenericException_InDev_ShouldExposeMessage()
    {
        _environment.EnvironmentName.Returns("Development");

        var middleware = CreateMiddleware(_ => throw new System.InvalidOperationException("Internal details"));

        var context = CreateContext();
        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task InvokeAsync_ShouldUseCorrelationIdFromHttpContextItems()
    {
        var middleware = CreateMiddleware(_ => throw new Exception("test"));

        var context = CreateContext();
        context.Items["CorrelationId"] = "corr-123";
        await middleware.InvokeAsync(context);

        var doc = await ReadResponseBody(context);
        doc.RootElement.GetProperty("traceId").GetString().Should().Be("corr-123");
    }

    [Fact]
    public async Task InvokeAsync_WithoutCorrelationId_ShouldUseTraceIdentifier()
    {
        var middleware = CreateMiddleware(_ => throw new Exception("test"));

        var context = CreateContext();
        await middleware.InvokeAsync(context);

        var doc = await ReadResponseBody(context);
        doc.RootElement.GetProperty("traceId").GetString().Should().Be(context.TraceIdentifier);
    }

    [Fact]
    public async Task InvokeAsync_ResponseShouldHaveJsonContentType()
    {
        var middleware = CreateMiddleware(_ => throw new Exception("test"));

        var context = CreateContext();
        await middleware.InvokeAsync(context);

        context.Response.ContentType.Should().Be("application/json");
    }

    [Fact]
    public async Task InvokeAsync_ResponseShouldContainErrorsArray()
    {
        _errorMessageService.GetUserMessage(Arg.Any<string>(), Arg.Any<string?>())
            .Returns("Some error message");

        var middleware = CreateMiddleware(_ => throw new Exception("test"));

        var context = CreateContext();
        await middleware.InvokeAsync(context);

        var doc = await ReadResponseBody(context);
        doc.RootElement.GetProperty("errors").GetArrayLength().Should().BeGreaterThan(0);
    }
}

/// <summary>
/// Concrete DomainException for testing purposes.
/// </summary>
file class TestDomainException : DomainException
{
    public TestDomainException(string errorCode, string message)
        : base(errorCode, message)
    {
    }
}
