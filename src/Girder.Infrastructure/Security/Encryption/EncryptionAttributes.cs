namespace Infrastructure.Security.Encryption;

/// <summary>
/// Attribute to mark properties for encryption
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public class EncryptedAttribute : Attribute
{
    /// <summary>
    /// Data classification level
    /// </summary>
    public DataClassification Classification { get; set; } = DataClassification.Confidential;

    /// <summary>
    /// Encryption purpose
    /// </summary>
    public EncryptionPurpose Purpose { get; set; } = EncryptionPurpose.Storage;

    /// <summary>
    /// Key ID to use for encryption (optional)
    /// </summary>
    public string? KeyId { get; set; }

    /// <summary>
    /// Encryption algorithm to use (optional)
    /// </summary>
    public EncryptionAlgorithm? Algorithm { get; set; }

    /// <summary>
    /// Whether to include integrity check
    /// </summary>
    public bool IncludeIntegrityCheck { get; set; } = true;

    /// <summary>
    /// Whether to compress before encryption
    /// </summary>
    public bool CompressBeforeEncryption { get; set; } = false;

    /// <summary>
    /// Compliance requirements
    /// </summary>
    public ComplianceRequirement[]? ComplianceRequirements { get; set; }

    /// <summary>
    /// Data retention period
    /// </summary>
    public string? RetentionPeriod { get; set; }

    /// <summary>
    /// Geographic restriction
    /// </summary>
    public string? GeographicRestriction { get; set; }

    /// <summary>
    /// Custom encryption context metadata
    /// </summary>
    public string? ContextMetadata { get; set; }
}

/// <summary>
/// Attribute to mark properties as sensitive (for hashing instead of encryption)
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public class SensitiveAttribute : Attribute
{
    /// <summary>
    /// Hashing algorithm to use
    /// </summary>
    public HashingAlgorithm Algorithm { get; set; } = HashingAlgorithm.Argon2id;

    /// <summary>
    /// Data classification level
    /// </summary>
    public DataClassification Classification { get; set; } = DataClassification.Confidential;

    /// <summary>
    /// Whether to use application-wide pepper
    /// </summary>
    public bool UsePepper { get; set; } = true;

    /// <summary>
    /// Salt size in bytes
    /// </summary>
    public int SaltSize { get; set; } = 32;

    /// <summary>
    /// Memory cost for Argon2 (in KB)
    /// </summary>
    public int MemoryCost { get; set; } = 65536;

    /// <summary>
    /// Time cost (iterations)
    /// </summary>
    public int TimeCost { get; set; } = 3;

    /// <summary>
    /// Parallelism degree
    /// </summary>
    public int Parallelism { get; set; } = 1;
}

/// <summary>
/// Attribute to exclude properties from encryption
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public class ExcludeFromEncryptionAttribute : Attribute
{
    /// <summary>
    /// Reason for exclusion
    /// </summary>
    public string? Reason { get; set; }
}

/// <summary>
/// Attribute to mark properties for PII (Personally Identifiable Information) handling
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public class PersonalDataAttribute : Attribute
{
    /// <summary>
    /// PII category
    /// </summary>
    public PiiCategory Category { get; set; } = PiiCategory.General;

    /// <summary>
    /// Whether this is direct identifier
    /// </summary>
    public bool IsDirectIdentifier { get; set; } = false;

    /// <summary>
    /// Whether this is quasi-identifier
    /// </summary>
    public bool IsQuasiIdentifier { get; set; } = false;

    /// <summary>
    /// Data subject rights applicable
    /// </summary>
    public DataSubjectRights ApplicableRights { get; set; } = DataSubjectRights.All;

    /// <summary>
    /// Legal basis for processing
    /// </summary>
    public string? LegalBasis { get; set; }

    /// <summary>
    /// Purpose limitation
    /// </summary>
    public string? PurposeLimitation { get; set; }

    /// <summary>
    /// Data retention period
    /// </summary>
    public string? RetentionPeriod { get; set; }

    /// <summary>
    /// Whether anonymization is possible
    /// </summary>
    public bool CanBeAnonymized { get; set; } = true;

    /// <summary>
    /// Whether pseudonymization is required
    /// </summary>
    public bool RequiresPseudonymization { get; set; } = false;
}

/// <summary>
/// Attribute to mark properties for tokenization
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public class TokenizedAttribute : Attribute
{
    /// <summary>
    /// Tokenization method
    /// </summary>
    public TokenizationMethod Method { get; set; } = TokenizationMethod.Format_Preserving;

    /// <summary>
    /// Token format (for format-preserving tokenization)
    /// </summary>
    public string? TokenFormat { get; set; }

    /// <summary>
    /// Whether to preserve format
    /// </summary>
    public bool PreserveFormat { get; set; } = true;

    /// <summary>
    /// Reversible tokenization
    /// </summary>
    public bool Reversible { get; set; } = true;

    /// <summary>
    /// Token vault identifier
    /// </summary>
    public string? VaultId { get; set; }
}

/// <summary>
/// Attribute to configure data masking
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public class MaskedAttribute : Attribute
{
    /// <summary>
    /// Masking method
    /// </summary>
    public MaskingMethod Method { get; set; } = MaskingMethod.Partial;

    /// <summary>
    /// Masking character
    /// </summary>
    public char MaskCharacter { get; set; } = '*';

    /// <summary>
    /// Number of characters to show at start
    /// </summary>
    public int ShowStart { get; set; } = 2;

    /// <summary>
    /// Number of characters to show at end
    /// </summary>
    public int ShowEnd { get; set; } = 2;

    /// <summary>
    /// Minimum length before masking
    /// </summary>
    public int MinLengthForMasking { get; set; } = 4;

    /// <summary>
    /// Custom masking pattern (regex)
    /// </summary>
    public string? CustomPattern { get; set; }

    /// <summary>
    /// Roles that can see unmasked data
    /// </summary>
    public string[]? UnmaskForRoles { get; set; }
}

/// <summary>
/// Attribute to mark fields for audit logging
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Class, AllowMultiple = false)]
public class AuditedAttribute : Attribute
{
    /// <summary>
    /// Whether to log field access
    /// </summary>
    public bool LogAccess { get; set; } = true;

    /// <summary>
    /// Whether to log field modifications
    /// </summary>
    public bool LogModification { get; set; } = true;

    /// <summary>
    /// Audit level
    /// </summary>
    public AuditLevel Level { get; set; } = AuditLevel.Standard;

    /// <summary>
    /// Include field value in audit log
    /// </summary>
    public bool IncludeValue { get; set; } = false;

    /// <summary>
    /// Include old value in modification logs
    /// </summary>
    public bool IncludeOldValue { get; set; } = false;

    /// <summary>
    /// Custom audit context
    /// </summary>
    public string? AuditContext { get; set; }
}

/// <summary>
/// Attribute for data classification
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Class, AllowMultiple = false)]
public class DataClassificationAttribute : Attribute
{
    /// <summary>
    /// Data classification level
    /// </summary>
    public DataClassification Level { get; set; }

    /// <summary>
    /// Handling instructions
    /// </summary>
    public string? HandlingInstructions { get; set; }

    /// <summary>
    /// Declassification date
    /// </summary>
    public string? DeclassificationDate { get; set; }

    /// <summary>
    /// Classification authority
    /// </summary>
    public string? ClassificationAuthority { get; set; }

    public DataClassificationAttribute(DataClassification level)
    {
        Level = level;
    }
}

/// <summary>
/// PII categories
/// </summary>
public enum PiiCategory
{
    General,
    Financial,
    Health,
    Biometric,
    Location,
    Behavioral,
    Communication,
    Identity,
    Professional,
    Educational,
    Criminal
}

/// <summary>
/// Data subject rights (GDPR)
/// </summary>
[Flags]
public enum DataSubjectRights
{
    None = 0,
    Access = 1,
    Rectification = 2,
    Erasure = 4,
    Portability = 8,
    Restriction = 16,
    Objection = 32,
    All = Access | Rectification | Erasure | Portability | Restriction | Objection
}

/// <summary>
/// Tokenization methods
/// </summary>
public enum TokenizationMethod
{
    /// <summary>
    /// Random token generation
    /// </summary>
    Random,

    /// <summary>
    /// Format-preserving tokenization
    /// </summary>
    Format_Preserving,

    /// <summary>
    /// Cryptographic tokenization
    /// </summary>
    Cryptographic,

    /// <summary>
    /// Hash-based tokenization
    /// </summary>
    Hash_Based,

    /// <summary>
    /// Database lookup tokenization
    /// </summary>
    Database_Lookup
}

/// <summary>
/// Data masking methods
/// </summary>
public enum MaskingMethod
{
    /// <summary>
    /// Show only partial data
    /// </summary>
    Partial,

    /// <summary>
    /// Mask all characters
    /// </summary>
    Complete,

    /// <summary>
    /// Mask with custom pattern
    /// </summary>
    Pattern,

    /// <summary>
    /// Replace with placeholder
    /// </summary>
    Placeholder,

    /// <summary>
    /// Show only length
    /// </summary>
    Length_Only,

    /// <summary>
    /// Show only first character
    /// </summary>
    First_Character_Only,

    /// <summary>
    /// Show only last character
    /// </summary>
    Last_Character_Only
}

/// <summary>
/// Audit levels
/// </summary>
public enum AuditLevel
{
    /// <summary>
    /// Basic audit logging
    /// </summary>
    Basic,

    /// <summary>
    /// Standard audit logging
    /// </summary>
    Standard,

    /// <summary>
    /// Detailed audit logging
    /// </summary>
    Detailed,

    /// <summary>
    /// Full audit logging with values
    /// </summary>
    Full,

    /// <summary>
    /// No audit logging
    /// </summary>
    None
}