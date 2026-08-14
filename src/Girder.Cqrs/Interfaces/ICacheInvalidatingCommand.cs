namespace CQRS.Interfaces;

/// <summary>
/// Interface for commands that should invalidate cache after successful execution
/// </summary>
public interface ICacheInvalidatingCommand
{
    /// <summary>
    /// Patterns to invalidate (e.g., "skills:*", "skill:{SkillId}:*")
    /// Placeholders like {SkillId} will be replaced with actual values from the command
    /// </summary>
    string[] InvalidationPatterns { get; }

    /// <summary>
    /// Optional: Specific cache keys to invalidate
    /// </summary>
    string[]? InvalidationKeys => null;

    /// <summary>
    /// Optional: Tags to invalidate by
    /// </summary>
    string[]? InvalidationTags => null;

    /// <summary>
    /// Optional: HTTP API path patterns for ETag invalidation (e.g., "/api/skills*")
    /// When set, ETags matching these patterns will be invalidated along with CQRS cache
    /// </summary>
    string[]? ETagInvalidationPatterns => null;

    /// <summary>
    /// Whether to invalidate only on success (default: true)
    /// </summary>
    bool InvalidateOnlyOnSuccess => true;
}