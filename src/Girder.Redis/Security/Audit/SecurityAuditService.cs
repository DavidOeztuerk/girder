using Girder.Abstractions.Security.Audit;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Girder.Redis.Security.Audit;

/// <summary>
/// Tamper-proof security audit service using Redis with cryptographic integrity
/// </summary>
public class SecurityAuditService : ISecurityAuditService
{
    private const string EventHashVersionPrefix = "v2:";
    private const int MaxChainAppendAttempts = 100;

    private readonly IDatabase _database;
    private readonly ILogger<SecurityAuditService> _logger;
    private readonly string _keyPrefix;
    private readonly byte[] _signingKey;
    private readonly SemaphoreSlim _chainLock = new(1, 1);

    // Lua script for atomic audit log insertion with chain integrity
    private const string LogEventScript = @"
        local eventKey = KEYS[1]
        local indexKey = KEYS[2]
        local chainKey = KEYS[3]
        local eventData = ARGV[1]
        local eventId = ARGV[2]
        local timestamp = ARGV[3]
        local severity = ARGV[4]
        local userId = ARGV[5] or ''
        local eventType = ARGV[6]
        local expectedPreviousHash = ARGV[7]
        local eventHash = ARGV[8]

        if redis.call('EXISTS', eventKey) == 1 then
            return -1
        end

        -- Compare-and-set the chain head. This prevents separate application
        -- processes from appending two children to the same predecessor.
        local currentHash = redis.call('GET', chainKey) or ''
        if currentHash ~= expectedPreviousHash then
            return 0
        end
        
        -- Store the event
        redis.call('SET', eventKey, eventData)
        
        -- Add to chronological index
        redis.call('ZADD', indexKey, timestamp, eventId)
        
        -- The chain links hashes, not Redis event IDs.
        redis.call('SET', chainKey, eventHash)
        
        -- Add to severity index
        local severityKey = 'audit:severity:' .. severity
        redis.call('ZADD', severityKey, timestamp, eventId)
        
        -- Add to user index if userId provided
        if userId ~= '' then
            local userKey = 'audit:user:' .. userId
            redis.call('ZADD', userKey, timestamp, eventId)
        end
        
        -- Add to event type index
        local typeKey = 'audit:type:' .. eventType
        redis.call('ZADD', typeKey, timestamp, eventId)
        
        return 1
    ";

    public SecurityAuditService(
        IConnectionMultiplexer connectionMultiplexer,
        ILogger<SecurityAuditService> logger,
        byte[] signingKey)
    {
        ArgumentNullException.ThrowIfNull(connectionMultiplexer);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(signingKey);

        if (signingKey.Length != 32)
        {
            throw new ArgumentException(
                $"The audit signing key is {signingKey.Length} bytes; 32 are required.",
                nameof(signingKey));
        }

        _database = connectionMultiplexer.GetDatabase();
        _logger = logger;
        _keyPrefix = "audit:";
        _signingKey = signingKey.ToArray();
    }

    public async Task<string> LogSecurityEventAsync(SecurityAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        var lockTaken = false;

        try
        {
            await _chainLock.WaitAsync(cancellationToken);
            lockTaken = true;

            // Calculate risk score if not set
            if (auditEvent.RiskScore == 0)
            {
                auditEvent.RiskScore = CalculateRiskScore(auditEvent);
            }

            auditEvent.Timestamp = auditEvent.Timestamp.ToUniversalTime();

            var chainKey = GetChainKey();
            var eventKey = GetEventKey(auditEvent.Id);
            var indexKey = GetIndexKey();
            var timestamp = new DateTimeOffset(auditEvent.Timestamp).ToUnixTimeSeconds();

            var appended = false;
            for (var attempt = 1; attempt <= MaxChainAppendAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var previousHash = await _database.StringGetAsync(chainKey);
                var expectedPreviousHash = previousHash.HasValue ? (string)previousHash! : string.Empty;
                auditEvent.PreviousEventHash = string.IsNullOrEmpty(expectedPreviousHash)
                    ? null
                    : expectedPreviousHash;
                auditEvent.EventHash = CalculateEventHash(auditEvent);
                auditEvent.Signature = CreateDigitalSignature(auditEvent);

                var eventData = JsonSerializer.Serialize(auditEvent, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                var scriptResult = await _database.ScriptEvaluateAsync(
                    LogEventScript,
                    new RedisKey[] { eventKey, indexKey, chainKey },
                    new RedisValue[]
                    {
                        eventData,
                        auditEvent.Id,
                        timestamp,
                        (int)auditEvent.Severity,
                        auditEvent.UserId ?? "",
                        auditEvent.EventType,
                        expectedPreviousHash,
                        auditEvent.EventHash
                    });

                // Redis returns 0 only when another process won the race. Some
                // mocked IDatabase implementations return an empty result for
                // a successful script, so only an explicit zero is a conflict.
                var scriptStatus = scriptResult.ToString();
                if (string.Equals(scriptStatus, "-1", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"An audit event with ID '{auditEvent.Id}' already exists.");
                }

                if (!string.Equals(scriptStatus, "0", StringComparison.Ordinal))
                {
                    appended = true;
                    break;
                }
            }

            if (!appended)
            {
                throw new InvalidOperationException(
                    $"Could not append audit event '{auditEvent.Id}' after {MaxChainAppendAttempts} concurrent updates.");
            }

            // Set expiration based on retention policy
            var expiration = TimeSpan.FromDays(auditEvent.RetentionDays);
            await _database.KeyExpireAsync(eventKey, expiration);

            _logger.LogDebug("Security audit event logged: {EventId} - {EventType}", 
                auditEvent.Id, auditEvent.EventType);

            return auditEvent.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log security audit event: {EventType}", auditEvent.EventType);
            throw;
        }
        finally
        {
            if (lockTaken)
            {
                _chainLock.Release();
            }
        }
    }

    public async Task<string> LogSecurityEventAsync(
        string eventType, 
        string description,
        SecurityEventSeverity severity = SecurityEventSeverity.Information,
        object? additionalData = null,
        CancellationToken cancellationToken = default)
    {
        var auditEvent = new SecurityAuditEvent
        {
            EventType = eventType,
            Description = description,
            Severity = severity,
            Category = MapEventTypeToCategory(eventType)
        };

        if (additionalData != null)
        {
            var properties = additionalData.GetType().GetProperties();
            foreach (var prop in properties)
            {
                var value = prop.GetValue(additionalData);
                auditEvent.Metadata[prop.Name] = value;
            }
        }

        return await LogSecurityEventAsync(auditEvent, cancellationToken);
    }

    public async Task<IEnumerable<SecurityAuditEvent>> GetSecurityEventsAsync(
        SecurityAuditQuery query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var events = new List<SecurityAuditEvent>();
            var indexKey = GetIndexKey();

            // Build Redis query based on filters
            var min = query.FromDate.HasValue ? 
                new DateTimeOffset(query.FromDate.Value).ToUnixTimeSeconds() : 0;
            var max = query.ToDate.HasValue ? 
                new DateTimeOffset(query.ToDate.Value).ToUnixTimeSeconds() : long.MaxValue;

            // Get event IDs from chronological index
            // var sortedSetOptions = new SortedSetRangeByScoreOptions
            // {
            //     Skip = (query.Page - 1) * query.PageSize,
            //     Take = query.PageSize
            // };

            var eventIds = await _database.SortedSetRangeByScoreAsync(
                indexKey, min, max, Exclude.None, 
                query.SortDescending ? Order.Descending : Order.Ascending);

            // Retrieve and filter events
            foreach (var eventId in eventIds)
            {
                try
                {
                    var eventKey = GetEventKey(eventId!);
                    var eventData = await _database.StringGetAsync(eventKey);
                    
                    if (eventData.HasValue)
                    {
                        var auditEvent = JsonSerializer.Deserialize<SecurityAuditEvent>(
                            (string)eventData!,
                            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

                        if (auditEvent != null && MatchesQuery(auditEvent, query))
                        {
                            events.Add(auditEvent);
                        }
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize audit event: {EventId}", eventId);
                }
            }

            return events;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve security audit events");
            return Enumerable.Empty<SecurityAuditEvent>();
        }
    }

    public async Task<AuditIntegrityResult> VerifyAuditIntegrityAsync(
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = new AuditIntegrityResult();
            var violations = new List<IntegrityViolation>();

            var eventList = await LoadEventsForIntegrityVerificationAsync(
                fromDate,
                toDate,
                violations,
                result,
                cancellationToken);

            foreach (var auditEvent in eventList)
            {
                result.EventsVerified++;

                // Verify event hash
                var calculatedHash = CalculateEventHash(auditEvent);
                if (auditEvent.EventHash != calculatedHash)
                {
                    violations.Add(new IntegrityViolation
                    {
                        EventId = auditEvent.Id,
                        ViolationType = "EventHash",
                        Description = "Event hash verification failed - possible tampering",
                        EventTimestamp = auditEvent.Timestamp
                    });
                    result.IntegrityViolations++;
                }

                // Verify digital signature
                if (!VerifyDigitalSignature(auditEvent))
                {
                    violations.Add(new IntegrityViolation
                    {
                        EventId = auditEvent.Id,
                        ViolationType = "Signature",
                        Description = "Digital signature verification failed",
                        EventTimestamp = auditEvent.Timestamp
                    });
                    result.IntegrityViolations++;
                }
            }

            VerifyChainTopology(eventList, violations, result);

            // Only an unbounded verification can compare the persisted chain
            // head with the last reachable event. A bounded slice can have
            // legitimate predecessors and successors outside the query.
            if (!fromDate.HasValue && !toDate.HasValue)
            {
                var storedHead = await _database.StringGetAsync(GetChainKey());
                var tail = FindChainTail(eventList);
                var expectedHead = tail?.EventHash ?? string.Empty;
                var actualHead = storedHead.HasValue ? (string)storedHead! : string.Empty;

                if (!string.Equals(actualHead, expectedHead, StringComparison.Ordinal))
                {
                    violations.Add(new IntegrityViolation
                    {
                        EventId = tail?.Id ?? string.Empty,
                        ViolationType = "ChainHead",
                        Description = "The persisted chain head does not match the last reachable audit event.",
                        EventTimestamp = tail?.Timestamp ?? DateTime.UtcNow
                    });
                    result.IntegrityViolations++;
                }
            }

            result.IsIntegrityIntact = result.IntegrityViolations == 0;
            result.Violations = violations;

            _logger.LogInformation(
                "Audit integrity verification completed: {EventsVerified} events, {Violations} violations",
                result.EventsVerified, result.IntegrityViolations);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify audit integrity");
            return new AuditIntegrityResult
            {
                IsIntegrityIntact = false,
                IntegrityViolations = 1,
                Violations =
                [
                    new IntegrityViolation
                    {
                        ViolationType = "VerificationFailure",
                        Description = "Audit integrity could not be verified because the backing store failed.",
                        EventTimestamp = DateTime.UtcNow
                    }
                ]
            };
        }
    }

    private async Task<List<SecurityAuditEvent>> LoadEventsForIntegrityVerificationAsync(
        DateTime? fromDate,
        DateTime? toDate,
        ICollection<IntegrityViolation> violations,
        AuditIntegrityResult result,
        CancellationToken cancellationToken)
    {
        var min = fromDate.HasValue
            ? new DateTimeOffset(fromDate.Value).ToUnixTimeSeconds()
            : 0;
        var max = toDate.HasValue
            ? new DateTimeOffset(toDate.Value).ToUnixTimeSeconds()
            : long.MaxValue;
        var eventIds = await _database.SortedSetRangeByScoreAsync(
            GetIndexKey(),
            min,
            max,
            Exclude.None,
            Order.Ascending);
        var events = new List<SecurityAuditEvent>(eventIds.Length);
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        foreach (var eventId in eventIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var eventIdText = (string)eventId!;
            var eventData = await _database.StringGetAsync(GetEventKey(eventIdText));
            if (!eventData.HasValue)
            {
                violations.Add(new IntegrityViolation
                {
                    EventId = eventIdText,
                    ViolationType = "MissingEvent",
                    Description = "The audit index references an event that is missing from storage.",
                    EventTimestamp = DateTime.UtcNow
                });
                result.IntegrityViolations++;
                continue;
            }

            try
            {
                var auditEvent = JsonSerializer.Deserialize<SecurityAuditEvent>((string)eventData!, jsonOptions);
                if (auditEvent is null)
                {
                    throw new JsonException("The stored audit event was null.");
                }

                events.Add(auditEvent);
            }
            catch (JsonException)
            {
                violations.Add(new IntegrityViolation
                {
                    EventId = eventIdText,
                    ViolationType = "InvalidEvent",
                    Description = "The stored audit event cannot be deserialized.",
                    EventTimestamp = DateTime.UtcNow
                });
                result.IntegrityViolations++;
            }
        }

        return events;
    }

    public async Task<SecurityAuditReport> GenerateAuditReportAsync(
        SecurityAuditQuery query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var events = await GetSecurityEventsAsync(query, cancellationToken);
            var eventList = events.ToList();

            var report = new SecurityAuditReport
            {
                PeriodStart = query.FromDate ?? DateTime.MinValue,
                PeriodEnd = query.ToDate ?? DateTime.MaxValue,
                TotalEvents = eventList.Count
            };

            // Group by severity
            report.EventsBySeverity = eventList
                .GroupBy(e => e.Severity)
                .ToDictionary(g => g.Key, g => g.Count());

            // Group by category
            report.EventsByCategory = eventList
                .GroupBy(e => e.Category)
                .ToDictionary(g => g.Key, g => g.Count());

            // Top users
            report.TopUsersByEventCount = eventList
                .Where(e => !string.IsNullOrEmpty(e.UserId))
                .GroupBy(e => e.UserId!)
                .OrderByDescending(g => g.Count())
                .Take(10)
                .ToDictionary(g => g.Key, g => g.Count());

            // Top IP addresses
            report.TopIpAddressesByEventCount = eventList
                .Where(e => !string.IsNullOrEmpty(e.IpAddress))
                .GroupBy(e => e.IpAddress!)
                .OrderByDescending(g => g.Count())
                .Take(10)
                .ToDictionary(g => g.Key, g => g.Count());

            // Security incidents (high/critical severity)
            report.SecurityIncidents = eventList
                .Where(e => e.Severity >= SecurityEventSeverity.High)
                .OrderByDescending(e => e.Timestamp)
                .ToList();

            // Compliance summary
            report.ComplianceSummary = eventList
                .SelectMany(e => e.ComplianceFlags)
                .GroupBy(flag => flag)
                .ToDictionary(g => g.Key, g => g.Count());

            return report;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate audit report");
            throw;
        }
    }

    public async Task<byte[]> ExportAuditLogsAsync(
        SecurityAuditQuery query,
        AuditExportFormat format = AuditExportFormat.Json,
        CancellationToken cancellationToken = default)
    {
        var events = await GetSecurityEventsAsync(query, cancellationToken);
        
        return format switch
        {
            AuditExportFormat.Json => ExportAsJson(events),
            AuditExportFormat.Csv => ExportAsCsv(events),
            AuditExportFormat.Xml => ExportAsXml(events),
            _ => throw new ArgumentException($"Unsupported export format: {format}")
        };
    }

    public async Task<int> ArchiveOldLogsAsync(
        DateTime archiveBeforeDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var archivedCount = 0;
            var indexKey = GetIndexKey();
            var maxScore = new DateTimeOffset(archiveBeforeDate).ToUnixTimeSeconds();

            // Get events to archive
            var eventIds = await _database.SortedSetRangeByScoreAsync(
                indexKey, 0, maxScore, Exclude.None, Order.Ascending);

            foreach (var eventId in eventIds)
            {
                var eventKey = GetEventKey(eventId!);
                
                // Move to archive (in a real implementation, this might involve moving to cold storage)
                var archiveKey = GetArchiveKey(eventId!);
                var eventData = await _database.StringGetAsync(eventKey);
                
                if (eventData.HasValue)
                {
                    await _database.StringSetAsync(archiveKey, eventData!);
                    await _database.KeyDeleteAsync(eventKey);
                    await _database.SortedSetRemoveAsync(indexKey, eventId!);
                    archivedCount++;
                }
            }

            _logger.LogInformation("Archived {Count} audit events older than {Date}", 
                archivedCount, archiveBeforeDate);

            return archivedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to archive old audit logs");
            throw;
        }
    }

    public async Task<SecurityAuditStatistics> GetAuditStatisticsAsync(
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var indexKey = GetIndexKey();
            var min = fromDate.HasValue ? new DateTimeOffset(fromDate.Value).ToUnixTimeSeconds() : 0;
            var max = toDate.HasValue ? new DateTimeOffset(toDate.Value).ToUnixTimeSeconds() : long.MaxValue;

            var totalEvents = await _database.SortedSetLengthAsync(indexKey, min, max);
            
            var statistics = new SecurityAuditStatistics
            {
                TotalEvents = totalEvents
            };

            if (totalEvents > 0)
            {
                // Get oldest and newest events
                var oldestEvent = await _database.SortedSetRangeByScoreAsync(indexKey, min, max, Exclude.None, Order.Ascending, 0, 1);
                var newestEvent = await _database.SortedSetRangeByScoreAsync(indexKey, min, max, Exclude.None, Order.Descending, 0, 1);

                if (oldestEvent.Any())
                {
                    var oldestScore = await _database.SortedSetScoreAsync(indexKey, oldestEvent.First());
                    statistics.OldestEventTimestamp = DateTimeOffset.FromUnixTimeSeconds((long)oldestScore!).DateTime;
                }

                if (newestEvent.Any())
                {
                    var newestScore = await _database.SortedSetScoreAsync(indexKey, newestEvent.First());
                    statistics.NewestEventTimestamp = DateTimeOffset.FromUnixTimeSeconds((long)newestScore!).DateTime;
                }

                // Calculate events per day
                if (statistics.OldestEventTimestamp.HasValue && statistics.NewestEventTimestamp.HasValue)
                {
                    var daySpan = (statistics.NewestEventTimestamp.Value - statistics.OldestEventTimestamp.Value).Days;
                    statistics.AverageEventsPerDay = daySpan > 0 ? (double)totalEvents / daySpan : totalEvents;
                }
            }

            return statistics;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get audit statistics");
            throw;
        }
    }

    private static void VerifyChainTopology(
        IReadOnlyCollection<SecurityAuditEvent> events,
        ICollection<IntegrityViolation> violations,
        AuditIntegrityResult result)
    {
        if (events.Count == 0)
        {
            return;
        }

        var eventsByHash = new Dictionary<string, SecurityAuditEvent>(StringComparer.Ordinal);
        var duplicateIds = events
            .GroupBy(auditEvent => auditEvent.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1);

        foreach (var duplicate in duplicateIds)
        {
            AddChainViolation(
                duplicate.First(),
                "DuplicateEventId",
                $"Audit event ID '{duplicate.Key}' occurs more than once.",
                violations,
                result);
        }

        foreach (var auditEvent in events)
        {
            if (string.IsNullOrEmpty(auditEvent.EventHash)
                || !eventsByHash.TryAdd(auditEvent.EventHash, auditEvent))
            {
                AddChainViolation(
                    auditEvent,
                    "DuplicateEventHash",
                    "The event hash is missing or occurs more than once.",
                    violations,
                    result);
            }
        }

        var childrenByPreviousHash = events
            .Where(auditEvent => !string.IsNullOrEmpty(auditEvent.PreviousEventHash))
            .GroupBy(auditEvent => auditEvent.PreviousEventHash!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var roots = events
            .Where(auditEvent => string.IsNullOrEmpty(auditEvent.PreviousEventHash)
                || !eventsByHash.ContainsKey(auditEvent.PreviousEventHash))
            .ToList();

        if (roots.Count != 1)
        {
            AddChainViolation(
                roots.FirstOrDefault() ?? events.First(),
                "ChainTopology",
                $"Expected one chain start in the verified set but found {roots.Count}.",
                violations,
                result);
            return;
        }

        var visitedEvents = new HashSet<SecurityAuditEvent>();
        var current = roots[0];

        while (current is not null)
        {
            if (!visitedEvents.Add(current))
            {
                AddChainViolation(
                    current,
                    "ChainCycle",
                    "The audit chain contains a cycle.",
                    violations,
                    result);
                break;
            }

            if (string.IsNullOrEmpty(current.EventHash)
                || !childrenByPreviousHash.TryGetValue(current.EventHash, out var children))
            {
                break;
            }

            if (children.Count != 1)
            {
                AddChainViolation(
                    current,
                    "ChainFork",
                    $"Event '{current.Id}' has {children.Count} successor events.",
                    violations,
                    result);
                break;
            }

            current = children[0];
        }

        if (visitedEvents.Count != events.Count)
        {
            var unvisited = events.First(auditEvent => !visitedEvents.Contains(auditEvent));
            AddChainViolation(
                unvisited,
                "DisconnectedChain",
                "The verified events do not form one connected audit chain.",
                violations,
                result);
        }
    }

    private static SecurityAuditEvent? FindChainTail(IReadOnlyCollection<SecurityAuditEvent> events)
    {
        if (events.Count == 0)
        {
            return null;
        }

        var referencedHashes = events
            .Select(auditEvent => auditEvent.PreviousEventHash)
            .Where(hash => !string.IsNullOrEmpty(hash))
            .ToHashSet(StringComparer.Ordinal);

        return events.FirstOrDefault(auditEvent =>
            !string.IsNullOrEmpty(auditEvent.EventHash)
            && !referencedHashes.Contains(auditEvent.EventHash));
    }

    private static void AddChainViolation(
        SecurityAuditEvent auditEvent,
        string violationType,
        string description,
        ICollection<IntegrityViolation> violations,
        AuditIntegrityResult result)
    {
        violations.Add(new IntegrityViolation
        {
            EventId = auditEvent.Id,
            ViolationType = violationType,
            Description = description,
            EventTimestamp = auditEvent.Timestamp
        });
        result.IntegrityViolations++;
    }

    private static string CalculateEventHash(SecurityAuditEvent auditEvent)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(GetCanonicalEventBytes(auditEvent));
        return EventHashVersionPrefix + Convert.ToBase64String(hashBytes);
    }

    private static byte[] GetCanonicalEventBytes(SecurityAuditEvent auditEvent)
    {
        using var buffer = new MemoryStream();
        using var writer = new Utf8JsonWriter(buffer);

        writer.WriteStartObject();
        writer.WriteNumber("formatVersion", 2);
        writer.WriteString("id", auditEvent.Id);
        writer.WriteString("eventType", auditEvent.EventType);
        writer.WriteString("description", auditEvent.Description);
        WriteNullableString(writer, "userId", auditEvent.UserId);
        WriteNullableString(writer, "sessionId", auditEvent.SessionId);
        WriteNullableString(writer, "ipAddress", auditEvent.IpAddress);
        WriteNullableString(writer, "userAgent", auditEvent.UserAgent);
        WriteNullableString(writer, "requestId", auditEvent.RequestId);
        writer.WriteString("source", auditEvent.Source);
        writer.WriteString("timestamp", auditEvent.Timestamp.ToUniversalTime());
        writer.WriteNumber("severity", (int)auditEvent.Severity);
        writer.WriteNumber("category", (int)auditEvent.Category);
        WriteNullableString(writer, "resourceType", auditEvent.ResourceType);
        WriteNullableString(writer, "resourceId", auditEvent.ResourceId);
        WriteNullableString(writer, "action", auditEvent.Action);
        WriteNullableString(writer, "result", auditEvent.Result);

        writer.WritePropertyName("metadata");
        writer.WriteStartObject();
        foreach (var pair in auditEvent.Metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            writer.WritePropertyName(pair.Key);
            WriteCanonicalJsonValue(writer, pair.Value);
        }
        writer.WriteEndObject();

        writer.WriteNumber("riskScore", auditEvent.RiskScore);
        writer.WritePropertyName("tags");
        writer.WriteStartArray();
        foreach (var tag in auditEvent.Tags)
        {
            writer.WriteStringValue(tag);
        }
        writer.WriteEndArray();
        WriteNullableString(writer, "previousEventHash", auditEvent.PreviousEventHash);
        writer.WritePropertyName("complianceFlags");
        writer.WriteStartArray();
        foreach (var flag in auditEvent.ComplianceFlags)
        {
            writer.WriteStringValue(flag);
        }
        writer.WriteEndArray();
        writer.WriteNumber("retentionDays", auditEvent.RetentionDays);
        writer.WriteEndObject();
        writer.Flush();

        return buffer.ToArray();
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string propertyName, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(propertyName);
        }
        else
        {
            writer.WriteString(propertyName, value);
        }
    }

    private static void WriteCanonicalJsonValue(Utf8JsonWriter writer, object? value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        var element = value is JsonElement jsonElement
            ? jsonElement
            : JsonSerializer.SerializeToElement(value, value.GetType());
        WriteCanonicalJsonElement(writer, element);
    }

    private static void WriteCanonicalJsonElement(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(
                    property => property.Name,
                    StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJsonElement(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonicalJsonElement(writer, item);
                }
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: true);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            default:
                writer.WriteNullValue();
                break;
        }
    }

    private string CreateDigitalSignature(SecurityAuditEvent auditEvent)
    {
        var signatureInput = $"{auditEvent.EventHash}|{auditEvent.Timestamp:O}";
        
        using var hmac = new HMACSHA256(_signingKey);
        var signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(signatureInput));
        return Convert.ToBase64String(signatureBytes);
    }

    private bool VerifyDigitalSignature(SecurityAuditEvent auditEvent)
    {
        try
        {
            var expected = Convert.FromBase64String(CreateDigitalSignature(auditEvent));
            var actual = Convert.FromBase64String(auditEvent.Signature ?? string.Empty);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }

    private static int CalculateRiskScore(SecurityAuditEvent auditEvent)
    {
        var baseScore = auditEvent.Severity switch
        {
            SecurityEventSeverity.Information => 10,
            SecurityEventSeverity.Low => 25,
            SecurityEventSeverity.Medium => 50,
            SecurityEventSeverity.High => 75,
            SecurityEventSeverity.Critical => 95,
            _ => 10
        };

        // Adjust based on category
        var categoryMultiplier = auditEvent.Category switch
        {
            SecurityEventCategory.SecurityIncident => 1.5,
            SecurityEventCategory.Authorization => 1.2,
            SecurityEventCategory.Authentication => 1.2,
            SecurityEventCategory.DataModification => 1.3,
            SecurityEventCategory.ConfigurationChange => 1.1,
            _ => 1.0
        };

        return Math.Min(100, (int)(baseScore * categoryMultiplier));
    }

    private static SecurityEventCategory MapEventTypeToCategory(string eventType)
    {
        return eventType.ToLowerInvariant() switch
        {
            var e when e.Contains("login") || e.Contains("auth") => SecurityEventCategory.Authentication,
            var e when e.Contains("permission") || e.Contains("access") => SecurityEventCategory.Authorization,
            var e when e.Contains("data") || e.Contains("modify") => SecurityEventCategory.DataModification,
            var e when e.Contains("config") => SecurityEventCategory.ConfigurationChange,
            var e when e.Contains("incident") || e.Contains("security") => SecurityEventCategory.SecurityIncident,
            _ => SecurityEventCategory.General
        };
    }

    private static bool MatchesQuery(SecurityAuditEvent auditEvent, SecurityAuditQuery query)
    {
        if (!string.IsNullOrEmpty(query.UserId) && auditEvent.UserId != query.UserId)
            return false;

        if (!string.IsNullOrEmpty(query.EventType) && !auditEvent.EventType.Contains(query.EventType, StringComparison.OrdinalIgnoreCase))
            return false;

        if (query.Severity.HasValue && auditEvent.Severity != query.Severity)
            return false;

        if (query.Category.HasValue && auditEvent.Category != query.Category)
            return false;

        if (!string.IsNullOrEmpty(query.Source) && auditEvent.Source != query.Source)
            return false;

        if (!string.IsNullOrEmpty(query.IpAddress) && auditEvent.IpAddress != query.IpAddress)
            return false;

        if (!string.IsNullOrEmpty(query.ResourceType) && auditEvent.ResourceType != query.ResourceType)
            return false;

        if (!string.IsNullOrEmpty(query.ResourceId) && auditEvent.ResourceId != query.ResourceId)
            return false;

        if (query.Tags.Any() && !query.Tags.Any(tag => auditEvent.Tags.Contains(tag)))
            return false;

        if (!string.IsNullOrEmpty(query.SearchText))
        {
            var searchText = query.SearchText.ToLowerInvariant();
            if (!auditEvent.Description.Contains(searchText, StringComparison.OrdinalIgnoreCase) &&
                !auditEvent.EventType.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private byte[] ExportAsJson(IEnumerable<SecurityAuditEvent> events)
    {
        var json = JsonSerializer.Serialize(events, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
        return Encoding.UTF8.GetBytes(json);
    }

    private static byte[] ExportAsCsv(IEnumerable<SecurityAuditEvent> events)
    {
        var csv = new StringBuilder();
        csv.AppendLine("Id,EventType,Description,UserId,Timestamp,Severity,Category,IpAddress,Source");
        
        foreach (var e in events)
        {
            csv.AppendLine($"{e.Id},{e.EventType},{e.Description},{e.UserId},{e.Timestamp:O},{e.Severity},{e.Category},{e.IpAddress},{e.Source}");
        }
        
        return Encoding.UTF8.GetBytes(csv.ToString());
    }

    private static byte[] ExportAsXml(IEnumerable<SecurityAuditEvent> events)
    {
        // Simplified XML export implementation
        var xml = new StringBuilder();
        xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        xml.AppendLine("<SecurityAuditEvents>");
        
        foreach (var e in events)
        {
            xml.AppendLine($"  <Event id=\"{e.Id}\" type=\"{e.EventType}\" timestamp=\"{e.Timestamp:O}\">");
            xml.AppendLine($"    <Description>{e.Description}</Description>");
            xml.AppendLine($"    <Severity>{e.Severity}</Severity>");
            xml.AppendLine($"    <Category>{e.Category}</Category>");
            if (!string.IsNullOrEmpty(e.UserId))
                xml.AppendLine($"    <UserId>{e.UserId}</UserId>");
            if (!string.IsNullOrEmpty(e.IpAddress))
                xml.AppendLine($"    <IpAddress>{e.IpAddress}</IpAddress>");
            xml.AppendLine("  </Event>");
        }
        
        xml.AppendLine("</SecurityAuditEvents>");
        return Encoding.UTF8.GetBytes(xml.ToString());
    }

    private string GetEventKey(string eventId) => $"{_keyPrefix}event:{eventId}";
    private string GetIndexKey() => $"{_keyPrefix}index";
    private string GetChainKey() => $"{_keyPrefix}chain";
    private string GetArchiveKey(string eventId) => $"{_keyPrefix}archive:{eventId}";
}
