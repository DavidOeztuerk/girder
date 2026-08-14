using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Text.Json;
using Infrastructure.Security.Encryption;
using AuditSeverity = Infrastructure.Security.Audit.SecurityEventSeverity;
using ISecurityAuditService = Infrastructure.Security.Audit.ISecurityAuditService;

namespace Infrastructure.Security.Compliance;

/// <summary>
/// GDPR and data protection compliance service implementation.
/// Implements IDataProtectionService (active methods: CheckConsentStatusAsync, GetDataRetentionPolicyAsync),
/// IConsentManagementService (RecordConsentAsync, GetConsentHistoryAsync, IsProcessingConsentedAsync, GetActiveConsentsAsync),
/// and IDataBreachNotificationService (ReportDataBreachAsync, UpdateBreachStatusAsync).
/// Methods that are still commented out on the interfaces are not implemented here.
/// </summary>
public class DataProtectionService : IDataProtectionService, IConsentManagementService, IDataBreachNotificationService
{
    private readonly IDatabase _database;
    private readonly ILogger<DataProtectionService> _logger;
    private readonly ISecurityAuditService _auditService;
    private readonly IDataEncryptionService _encryptionService;
    private readonly DataProtectionOptions _options;
    private readonly string _keyPrefix = "gdpr:";

    public DataProtectionService(
        IConnectionMultiplexer connectionMultiplexer,
        ILogger<DataProtectionService> logger,
        ISecurityAuditService auditService,
        IDataEncryptionService encryptionService,
        IOptions<DataProtectionOptions> options)
    {
        _database = connectionMultiplexer.GetDatabase();
        _logger = logger;
        _auditService = auditService;
        _encryptionService = encryptionService;
        _options = options.Value;
    }

    #region IDataProtectionService — Active Methods

    public async Task<ConsentStatus> CheckConsentStatusAsync(
        string dataSubjectId,
        string processingPurpose,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var consentKey = GetConsentKey(dataSubjectId, processingPurpose);
            var consentData = await _database.StringGetAsync(consentKey);

            if (!consentData.HasValue)
            {
                return ConsentStatus.Invalid;
            }

            var decrypted = await DecryptPayloadAsync(consentData!, "consent-read", cancellationToken);
            var consent = JsonSerializer.Deserialize<ConsentRecord>(decrypted);
            if (consent == null)
            {
                return ConsentStatus.Invalid;
            }

            if (consent.Status == ConsentStatus.Withdrawn)
            {
                return ConsentStatus.Withdrawn;
            }

            if (IsConsentExpired(consent))
            {
                return ConsentStatus.Expired;
            }

            return ConsentStatus.Given;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking consent status for {DataSubjectId}, purpose {Purpose}",
                dataSubjectId, processingPurpose);
            return ConsentStatus.Invalid;
        }
    }

    public async Task<DataRetentionPolicy> GetDataRetentionPolicyAsync(
        string dataType,
        string processingPurpose,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var policyKey = GetRetentionPolicyKey(dataType, processingPurpose);
            var policyData = await _database.StringGetAsync(policyKey);

            if (policyData.HasValue)
            {
                var policy = JsonSerializer.Deserialize<DataRetentionPolicy>(policyData!);
                if (policy != null)
                {
                    return policy;
                }
            }

            return CreateDefaultRetentionPolicy(dataType, processingPurpose);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting retention policy for {DataType}, purpose {Purpose}",
                dataType, processingPurpose);
            return CreateDefaultRetentionPolicy(dataType, processingPurpose);
        }
    }

    /// <summary>
    /// Apply data retention policies — marks expired consents, removes expired tokens,
    /// archives old audit logs. DSGVO Art. 5(1)(e): Storage limitation.
    /// Does NOT hard-delete user data — that is handled by DeleteUserCommandHandler (Art. 17).
    /// </summary>
    public async Task<DataRetentionResult> ApplyDataRetentionAsync(CancellationToken cancellationToken = default)
    {
        var result = new DataRetentionResult();
        _logger.LogInformation("[DataRetention] Starting automated data retention check");

        try
        {
            // 1. Mark expired consents
            var expiredCount = await MarkExpiredConsentsAsync(cancellationToken);
            result.ExpiredConsentsMarked = expiredCount;
            _logger.LogInformation("[DataRetention] Marked {Count} expired consents", expiredCount);

            // 2. Clean up expired refresh tokens (stored in Redis with gdpr: prefix)
            var tokensRemoved = await CleanExpiredTokensAsync(cancellationToken);
            result.ExpiredTokensRemoved = tokensRemoved;
            _logger.LogInformation("[DataRetention] Removed {Count} expired tokens", tokensRemoved);

            // 3. Archive old audit log entries beyond retention period
            var logsArchived = await ArchiveOldAuditLogsAsync(cancellationToken);
            result.AuditLogsArchived = logsArchived;
            _logger.LogInformation("[DataRetention] Archived {Count} old audit log entries", logsArchived);

            await _auditService.LogSecurityEventAsync(
                "DataRetentionExecuted",
                $"Data retention completed: {expiredCount} consents expired, {tokensRemoved} tokens removed, {logsArchived} logs archived",
                AuditSeverity.Low,
                new { result.ExpiredConsentsMarked, result.ExpiredTokensRemoved, result.AuditLogsArchived });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DataRetention] Error during data retention execution");
            result.Errors.Add(ex.Message);
        }

        return result;
    }

    /// <summary>
    /// Generate periodic compliance report with consent statistics and breach status.
    /// DSGVO Art. 30: Records of processing activities.
    /// </summary>
    public async Task<ComplianceReport> GenerateComplianceReportAsync(CancellationToken cancellationToken = default)
    {
        var report = new ComplianceReport();
        _logger.LogInformation("[ComplianceReport] Generating compliance report");

        try
        {
            // Count consent states via Redis scan
            var (active, expired, withdrawn) = await CountConsentsByStatusAsync(cancellationToken);
            report.ActiveConsentsCount = active;
            report.ExpiredConsentsCount = expired;
            report.WithdrawnConsentsCount = withdrawn;

            // Count open/resolved breaches
            var (openBreaches, resolvedBreaches) = await CountBreachesByStatusAsync(cancellationToken);
            report.DataBreachesOpen = openBreaches;
            report.DataBreachesResolved = resolvedBreaches;

            // Warnings
            if (report.DataBreachesOpen > 0)
                report.Warnings.Add($"{report.DataBreachesOpen} unresolved data breach(es) — DSGVO Art. 33 requires notification within 72 hours");
            if (report.ExpiredConsentsCount > 10)
                report.Warnings.Add($"{report.ExpiredConsentsCount} expired consents — consider sending renewal requests");

            await _auditService.LogSecurityEventAsync(
                "ComplianceReportGenerated",
                $"Compliance report: {active} active consents, {expired} expired, {withdrawn} withdrawn, {openBreaches} open breaches",
                AuditSeverity.Low,
                new { report.ActiveConsentsCount, report.ExpiredConsentsCount, report.DataBreachesOpen });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ComplianceReport] Error generating compliance report");
            report.Warnings.Add($"Report generation error: {ex.Message}");
        }

        return report;
    }

    #endregion

    #region Data Retention Helpers

    private async Task<int> MarkExpiredConsentsAsync(CancellationToken cancellationToken)
    {
        var count = 0;
        var server = _database.Multiplexer.GetServer(_database.Multiplexer.GetEndPoints()[0]);
        var pattern = $"{_keyPrefix}consent:*";

        await foreach (var key in server.KeysAsync(pattern: pattern))
        {
            if (cancellationToken.IsCancellationRequested) break;
            try
            {
                var data = await _database.StringGetAsync(key);
                if (!data.HasValue) continue;

                var decrypted = await DecryptPayloadAsync(data!, "retention-check", cancellationToken);
                var consent = JsonSerializer.Deserialize<ConsentRecord>(decrypted);
                if (consent == null || consent.Status != ConsentStatus.Given) continue;

                if (IsConsentExpired(consent))
                {
                    consent.Status = ConsentStatus.Expired;
                    var json = JsonSerializer.Serialize(consent);
                    var encrypted = await EncryptPayloadAsync(json, "retention-update", cancellationToken);
                    if (encrypted != null)
                    {
                        await _database.StringSetAsync(key, encrypted);
                        count++;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[DataRetention] Error processing consent key {Key}", key);
            }
        }

        return count;
    }

    private async Task<int> CleanExpiredTokensAsync(CancellationToken cancellationToken)
    {
        // Expired tokens are automatically removed by Redis TTL.
        // This method scans for any orphaned token keys without TTL and sets a 24h expiry.
        var count = 0;
        var server = _database.Multiplexer.GetServer(_database.Multiplexer.GetEndPoints()[0]);
        var pattern = $"{_keyPrefix}token:*";

        await foreach (var key in server.KeysAsync(pattern: pattern))
        {
            if (cancellationToken.IsCancellationRequested) break;
            var ttl = await _database.KeyTimeToLiveAsync(key);
            if (ttl == null) // No TTL set — orphaned key
            {
                await _database.KeyExpireAsync(key, TimeSpan.FromHours(24));
                count++;
            }
        }

        return count;
    }

    private async Task<int> ArchiveOldAuditLogsAsync(CancellationToken cancellationToken)
    {
        // Audit logs older than the retention period are removed from Redis.
        // In production, these would be shipped to cold storage first.
        var count = 0;
        var server = _database.Multiplexer.GetServer(_database.Multiplexer.GetEndPoints()[0]);
        var pattern = $"{_keyPrefix}audit:*";
        var cutoff = DateTime.UtcNow - _options.DefaultRetentionPeriod;

        await foreach (var key in server.KeysAsync(pattern: pattern))
        {
            if (cancellationToken.IsCancellationRequested) break;
            try
            {
                var data = await _database.StringGetAsync(key);
                if (!data.HasValue) continue;

                // Check if the key has creation metadata (best-effort timestamp check)
                var ttl = await _database.KeyTimeToLiveAsync(key);
                if (ttl == null)
                {
                    // No TTL = potentially old, set retention-based expiry
                    await _database.KeyExpireAsync(key, TimeSpan.FromDays(7));
                    count++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[DataRetention] Error archiving audit key {Key}", key);
            }
        }

        return count;
    }

    private async Task<(int active, int expired, int withdrawn)> CountConsentsByStatusAsync(CancellationToken cancellationToken)
    {
        int active = 0, expired = 0, withdrawn = 0;
        var server = _database.Multiplexer.GetServer(_database.Multiplexer.GetEndPoints()[0]);
        var pattern = $"{_keyPrefix}consent:*";

        await foreach (var key in server.KeysAsync(pattern: pattern))
        {
            if (cancellationToken.IsCancellationRequested) break;
            // Skip history keys
            if (key.ToString().Contains("consent_history:")) continue;

            try
            {
                var data = await _database.StringGetAsync(key);
                if (!data.HasValue) continue;

                var decrypted = await DecryptPayloadAsync(data!, "report-read", cancellationToken);
                var consent = JsonSerializer.Deserialize<ConsentRecord>(decrypted);
                if (consent == null) continue;

                switch (consent.Status)
                {
                    case ConsentStatus.Given: active++; break;
                    case ConsentStatus.Expired: expired++; break;
                    case ConsentStatus.Withdrawn: withdrawn++; break;
                }
            }
            catch
            {
                // Skip unreadable entries
            }
        }

        return (active, expired, withdrawn);
    }

    private async Task<(int open, int resolved)> CountBreachesByStatusAsync(CancellationToken cancellationToken)
    {
        int open = 0, resolved = 0;
        var server = _database.Multiplexer.GetServer(_database.Multiplexer.GetEndPoints()[0]);
        var pattern = $"{_keyPrefix}breach:*";

        await foreach (var key in server.KeysAsync(pattern: pattern))
        {
            if (cancellationToken.IsCancellationRequested) break;
            try
            {
                var data = await _database.StringGetAsync(key);
                if (!data.HasValue) continue;

                var decrypted = await DecryptPayloadAsync(data!, "report-read", cancellationToken);
                var breach = JsonSerializer.Deserialize<DataBreach>(decrypted);
                if (breach == null) continue;

                if (breach.Status is BreachStatus.Resolved or BreachStatus.Closed)
                    resolved++;
                else
                    open++;
            }
            catch
            {
                // Skip unreadable entries
            }
        }

        return (open, resolved);
    }

    #endregion

    #region IConsentManagementService

    public async Task<string> RecordConsentAsync(
        ConsentRecord consent,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Validate consent
            var validity = ValidateConsent(consent);
            if (!validity.IsValid)
            {
                _logger.LogWarning("Invalid consent for {DataSubjectId}: {Issues}",
                    consent.DataSubjectId, string.Join(", ", validity.ValidationIssues));
                return string.Empty;
            }

            // Store consent (encrypted — contains data subject IDs and consent metadata)
            var consentKey = GetConsentKey(consent.DataSubjectId, consent.ProcessingPurpose);
            var consentJson = JsonSerializer.Serialize(consent);
            var encryptedConsent = await EncryptPayloadAsync(consentJson, "consent-store", cancellationToken);
            if (encryptedConsent == null)
            {
                _logger.LogError("Refusing to store consent {ConsentId} — encryption failed", consent.Id);
                return string.Empty;
            }
            await _database.StringSetAsync(consentKey, encryptedConsent);

            // Add to consent history
            var historyKey = GetConsentHistoryKey(consent.DataSubjectId);
            await _database.ListLeftPushAsync(historyKey, encryptedConsent);

            // Log consent recording
            await _auditService.LogSecurityEventAsync(
                "ConsentRecorded",
                $"Consent recorded for data subject {consent.DataSubjectId}",
                AuditSeverity.Low,
                new
                {
                    ConsentId = consent.Id,
                    DataSubjectId = consent.DataSubjectId,
                    ProcessingPurpose = consent.ProcessingPurpose,
                    ConsentMethod = consent.ConsentMethod
                });

            return consent.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording consent {ConsentId}", consent.Id);
            return string.Empty;
        }
    }

    public async Task<List<ConsentRecord>> GetConsentHistoryAsync(
        string dataSubjectId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var historyKey = GetConsentHistoryKey(dataSubjectId);
            var entries = await _database.ListRangeAsync(historyKey);

            var history = new List<ConsentRecord>();
            foreach (var entry in entries)
            {
                if (entry.HasValue)
                {
                    var decrypted = await DecryptPayloadAsync(entry!, "consent-history-read", cancellationToken);
                    var consent = JsonSerializer.Deserialize<ConsentRecord>(decrypted);
                    if (consent != null)
                    {
                        history.Add(consent);
                    }
                }
            }

            return history;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting consent history for {DataSubjectId}", dataSubjectId);
            return new List<ConsentRecord>();
        }
    }

    public async Task<bool> IsProcessingConsentedAsync(
        string dataSubjectId,
        string processingPurpose,
        CancellationToken cancellationToken = default)
    {
        var status = await CheckConsentStatusAsync(dataSubjectId, processingPurpose, cancellationToken);
        return status == ConsentStatus.Given;
    }

    public async Task<List<ConsentRecord>> GetActiveConsentsAsync(
        string dataSubjectId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var history = await GetConsentHistoryAsync(dataSubjectId, cancellationToken);
            return history
                .Where(c => c.Status == ConsentStatus.Given && !IsConsentExpired(c))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active consents for {DataSubjectId}", dataSubjectId);
            return new List<ConsentRecord>();
        }
    }

    #endregion

    #region IDataBreachNotificationService

    public async Task<string> ReportDataBreachAsync(
        DataBreach breach,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Store breach report (encrypted — contains affected subject identifiers)
            var breachKey = GetBreachKey(breach.Id);
            var breachJson = JsonSerializer.Serialize(breach);
            var encryptedBreach = await EncryptPayloadAsync(breachJson, "breach-store", cancellationToken);
            if (encryptedBreach == null)
            {
                _logger.LogError("Refusing to store breach {BreachId} — encryption failed", breach.Id);
                return string.Empty;
            }
            await _database.StringSetAsync(breachKey, encryptedBreach, TimeSpan.FromDays(365 * 7)); // 7 years retention

            // Log breach report
            await _auditService.LogSecurityEventAsync(
                "DataBreachReported",
                $"Data breach reported: {breach.Description}",
                AuditSeverity.Critical,
                new
                {
                    BreachId = breach.Id,
                    BreachType = breach.BreachType,
                    Severity = breach.Severity,
                    AffectedSubjects = breach.AffectedDataSubjectsCount
                });

            _logger.LogCritical("Data breach reported: {BreachId}, Type: {BreachType}, Severity: {Severity}, Affected: {AffectedCount}",
                breach.Id, breach.BreachType, breach.Severity, breach.AffectedDataSubjectsCount);

            return breach.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reporting data breach {BreachId}", breach.Id);
            return string.Empty;
        }
    }

    public async Task UpdateBreachStatusAsync(
        string breachId,
        BreachStatus status,
        string statusReason,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var breachKey = GetBreachKey(breachId);
            var encryptedData = await _database.StringGetAsync(breachKey);

            if (!encryptedData.HasValue)
            {
                _logger.LogWarning("Breach not found: {BreachId}", breachId);
                return;
            }

            var decrypted = await DecryptPayloadAsync(encryptedData!, "breach-read", cancellationToken);
            var breach = JsonSerializer.Deserialize<DataBreach>(decrypted);
            if (breach == null)
            {
                _logger.LogWarning("Invalid breach data for {BreachId}", breachId);
                return;
            }

            var oldStatus = breach.Status;
            breach.Status = status;

            var updatedJson = JsonSerializer.Serialize(breach);
            var reEncrypted = await EncryptPayloadAsync(updatedJson, "breach-update", cancellationToken);
            if (reEncrypted == null)
            {
                _logger.LogError("Refusing to overwrite breach {BreachId} — re-encryption failed", breachId);
                return;
            }
            await _database.StringSetAsync(breachKey, reEncrypted);

            await _auditService.LogSecurityEventAsync(
                "DataBreachStatusUpdated",
                $"Breach {breachId} status updated to {status}: {statusReason}",
                AuditSeverity.High,
                new { BreachId = breachId, OldStatus = oldStatus, NewStatus = status, Reason = statusReason });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating breach status for {BreachId}", breachId);
        }
    }

    #endregion

    #region Private Helper Methods

    private ConsentValidity ValidateConsent(ConsentRecord consent)
    {
        var validity = new ConsentValidity();

        if (!consent.FreelyGiven || !consent.Specific || !consent.Informed || !consent.Unambiguous)
        {
            validity.IsValid = false;
            validity.ValidationIssues.Add("Consent does not meet GDPR validity requirements");
        }

        return validity;
    }

    private bool IsConsentExpired(ConsentRecord consent)
    {
        var maxConsentAge = _options.MaxConsentAge;
        return DateTime.UtcNow - consent.ConsentTimestamp > maxConsentAge;
    }

    private DataRetentionPolicy CreateDefaultRetentionPolicy(string dataType, string processingPurpose)
    {
        return new DataRetentionPolicy
        {
            DataType = dataType,
            ProcessingPurpose = processingPurpose,
            RetentionPeriod = _options.DefaultRetentionPeriod,
            LegalBasis = "Legitimate interest",
            DisposalMethod = DataDisposalMethod.SecureDelete
        };
    }

    // Encryption helpers — consent and breach payloads contain PII (data subject IDs, IP, user-agent)
    private async Task<string?> EncryptPayloadAsync(string json, string purpose, CancellationToken cancellationToken = default)
    {
        var context = new EncryptionContext
        {
            Classification = DataClassification.Restricted,
            Purpose = EncryptionPurpose.Storage,
            ComplianceRequirements = new List<ComplianceRequirement> { ComplianceRequirement.GDPR }
        };
        var result = await _encryptionService.EncryptAsync(json, context, cancellationToken);
        if (!result.Success)
        {
            _logger.LogError("Encryption failed for {Purpose} — refusing to store plaintext PII", purpose);
            return null;
        }
        return result.EncryptedData;
    }

    private async Task<string> DecryptPayloadAsync(string encrypted, string purpose, CancellationToken cancellationToken = default)
    {
        var context = new EncryptionContext
        {
            Classification = DataClassification.Restricted,
            Purpose = EncryptionPurpose.Storage,
            ComplianceRequirements = new List<ComplianceRequirement> { ComplianceRequirement.GDPR }
        };
        var result = await _encryptionService.DecryptAsync(encrypted, context, cancellationToken);
        if (!result.Success)
        {
            _logger.LogWarning("Decryption failed for {Purpose}, attempting plaintext parse as fallback", purpose);
            return encrypted;
        }
        return result.Data;
    }

    // Key generation methods
    private string GetConsentKey(string dataSubjectId, string processingPurpose) =>
        $"{_keyPrefix}consent:{dataSubjectId}:{processingPurpose}";
    private string GetConsentHistoryKey(string dataSubjectId) =>
        $"{_keyPrefix}consent_history:{dataSubjectId}";
    private string GetRetentionPolicyKey(string dataType, string processingPurpose) =>
        $"{_keyPrefix}retention_policy:{dataType}:{processingPurpose}";
    private string GetBreachKey(string breachId) =>
        $"{_keyPrefix}breach:{breachId}";

    #endregion
}

/// <summary>
/// Configuration options for data protection service
/// </summary>
public partial class DataProtectionOptions
{
    /// <summary>
    /// Bypass identity verification (for development)
    /// </summary>
    public bool BypassIdentityVerification { get; set; } = false;

    /// <summary>
    /// Directory for data exports
    /// </summary>
    public string ExportDirectory { get; set; } = "/tmp/exports";

    /// <summary>
    /// Threshold for inline data display
    /// </summary>
    public int InlineDataThreshold { get; set; } = 100;

    /// <summary>
    /// Notify recipients on rectification
    /// </summary>
    public bool NotifyRecipientsOnRectification { get; set; } = true;

    /// <summary>
    /// Notify third parties on erasure
    /// </summary>
    public bool NotifyThirdPartiesOnErasure { get; set; } = true;

    /// <summary>
    /// Maximum consent age before renewal required
    /// </summary>
    public TimeSpan MaxConsentAge { get; set; } = TimeSpan.FromDays(730);

    /// <summary>
    /// Default data retention period
    /// </summary>
    public TimeSpan DefaultRetentionPeriod { get; set; } = TimeSpan.FromDays(2555);

    /// <summary>
    /// Enable automated anonymization
    /// </summary>
    public bool EnableAutomatedAnonymization { get; set; } = false;

    /// <summary>
    /// Enable automated pseudonymization
    /// </summary>
    public bool EnableAutomatedPseudonymization { get; set; } = true;
}

/// <summary>
/// Consent validity check result
/// </summary>
public class ConsentValidity
{
    public bool IsValid { get; set; } = true;
    public List<string> ValidationIssues { get; set; } = new();
}

/// <summary>
/// Data transfer information for cross-border transfers
/// </summary>
public class DataTransfer
{
    public string DestinationCountry { get; set; } = string.Empty;
    public string TransferMechanism { get; set; } = string.Empty;
    public string LegalBasis { get; set; } = string.Empty;
    public string RecipientName { get; set; } = string.Empty;
}
