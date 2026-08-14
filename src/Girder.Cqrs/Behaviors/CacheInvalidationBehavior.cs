using MediatR;
using Microsoft.Extensions.Logging;
using CQRS.Interfaces;
using System.Reflection;
using Infrastructure.Caching;
using Infrastructure.Caching.Http;

namespace CQRS.Behaviors;

/// <summary>
/// Cache invalidation behavior for commands
/// </summary>
public class CacheInvalidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IDistributedCacheService? _cacheService;
    private readonly IETagGenerator? _etagGenerator;
    private readonly ILogger<CacheInvalidationBehavior<TRequest, TResponse>> _logger;

    public CacheInvalidationBehavior(
        IDistributedCacheService? cacheService,
        IETagGenerator? etagGenerator,
        ILogger<CacheInvalidationBehavior<TRequest, TResponse>> logger)
    {
        _cacheService = cacheService;
        _etagGenerator = etagGenerator;
        _logger = logger;

        _logger.LogDebug("CacheInvalidationBehavior initialized for {RequestType} -> {ResponseType}",
            typeof(TRequest).Name, typeof(TResponse).Name);
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Check if this is a cache-invalidating command
        if (request is not ICacheInvalidatingCommand invalidatingCommand)
        {
            return await next(cancellationToken);
        }

        _logger.LogDebug("Processing cache invalidation for {CommandType} with {PatternCount} patterns",
            typeof(TRequest).Name, invalidatingCommand.InvalidationPatterns?.Length ?? 0);

        if (_cacheService == null)
        {
            _logger.LogDebug("Cache service not available, skipping cache invalidation for {CommandType}", typeof(TRequest).Name);
            return await next(cancellationToken);
        }

        // Execute the command
        TResponse response;
        bool success;

        try
        {
            response = await next(cancellationToken);
            success = IsSuccessResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Command execution failed for {CommandType}", typeof(TRequest).Name);
            throw;
        }

        // Invalidate cache if appropriate
        if (success || !invalidatingCommand.InvalidateOnlyOnSuccess)
        {
            await InvalidateCache(invalidatingCommand, request, cancellationToken);
        }

        return response;
    }

    private async Task InvalidateCache(
        ICacheInvalidatingCommand invalidatingCommand,
        TRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var invalidationTasks = new List<Task>();

            // Invalidate patterns using IDistributedCacheService
            if (invalidatingCommand.InvalidationPatterns?.Any() == true)
            {
                foreach (var pattern in invalidatingCommand.InvalidationPatterns)
                {
                    var processedPattern = ProcessPattern(pattern, request);
                    _logger.LogDebug("Invalidating cache pattern: {Pattern}", processedPattern);
                    invalidationTasks.Add(_cacheService!.RemoveByPatternAsync(processedPattern, cancellationToken));
                }
            }

            // Invalidate specific keys
            if (invalidatingCommand.InvalidationKeys?.Any() == true)
            {
                foreach (var key in invalidatingCommand.InvalidationKeys)
                {
                    var processedKey = ProcessPattern(key, request);
                    _logger.LogDebug("Invalidating cache key: {Key}", processedKey);
                    invalidationTasks.Add(_cacheService!.RemoveAsync(processedKey, cancellationToken));
                }
            }

            // Invalidate by tags
            if (invalidatingCommand.InvalidationTags?.Any() == true)
            {
                foreach (var tag in invalidatingCommand.InvalidationTags)
                {
                    _logger.LogDebug("Invalidating cache by tag: {Tag}", tag);
                    invalidationTasks.Add(_cacheService!.RemoveByTagAsync(tag, cancellationToken));
                }
            }

            await Task.WhenAll(invalidationTasks);

            // CRITICAL: Invalidate ETags to prevent 304 Not Modified with stale data
            if (_etagGenerator != null)
            {
                var etagPatterns = invalidatingCommand.ETagInvalidationPatterns;

                // If no explicit ETag patterns, derive from cache patterns
                if (etagPatterns == null || !etagPatterns.Any())
                {
                    etagPatterns = DeriveETagPatternsFromCachePatterns(invalidatingCommand.InvalidationPatterns);
                }

                if (etagPatterns?.Any() == true)
                {
                    var etagTasks = new List<Task>();
                    foreach (var pattern in etagPatterns)
                    {
                        var processedPattern = ProcessPattern(pattern, request);
                        _logger.LogDebug("Invalidating ETag pattern: {Pattern}", processedPattern);
                        etagTasks.Add(_etagGenerator.InvalidateETagsByPatternAsync(processedPattern, cancellationToken));
                    }
                    await Task.WhenAll(etagTasks);
                }
            }

            _logger.LogDebug(
                "Cache invalidation completed for {CommandType} - Patterns: {PatternCount}, Keys: {KeyCount}, Tags: {TagCount}",
                typeof(TRequest).Name,
                invalidatingCommand.InvalidationPatterns?.Length ?? 0,
                invalidatingCommand.InvalidationKeys?.Length ?? 0,
                invalidatingCommand.InvalidationTags?.Length ?? 0);
        }
        catch (Exception ex)
        {
            // Log but don't fail the command
            _logger.LogError(ex, "Cache invalidation failed for {CommandType}", typeof(TRequest).Name);
        }
    }

    /// <summary>
    /// Derives ETag invalidation patterns from CQRS cache patterns.
    /// Maps common cache key patterns to their corresponding API path patterns.
    /// IMPORTANT: Must include ALL route patterns from ocelot.json that serve this data!
    /// </summary>
    private static string[]? DeriveETagPatternsFromCachePatterns(string[]? cachePatterns)
    {
        if (cachePatterns == null || !cachePatterns.Any())
            return null;

        var etagPatterns = new HashSet<string>();

        foreach (var pattern in cachePatterns)
        {
            var lowerPattern = pattern.ToLowerInvariant();

            // IMPORTANT: Gateway rewrites URLs, so we need BOTH patterns:
            // - /api/skills* (Gateway-level ETags)
            // - /skills* (Service-level ETags, after Gateway URL rewrite)
            //
            // Check ocelot.json for ALL DownstreamPathTemplate values!

            if (lowerPattern.Contains("skill"))
            {
                // SkillService routes (see ocelot.json lines 585-767)
                etagPatterns.Add("/api/skills*");
                etagPatterns.Add("/skills*");
                etagPatterns.Add("/api/categories*");
                etagPatterns.Add("/categories*");
                etagPatterns.Add("/api/proficiency-levels*");
                etagPatterns.Add("/proficiency-levels*");
                // Analytics & recommendations (different downstream paths!)
                etagPatterns.Add("/api/skills/analytics*");
                etagPatterns.Add("/analytics*");
                etagPatterns.Add("/api/skills/recommendations*");
                etagPatterns.Add("/recommendations*");
            }
            if (lowerPattern.Contains("appointment"))
            {
                // AppointmentService routes (see ocelot.json lines 968-1086)
                etagPatterns.Add("/api/appointments*");
                etagPatterns.Add("/appointments*");
                etagPatterns.Add("/api/my/appointments*");
                etagPatterns.Add("/my/appointments*");
            }
            if (lowerPattern.Contains("user") || lowerPattern.Contains("profile"))
            {
                // UserService routes (see ocelot.json lines 154-583)
                etagPatterns.Add("/api/users*");
                etagPatterns.Add("/users*");
                // Note: /api/users/profile does NOT get rewritten (stays /api/users/profile)
            }
            if (lowerPattern.Contains("favorite"))
            {
                // UserService favorites (downstream: /users/skills/favorites)
                etagPatterns.Add("/api/users/favorites*");
                etagPatterns.Add("/users/skills/favorites*");
            }
            if (lowerPattern.Contains("permission"))
            {
                // UserService permissions
                etagPatterns.Add("/api/users/permissions*");
                etagPatterns.Add("/users/permissions*");
                etagPatterns.Add("/api/permission*");
                etagPatterns.Add("/permission*");
            }
            if (lowerPattern.Contains("match"))
            {
                // MatchmakingService routes (see ocelot.json lines 768-966)
                etagPatterns.Add("/api/matchmaking*");
                etagPatterns.Add("/matchmaking*");
                etagPatterns.Add("/api/matches*");
                etagPatterns.Add("/matches*");
                // Statistics has different downstream path
                etagPatterns.Add("/analytics/statistics*");
            }
            // NOTE: VideoCall endpoints are EXCLUDED from caching (no-store)
            if (lowerPattern.Contains("notification"))
            {
                // NotificationService routes (see ocelot.json lines 1087-1379)
                etagPatterns.Add("/api/notifications*");
                etagPatterns.Add("/notifications*");
                // Preferences & templates have different downstream paths
                etagPatterns.Add("/preferences*");
                etagPatterns.Add("/templates*");
            }
        }

        return etagPatterns.Count > 0 ? etagPatterns.ToArray() : null;
    }


    private string ProcessPattern(string pattern, TRequest request)
    {
        var result = pattern;
        var properties = request.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var property in properties)
        {
            var placeholder = $"{{{property.Name}}}";
            if (result.Contains(placeholder))
            {
                var value = property.GetValue(request)?.ToString() ?? "";
                result = result.Replace(placeholder, value, StringComparison.OrdinalIgnoreCase);
            }
        }

        return result;
    }

    private bool IsSuccessResponse(TResponse response)
    {
        if (response == null)
            return false;

        // Check for common success indicators
        var type = response.GetType();
        
        // Check for Success property
        var successProperty = type.GetProperty("Success") ?? type.GetProperty("IsSuccess");
        if (successProperty?.PropertyType == typeof(bool))
        {
            return (bool)(successProperty.GetValue(response) ?? false);
        }

        // Check for Errors property
        var errorsProperty = type.GetProperty("Errors");
        if (errorsProperty != null)
        {
            var errors = errorsProperty.GetValue(response);
            if (errors is IEnumerable<object> errorList)
            {
                return !errorList.Any();
            }
        }

        // Default to true if we can't determine
        return true;
    }

}
