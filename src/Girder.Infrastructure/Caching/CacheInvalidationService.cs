using Infrastructure.Caching.PlaceholderEvents;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Caching
{
    /// <summary>
    /// Service for managing cache invalidation based on domain events
    /// </summary>
    public class CacheInvalidationService : ICacheInvalidationService
    {
        private readonly IDistributedCacheService _cacheService;
        private readonly ILogger<CacheInvalidationService> _logger;
        private readonly Dictionary<Type, List<CacheInvalidationRule>> _invalidationRules = new();

        public CacheInvalidationService(
            IDistributedCacheService cacheService,
            ILogger<CacheInvalidationService> logger)
        {
            _cacheService = cacheService;
            _logger = logger;

            RegisterDefaultInvalidationRules();
        }

        public void RegisterInvalidationRule<TEvent>(string keyPattern, params string[] tags)
            where TEvent : IDomainEvent
        {
            var eventType = typeof(TEvent);

            if (!_invalidationRules.ContainsKey(eventType))
            {
                _invalidationRules[eventType] = new List<CacheInvalidationRule>();
            }

            _invalidationRules[eventType].Add(new CacheInvalidationRule
            {
                KeyPattern = keyPattern,
                Tags = tags.ToHashSet(),
                InvalidationType = CacheInvalidationType.Pattern
            });

            _logger.LogDebug("Registered cache invalidation rule for event {EventType} with pattern {Pattern}",
                eventType.Name, keyPattern);
        }

        public void RegisterTagInvalidationRule<TEvent>(params string[] tags)
            where TEvent : IDomainEvent
        {
            var eventType = typeof(TEvent);

            if (!_invalidationRules.ContainsKey(eventType))
            {
                _invalidationRules[eventType] = new List<CacheInvalidationRule>();
            }

            _invalidationRules[eventType].Add(new CacheInvalidationRule
            {
                Tags = tags.ToHashSet(),
                InvalidationType = CacheInvalidationType.Tag
            });

            _logger.LogDebug("Registered tag invalidation rule for event {EventType} with tags {Tags}",
                eventType.Name, string.Join(", ", tags));
        }

        public async Task InvalidateAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent
        {
            var eventType = typeof(TEvent);

            if (!_invalidationRules.ContainsKey(eventType))
            {
                return;
            }

            var rules = _invalidationRules[eventType];
            var invalidationTasks = new List<Task>();

            foreach (var rule in rules)
            {
                switch (rule.InvalidationType)
                {
                    case CacheInvalidationType.Pattern:
                        if (!string.IsNullOrEmpty(rule.KeyPattern))
                        {
                            var pattern = ProcessKeyPattern(rule.KeyPattern, domainEvent);
                            invalidationTasks.Add(_cacheService.RemoveByPatternAsync(pattern, cancellationToken));
                        }
                        break;

                    case CacheInvalidationType.Tag:
                        if (rule.Tags.Any())
                        {
                            var tags = ProcessTags(rule.Tags, domainEvent);
                            invalidationTasks.Add(_cacheService.RemoveByTagsAsync(tags, cancellationToken));
                        }
                        break;

                    case CacheInvalidationType.Key:
                        if (!string.IsNullOrEmpty(rule.KeyPattern))
                        {
                            var key = ProcessKeyPattern(rule.KeyPattern, domainEvent);
                            invalidationTasks.Add(_cacheService.RemoveAsync(key, cancellationToken));
                        }
                        break;
                }
            }

            if (invalidationTasks.Any())
            {
                await Task.WhenAll(invalidationTasks);
                _logger.LogDebug("Cache invalidation completed for event {EventType} with {RuleCount} rules",
                    eventType.Name, rules.Count);
            }
        }

        public async Task InvalidateCacheAsync(CacheInvalidationRequest request, CancellationToken cancellationToken = default)
        {
            var tasks = new List<Task>();

            // Invalidate by keys
            if (request.Keys?.Any() == true)
            {
                tasks.Add(_cacheService.RemoveAsync(request.Keys, cancellationToken));
            }

            // Invalidate by patterns
            if (request.Patterns?.Any() == true)
            {
                foreach (var pattern in request.Patterns)
                {
                    tasks.Add(_cacheService.RemoveByPatternAsync(pattern, cancellationToken));
                }
            }

            // Invalidate by tags
            if (request.Tags?.Any() == true)
            {
                tasks.Add(_cacheService.RemoveByTagsAsync(request.Tags, cancellationToken));
            }

            if (tasks.Any())
            {
                await Task.WhenAll(tasks);
                _logger.LogInformation("Manual cache invalidation completed: {KeyCount} keys, {PatternCount} patterns, {TagCount} tags",
                    request.Keys?.Count ?? 0, request.Patterns?.Count ?? 0, request.Tags?.Count ?? 0);
            }
        }

        public CacheInvalidationStatistics GetStatistics()
        {
            return new CacheInvalidationStatistics
            {
                RegisteredEventTypes = _invalidationRules.Keys.Select(t => t.Name).ToList(),
                TotalRules = _invalidationRules.Values.SelectMany(r => r).Count(),
                RulesByEventType = _invalidationRules.ToDictionary(
                    kvp => kvp.Key.Name,
                    kvp => kvp.Value.Count),
                LastUpdated = DateTime.UtcNow
            };
        }

        private void RegisterDefaultInvalidationRules()
        {
            // User-related cache invalidation
            RegisterInvalidationRule<UserCreatedEvent>("user:*", "users", "user-list");
            RegisterInvalidationRule<UserUpdatedEvent>("user:{UserId}:*", "users", "user-{UserId}");
            RegisterInvalidationRule<UserDeletedEvent>("user:{UserId}:*", "users", "user-{UserId}");

            // Skill-related cache invalidation
            RegisterInvalidationRule<SkillCreatedEvent>("skill:*", "skills", "skill-list");
            RegisterInvalidationRule<SkillUpdatedEvent>("skill:{SkillId}:*", "skills", "skill-{SkillId}");
            RegisterInvalidationRule<SkillDeletedEvent>("skill:{SkillId}:*", "skills", "skill-{SkillId}");

            // Appointment-related cache invalidation
            RegisterInvalidationRule<AppointmentCreatedEvent>("appointment:*", "appointments");
            RegisterInvalidationRule<AppointmentUpdatedEvent>("appointment:{AppointmentId}:*", "appointments", "appointment-{AppointmentId}");

            // Match-related cache invalidation
            RegisterInvalidationRule<MatchRequestCreatedEvent>("match:*", "matches");
            RegisterInvalidationRule<MatchRequestAcceptedEvent>("match:{MatchId}:*", "matches", "match-{MatchId}");

            _logger.LogInformation("Registered {Count} default cache invalidation rules", _invalidationRules.Count);
        }

        private string ProcessKeyPattern(string pattern, object eventData)
        {
            var result = pattern;
            var properties = eventData.GetType().GetProperties();

            foreach (var property in properties)
            {
                var placeholder = $"{{{property.Name}}}";
                var value = property.GetValue(eventData)?.ToString() ?? "";
                result = result.Replace(placeholder, value, StringComparison.OrdinalIgnoreCase);
            }

            return result;
        }

        private IEnumerable<string> ProcessTags(HashSet<string> tags, object eventData)
        {
            return tags.Select(tag => ProcessKeyPattern(tag, eventData));
        }
    }

    /// <summary>
    /// Interface for cache invalidation service
    /// </summary>
    public interface ICacheInvalidationService
    {
        /// <summary>
        /// Register invalidation rule for specific event type
        /// </summary>
        void RegisterInvalidationRule<TEvent>(string keyPattern, params string[] tags) where TEvent : IDomainEvent;

        /// <summary>
        /// Register tag-based invalidation rule
        /// </summary>
        void RegisterTagInvalidationRule<TEvent>(params string[] tags) where TEvent : IDomainEvent;

        /// <summary>
        /// Invalidate cache based on domain event
        /// </summary>
        Task InvalidateAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default) where TEvent : IDomainEvent;

        /// <summary>
        /// Manual cache invalidation
        /// </summary>
        Task InvalidateCacheAsync(CacheInvalidationRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get invalidation statistics
        /// </summary>
        CacheInvalidationStatistics GetStatistics();
    }

    /// <summary>
    /// Cache invalidation rule configuration
    /// </summary>
    public class CacheInvalidationRule
    {
        public string? KeyPattern { get; set; }
        public HashSet<string> Tags { get; set; } = new();
        public CacheInvalidationType InvalidationType { get; set; }
    }

    /// <summary>
    /// Type of cache invalidation
    /// </summary>
    public enum CacheInvalidationType
    {
        Key,
        Pattern,
        Tag
    }

    /// <summary>
    /// Manual cache invalidation request
    /// </summary>
    public class CacheInvalidationRequest
    {
        public List<string>? Keys { get; set; }
        public List<string>? Patterns { get; set; }
        public List<string>? Tags { get; set; }
    }

    /// <summary>
    /// Cache invalidation statistics
    /// </summary>
    public class CacheInvalidationStatistics
    {
        public List<string> RegisteredEventTypes { get; set; } = new();
        public int TotalRules { get; set; }
        public Dictionary<string, int> RulesByEventType { get; set; } = new();
        public DateTime LastUpdated { get; set; }
    }

    // Placeholder events - these should be moved to the Events project
    namespace PlaceholderEvents
    {
        public interface IDomainEvent
        {
            string Id { get; }
            DateTime OccurredAt { get; }
        }

        public record UserCreatedEvent(string UserId) : IDomainEvent
        {
            public string Id { get; init; } = Guid.NewGuid().ToString();
            public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
        }

        public record UserUpdatedEvent(string UserId) : IDomainEvent
        {
            public string Id { get; init; } = Guid.NewGuid().ToString();
            public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
        }

        public record UserDeletedEvent(string UserId) : IDomainEvent
        {
            public string Id { get; init; } = Guid.NewGuid().ToString();
            public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
        }

        public record SkillCreatedEvent(string SkillId) : IDomainEvent
        {
            public string Id { get; init; } = Guid.NewGuid().ToString();
            public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
        }

        public record SkillUpdatedEvent(string SkillId) : IDomainEvent
        {
            public string Id { get; init; } = Guid.NewGuid().ToString();
            public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
        }

        public record SkillDeletedEvent(string SkillId) : IDomainEvent
        {
            public string Id { get; init; } = Guid.NewGuid().ToString();
            public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
        }

        public record AppointmentCreatedEvent(string AppointmentId) : IDomainEvent
        {
            public string Id { get; init; } = Guid.NewGuid().ToString();
            public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
        }

        public record AppointmentUpdatedEvent(string AppointmentId) : IDomainEvent
        {
            public string Id { get; init; } = Guid.NewGuid().ToString();
            public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
        }

        public record MatchRequestCreatedEvent(string MatchId) : IDomainEvent
        {
            public string Id { get; init; } = Guid.NewGuid().ToString();
            public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
        }

        public record MatchRequestAcceptedEvent(string MatchId) : IDomainEvent
        {
            public string Id { get; init; } = Guid.NewGuid().ToString();
            public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
        }
    }
}