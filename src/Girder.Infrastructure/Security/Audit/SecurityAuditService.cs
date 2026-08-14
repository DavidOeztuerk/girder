using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Infrastructure.Security.Audit;

/// <summary>
/// Tamper-proof security audit service using Redis with cryptographic integrity
/// </summary>
public class SecurityAuditService : ISecurityAuditService
{
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
        
        -- Store the event
        redis.call('SET', eventKey, eventData)
        
        -- Add to chronological index
        redis.call('ZADD', indexKey, timestamp, eventId)
        
        -- Update chain hash
        local prevHash = redis.call('GET', chainKey) or ''
        redis.call('SET', chainKey, eventId)
        
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
        
        return prevHash
    ";

    public SecurityAuditService(
        IConnectionMultiplexer connectionMultiplexer,
        ILogger<SecurityAuditService> logger,
        byte[]? signingKey = null)
    {
        _database = connectionMultiplexer.GetDatabase();
        _logger = logger;
        _keyPrefix = "audit:";
        _signingKey = signingKey ?? GenerateSigningKey();
    }

    public async Task<string> LogSecurityEventAsync(SecurityAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        try
        {
            await _chainLock.WaitAsync(cancellationToken);

            // Calculate risk score if not set
            if (auditEvent.RiskScore == 0)
            {
                auditEvent.RiskScore = CalculateRiskScore(auditEvent);
            }

            // Get previous event hash for chain integrity
            var chainKey = GetChainKey();
            var previousHash = await _database.StringGetAsync(chainKey);
            auditEvent.PreviousEventHash = previousHash.HasValue ? (string?)previousHash! : null;

            // Calculate event hash
            auditEvent.EventHash = CalculateEventHash(auditEvent);

            // Create digital signature
            auditEvent.Signature = CreateDigitalSignature(auditEvent);

            // Serialize event
            var eventData = JsonSerializer.Serialize(auditEvent, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            // Store event atomically with indexes
            var eventKey = GetEventKey(auditEvent.Id);
            var indexKey = GetIndexKey();
            var timestamp = new DateTimeOffset(auditEvent.Timestamp).ToUnixTimeSeconds();

            await _database.ScriptEvaluateAsync(
                LogEventScript,
                new RedisKey[] { eventKey, indexKey, chainKey },
                new RedisValue[] 
                { 
                    eventData, 
                    auditEvent.Id, 
                    timestamp,
                    (int)auditEvent.Severity,
                    auditEvent.UserId ?? "",
                    auditEvent.EventType
                }
            );

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
            _chainLock.Release();
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
                            eventData!, 
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

            var query = new SecurityAuditQuery
            {
                FromDate = fromDate,
                ToDate = toDate,
                PageSize = 1000,
                SortDescending = false // Verify chronologically
            };

            var events = await GetSecurityEventsAsync(query, cancellationToken);
            var eventList = events.ToList();

            string? previousHash = null;

            foreach (var auditEvent in eventList)
            {
                result.EventsVerified++;

                // Verify hash chain integrity
                if (auditEvent.PreviousEventHash != previousHash)
                {
                    violations.Add(new IntegrityViolation
                    {
                        EventId = auditEvent.Id,
                        ViolationType = "ChainIntegrity",
                        Description = $"Chain hash mismatch. Expected: {previousHash}, Found: {auditEvent.PreviousEventHash}",
                        EventTimestamp = auditEvent.Timestamp
                    });
                    result.IntegrityViolations++;
                }

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

                previousHash = auditEvent.EventHash;
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
            return new AuditIntegrityResult { IsIntegrityIntact = false };
        }
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

    private string CalculateEventHash(SecurityAuditEvent auditEvent)
    {
        var hashInput = $"{auditEvent.Id}|{auditEvent.EventType}|{auditEvent.Description}|" +
                       $"{auditEvent.UserId}|{auditEvent.Timestamp:O}|{auditEvent.PreviousEventHash}";
        
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(hashInput));
        return Convert.ToBase64String(hashBytes);
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
            var expectedSignature = CreateDigitalSignature(auditEvent);
            return auditEvent.Signature == expectedSignature;
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

    private static byte[] GenerateSigningKey()
    {
        using var rng = RandomNumberGenerator.Create();
        var key = new byte[32]; // 256-bit key
        rng.GetBytes(key);
        return key;
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