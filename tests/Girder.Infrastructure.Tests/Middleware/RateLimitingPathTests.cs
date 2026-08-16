using Girder.Abstractions.Caching;
using System.Reflection;
using Girder.Infrastructure.Middleware;
using Microsoft.AspNetCore.Http;

namespace Girder.Infrastructure.Tests.Middleware;

/// <summary>
/// Tests for pure logic methods in DistributedRateLimitingMiddleware:
/// NormalizePath, IsIdSegment, MatchesPattern
/// </summary>
public class RateLimitingPathTests
{
    #region NormalizePath Tests

    [Theory]
    [InlineData("", "/")]
    [InlineData("/api/users", "/api/users")]
    [InlineData("/api/users/123", "/api/users/{id}")]
    [InlineData("/api/users/550e8400-e29b-41d4-a716-446655440000", "/api/users/{id}")]
    [InlineData("/API/Users", "/api/users")]
    [InlineData("/api/users/550e8400-e29b-41d4-a716-446655440000/profile", "/api/users/{id}/profile")]
    public void NormalizePath_ShouldNormalizeCorrectly(string input, string expected)
    {
        var result = InvokeNormalizePath(input);
        result.Should().Be(expected);
    }

    [Fact]
    public void NormalizePath_GuidSegment_ShouldBeReplacedWithId()
    {
        var guid = Guid.NewGuid().ToString();
        var result = InvokeNormalizePath($"/api/users/{guid}");
        result.Should().Be("/api/users/{id}");
    }

    [Fact]
    public void NormalizePath_NumericSegment_ShouldBeReplacedWithId()
    {
        var result = InvokeNormalizePath("/api/items/99999");
        result.Should().Be("/api/items/{id}");
    }

    [Fact]
    public void NormalizePath_ShortAlphanumericSegment_ShouldNotBeReplacedWithId()
    {
        // Short segments (<=10 chars) that aren't GUIDs or numbers should stay
        var result = InvokeNormalizePath("/api/users/profile");
        result.Should().Be("/api/users/profile");
    }

    [Fact]
    public void NormalizePath_LongAlphanumericWithDigits_ShouldBeReplacedWithId()
    {
        // Long alphanumeric segments with digits (>10 chars) are treated as IDs
        var result = InvokeNormalizePath("/api/users/abc123def456ghi");
        result.Should().Be("/api/users/{id}");
    }

    [Theory]
    [InlineData("/api/bookings", "/api/bookings")]
    [InlineData("/api/notifications", "/api/notifications")]
    [InlineData("/api/grade-levels", "/api/grade-levels")]
    [InlineData("/api/bookings/550e8400-e29b-41d4-a716-446655440000", "/api/bookings/{id}")]
    public void NormalizePath_LongPathWords_ShouldNotBeReplacedWithId(string input, string expected)
    {
        // Regression: pure-alphabetic words like "bookings" (12 chars) must NOT be treated as IDs
        var result = InvokeNormalizePath(input);
        result.Should().Be(expected);
    }

    #endregion

    #region IsIdSegment Tests

    [Theory]
    [InlineData("550e8400-e29b-41d4-a716-446655440000", true)]  // GUID
    [InlineData("12345", true)]                                    // Number
    [InlineData("abc123def456ghi", true)]                          // Long alphanumeric with digits
    [InlineData("profile", false)]                                 // Short word
    [InlineData("api", false)]                                     // Short word
    [InlineData("users", false)]                                   // Short word
    [InlineData("health", false)]                                  // Short word
    [InlineData("bookings", false)]                            // Regression: long pure-alpha word
    [InlineData("notifications", false)]                           // Regression: long pure-alpha word
    [InlineData("grade-levels", false)]                      // Regression: long word with hyphens but no digits
    [InlineData("abc_def_ghi_jkl", false)]                         // Long with underscores but no digits
    [InlineData("abc1def2ghi3jkl", true)]                          // Long with digits -> ID
    public void IsIdSegment_ShouldClassifyCorrectly(string segment, bool expected)
    {
        var result = InvokeIsIdSegment(segment);
        result.Should().Be(expected, $"'{segment}' should {(expected ? "" : "not ")}be classified as ID");
    }

    #endregion

    #region MatchesPattern Tests

    [Theory]
    [InlineData("/api/users", "/api/users", true)]
    [InlineData("/api/users", "/api/users*", true)]
    [InlineData("/api/users/123", "/api/users*", true)]
    [InlineData("/api/users/123/profile", "/api/users*", true)]
    [InlineData("/api/jobs", "/api/users*", false)]
    [InlineData("/API/USERS", "/api/users", true)]         // Case insensitive
    [InlineData("/api/jobs", "/api/jobs", true)]
    [InlineData("/api/jobs/search", "/api/jobs", false)]  // Exact match, no wildcard
    public void MatchesPattern_ShouldMatchCorrectly(string path, string pattern, bool expected)
    {
        var result = InvokeMatchesPattern(path, pattern);
        result.Should().Be(expected,
            $"Path '{path}' with pattern '{pattern}' should {(expected ? "" : "not ")}match");
    }

    #endregion

    #region Reflection Helpers

    private static string InvokeNormalizePath(string path)
    {
        var method = typeof(DistributedRateLimitingMiddleware)
            .GetMethod("NormalizePath", BindingFlags.NonPublic | BindingFlags.Instance);

        var instance = CreateMiddlewareInstance();
        return (string)method!.Invoke(instance, [path])!;
    }

    private static bool InvokeIsIdSegment(string segment)
    {
        var method = typeof(DistributedRateLimitingMiddleware)
            .GetMethod("IsIdSegment", BindingFlags.NonPublic | BindingFlags.Static);

        return (bool)method!.Invoke(null, [segment])!;
    }

    private static bool InvokeMatchesPattern(string path, string pattern)
    {
        var method = typeof(DistributedRateLimitingMiddleware)
            .GetMethod("MatchesPattern", BindingFlags.NonPublic | BindingFlags.Static);

        return (bool)method!.Invoke(null, [path, pattern])!;
    }

    private static DistributedRateLimitingMiddleware CreateMiddlewareInstance()
    {
        // Use reflection to create instance since constructor requires DI dependencies
        var constructor = typeof(DistributedRateLimitingMiddleware).GetConstructors()[0];
        var parameters = constructor.GetParameters();

        // Create minimal mock parameters
        var next = new RequestDelegate(_ => Task.CompletedTask);
        var rateLimitStore = Substitute.For<Girder.Abstractions.Caching.IDistributedRateLimitStore>();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<DistributedRateLimitingMiddleware>>();
        var options = Microsoft.Extensions.Options.Options.Create(new Girder.Infrastructure.Models.DistributedRateLimitingOptions());

        return new DistributedRateLimitingMiddleware(next, rateLimitStore, logger, options);
    }

    #endregion
}
