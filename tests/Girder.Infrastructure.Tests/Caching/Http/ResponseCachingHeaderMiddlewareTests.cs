using Girder.Application.Interfaces;
using Girder.Infrastructure.Caching.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Girder.Infrastructure.Tests.Caching.Http;

[Trait("Category", "Unit")]
public class ResponseCachingHeaderMiddlewareTests
{
    private static ResponseCachingHeaderMiddleware CreateMiddleware(
        RequestDelegate? next = null,
        ICachePolicyProvider? policyProvider = null,
        IETagGenerator? etagGenerator = null,
        HttpCachingOptions? options = null)
    {
        next ??= _ => Task.CompletedTask;
        policyProvider ??= Substitute.For<ICachePolicyProvider>();
        etagGenerator ??= Substitute.For<IETagGenerator>();
        options ??= new HttpCachingOptions { Enabled = true };

        return new ResponseCachingHeaderMiddleware(
            next,
            policyProvider,
            etagGenerator,
            Options.Create(options),
            Substitute.For<ILogger<ResponseCachingHeaderMiddleware>>());
    }

    private static DefaultHttpContext CreateHttpContext(string method = "GET", string path = "/api/test")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new System.IO.MemoryStream();
        return context;
    }

    [Fact]
    public async Task InvokeAsync_WhenCachingDisabled_CallsNext()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(
            next: _ => { nextCalled = true; return Task.CompletedTask; },
            options: new HttpCachingOptions { Enabled = false });

        await middleware.InvokeAsync(CreateHttpContext());

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_WhenNoCachePolicyAndCachingEnabled_CallsNext()
    {
        var nextCalled = false;
        var policyProvider = Substitute.For<ICachePolicyProvider>();
        policyProvider.GetCachePolicy(Arg.Any<HttpContext>()).Returns((CachePolicyResult?)null);

        var middleware = CreateMiddleware(
            next: _ => { nextCalled = true; return Task.CompletedTask; },
            policyProvider: policyProvider);

        await middleware.InvokeAsync(CreateHttpContext());

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_WhenPolicyHasNoStore_SetsNoCacheHeader()
    {
        var policyProvider = Substitute.For<ICachePolicyProvider>();
        policyProvider.GetCachePolicy(Arg.Any<HttpContext>())
            .Returns(new CachePolicyResult { NoStore = true, CacheControl = "no-store" });

        var context = CreateHttpContext();
        var middleware = CreateMiddleware(policyProvider: policyProvider);

        await middleware.InvokeAsync(context);

        context.Response.Headers[HeaderNames.CacheControl].ToString().Should().Contain("no-store");
    }

    [Fact]
    public async Task InvokeAsync_WhenAddCacheStatusHeader_AddsXCacheStatusHeader()
    {
        var policyProvider = Substitute.For<ICachePolicyProvider>();
        policyProvider.GetCachePolicy(Arg.Any<HttpContext>())
            .Returns(new CachePolicyResult { NoStore = true, CacheControl = "no-store" });

        var context = CreateHttpContext();
        var middleware = CreateMiddleware(
            policyProvider: policyProvider,
            options: new HttpCachingOptions { Enabled = true, AddCacheStatusHeader = true });

        await middleware.InvokeAsync(context);

        context.Response.Headers["X-Cache-Status"].ToString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task InvokeAsync_WhenIfNoneMatchMatchesCachedEtag_Returns304()
    {
        var etag = "\"abc123\"";
        var policyProvider = Substitute.For<ICachePolicyProvider>();
        policyProvider.GetCachePolicy(Arg.Any<HttpContext>())
            .Returns(new CachePolicyResult
            {
                CacheControl = "public, max-age=300",
                MaxAge = 300,
                GenerateETag = true
            });

        var etagGenerator = Substitute.For<IETagGenerator>();
        etagGenerator.GetCachedETagAsync(Arg.Any<string>()).Returns(etag);
        etagGenerator.ValidateETag(etag, etag).Returns(true);

        var context = CreateHttpContext();
        context.Request.Headers[HeaderNames.IfNoneMatch] = etag;

        var middleware = CreateMiddleware(
            policyProvider: policyProvider,
            etagGenerator: etagGenerator);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status304NotModified);
    }

    [Fact]
    public async Task InvokeAsync_WhenPostRequest_WithCachePolicy_CallsNextDirectly()
    {
        var nextCalled = false;
        var policyProvider = Substitute.For<ICachePolicyProvider>();
        policyProvider.GetCachePolicy(Arg.Any<HttpContext>())
            .Returns(new CachePolicyResult
            {
                CacheControl = "no-store",
                NoStore = false,
                GenerateETag = false
            });

        var middleware = CreateMiddleware(
            next: _ => { nextCalled = true; return Task.CompletedTask; },
            policyProvider: policyProvider);

        await middleware.InvokeAsync(CreateHttpContext("POST"));

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_WhenGetRequest_WithEtagEnabled_ProcessesResponse()
    {
        var policyProvider = Substitute.For<ICachePolicyProvider>();
        policyProvider.GetCachePolicy(Arg.Any<HttpContext>())
            .Returns(new CachePolicyResult
            {
                CacheControl = "public, max-age=300",
                MaxAge = 300,
                GenerateETag = true
            });

        var etagGenerator = Substitute.For<IETagGenerator>();
        etagGenerator.GetCachedETagAsync(Arg.Any<string>()).Returns((string?)null);
        etagGenerator.GenerateETag(Arg.Any<byte[]>()).Returns("\"generated-etag\"");
        etagGenerator.StoreETagAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>())
            .Returns(Task.CompletedTask);

        var nextCalled = false;
        var middleware = CreateMiddleware(
            next: async ctx =>
            {
                nextCalled = true;
                ctx.Response.StatusCode = 200;
                await ctx.Response.WriteAsync("test body");
            },
            policyProvider: policyProvider,
            etagGenerator: etagGenerator);

        var context = CreateHttpContext();

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }
}
