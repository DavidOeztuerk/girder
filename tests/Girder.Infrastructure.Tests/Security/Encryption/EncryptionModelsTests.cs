using Girder.Infrastructure.Security.Encryption;
using Microsoft.Extensions.DependencyInjection;

namespace Girder.Infrastructure.Tests.Security.Encryption;

[Trait("Category", "Unit")]
public class EncryptionModelsTests
{
    #region Attributes

    [Fact]
    public void EncryptedAttribute_DefaultConstruction_HasExpectedDefaults()
    {
        var attr = new EncryptedAttribute();

        attr.Classification.Should().Be(DataClassification.Confidential);
        attr.Purpose.Should().Be(EncryptionPurpose.Storage);
        attr.KeyId.Should().BeNull();
        attr.Algorithm.Should().BeNull();
        attr.IncludeIntegrityCheck.Should().BeTrue();
        attr.CompressBeforeEncryption.Should().BeFalse();
        attr.ComplianceRequirements.Should().BeNull();
        attr.RetentionPeriod.Should().BeNull();
        attr.GeographicRestriction.Should().BeNull();
        attr.ContextMetadata.Should().BeNull();
    }

    [Fact]
    public void EncryptedAttribute_PropertyAssignment_Works()
    {
        var attr = new EncryptedAttribute
        {
            Classification = DataClassification.TopSecret,
            Purpose = EncryptionPurpose.Transit,
            KeyId = "key-1",
            Algorithm = EncryptionAlgorithm.AES256GCM,
            IncludeIntegrityCheck = false,
            CompressBeforeEncryption = true,
            ComplianceRequirements = new[] { ComplianceRequirement.GDPR },
            RetentionPeriod = "7years",
            GeographicRestriction = "EU",
            ContextMetadata = "test"
        };

        attr.Classification.Should().Be(DataClassification.TopSecret);
        attr.Purpose.Should().Be(EncryptionPurpose.Transit);
        attr.KeyId.Should().Be("key-1");
        attr.Algorithm.Should().Be(EncryptionAlgorithm.AES256GCM);
        attr.IncludeIntegrityCheck.Should().BeFalse();
        attr.CompressBeforeEncryption.Should().BeTrue();
        attr.ComplianceRequirements.Should().Contain(ComplianceRequirement.GDPR);
    }

    [Fact]
    public void SensitiveAttribute_DefaultConstruction_HasExpectedDefaults()
    {
        var attr = new SensitiveAttribute();

        attr.Algorithm.Should().Be(HashingAlgorithm.Argon2id);
        attr.Classification.Should().Be(DataClassification.Confidential);
        attr.UsePepper.Should().BeTrue();
        attr.SaltSize.Should().Be(32);
        attr.MemoryCost.Should().Be(65536);
        attr.TimeCost.Should().Be(3);
        attr.Parallelism.Should().Be(1);
    }

    [Fact]
    public void SensitiveAttribute_PropertyAssignment_Works()
    {
        var attr = new SensitiveAttribute
        {
            Algorithm = HashingAlgorithm.BCrypt,
            Classification = DataClassification.Restricted,
            UsePepper = false,
            SaltSize = 64,
            MemoryCost = 131072,
            TimeCost = 4,
            Parallelism = 2
        };

        attr.Algorithm.Should().Be(HashingAlgorithm.BCrypt);
        attr.Classification.Should().Be(DataClassification.Restricted);
        attr.UsePepper.Should().BeFalse();
        attr.SaltSize.Should().Be(64);
    }

    [Fact]
    public void ExcludeFromEncryptionAttribute_DefaultConstruction_HasNullReason()
    {
        var attr = new ExcludeFromEncryptionAttribute();

        attr.Reason.Should().BeNull();
    }

    [Fact]
    public void ExcludeFromEncryptionAttribute_WithReason_SetsReason()
    {
        var attr = new ExcludeFromEncryptionAttribute { Reason = "This is a public field" };

        attr.Reason.Should().Be("This is a public field");
    }

    [Fact]
    public void PersonalDataAttribute_DefaultConstruction_HasExpectedDefaults()
    {
        var attr = new PersonalDataAttribute();

        attr.Category.Should().Be(PiiCategory.General);
        attr.IsDirectIdentifier.Should().BeFalse();
        attr.IsQuasiIdentifier.Should().BeFalse();
        attr.ApplicableRights.Should().Be(DataSubjectRights.All);
        attr.LegalBasis.Should().BeNull();
        attr.PurposeLimitation.Should().BeNull();
        attr.RetentionPeriod.Should().BeNull();
        attr.CanBeAnonymized.Should().BeTrue();
        attr.RequiresPseudonymization.Should().BeFalse();
    }

    [Fact]
    public void PersonalDataAttribute_PropertyAssignment_Works()
    {
        var attr = new PersonalDataAttribute
        {
            Category = PiiCategory.Health,
            IsDirectIdentifier = true,
            ApplicableRights = DataSubjectRights.Access | DataSubjectRights.Erasure,
            LegalBasis = "Consent",
            CanBeAnonymized = false,
            RequiresPseudonymization = true
        };

        attr.Category.Should().Be(PiiCategory.Health);
        attr.IsDirectIdentifier.Should().BeTrue();
        attr.LegalBasis.Should().Be("Consent");
        attr.CanBeAnonymized.Should().BeFalse();
        attr.RequiresPseudonymization.Should().BeTrue();
    }

    [Fact]
    public void TokenizedAttribute_DefaultConstruction_HasExpectedDefaults()
    {
        var attr = new TokenizedAttribute();

        attr.Method.Should().Be(TokenizationMethod.Format_Preserving);
        attr.TokenFormat.Should().BeNull();
        attr.PreserveFormat.Should().BeTrue();
        attr.Reversible.Should().BeTrue();
        attr.VaultId.Should().BeNull();
    }

    [Fact]
    public void TokenizedAttribute_PropertyAssignment_Works()
    {
        var attr = new TokenizedAttribute
        {
            Method = TokenizationMethod.Cryptographic,
            TokenFormat = "####-####",
            PreserveFormat = false,
            Reversible = false,
            VaultId = "vault-1"
        };

        attr.Method.Should().Be(TokenizationMethod.Cryptographic);
        attr.TokenFormat.Should().Be("####-####");
        attr.PreserveFormat.Should().BeFalse();
        attr.VaultId.Should().Be("vault-1");
    }

    [Fact]
    public void MaskedAttribute_DefaultConstruction_HasExpectedDefaults()
    {
        var attr = new MaskedAttribute();

        attr.Method.Should().Be(MaskingMethod.Partial);
        attr.MaskCharacter.Should().Be('*');
        attr.ShowStart.Should().Be(2);
        attr.ShowEnd.Should().Be(2);
        attr.MinLengthForMasking.Should().Be(4);
        attr.CustomPattern.Should().BeNull();
        attr.UnmaskForRoles.Should().BeNull();
    }

    [Fact]
    public void MaskedAttribute_PropertyAssignment_Works()
    {
        var attr = new MaskedAttribute
        {
            Method = MaskingMethod.Complete,
            MaskCharacter = 'X',
            ShowStart = 0,
            ShowEnd = 4,
            UnmaskForRoles = new[] { "Admin" }
        };

        attr.Method.Should().Be(MaskingMethod.Complete);
        attr.MaskCharacter.Should().Be('X');
        attr.ShowEnd.Should().Be(4);
        attr.UnmaskForRoles.Should().Contain("Admin");
    }

    [Fact]
    public void AuditedAttribute_DefaultConstruction_HasExpectedDefaults()
    {
        var attr = new AuditedAttribute();

        attr.LogAccess.Should().BeTrue();
        attr.LogModification.Should().BeTrue();
        attr.Level.Should().Be(AuditLevel.Standard);
        attr.IncludeValue.Should().BeFalse();
        attr.IncludeOldValue.Should().BeFalse();
        attr.AuditContext.Should().BeNull();
    }

    [Fact]
    public void AuditedAttribute_PropertyAssignment_Works()
    {
        var attr = new AuditedAttribute
        {
            LogAccess = false,
            Level = AuditLevel.Full,
            IncludeValue = true,
            IncludeOldValue = true,
            AuditContext = "payment"
        };

        attr.LogAccess.Should().BeFalse();
        attr.Level.Should().Be(AuditLevel.Full);
        attr.IncludeValue.Should().BeTrue();
        attr.AuditContext.Should().Be("payment");
    }

    [Fact]
    public void DataClassificationAttribute_ConstructorSetsLevel()
    {
        var attr = new DataClassificationAttribute(DataClassification.TopSecret);

        attr.Level.Should().Be(DataClassification.TopSecret);
        attr.HandlingInstructions.Should().BeNull();
        attr.DeclassificationDate.Should().BeNull();
        attr.ClassificationAuthority.Should().BeNull();
    }

    [Fact]
    public void DataClassificationAttribute_PropertyAssignment_Works()
    {
        var attr = new DataClassificationAttribute(DataClassification.Confidential)
        {
            HandlingInstructions = "Handle with care",
            DeclassificationDate = "2030-01-01",
            ClassificationAuthority = "Security Team"
        };

        attr.HandlingInstructions.Should().Be("Handle with care");
        attr.DeclassificationDate.Should().Be("2030-01-01");
        attr.ClassificationAuthority.Should().Be("Security Team");
    }

    #endregion

    #region KeyManagement Models

    [Fact]
    public void KeyGenerationOptions_DefaultConstruction_HasExpectedDefaults()
    {
        var opts = new KeyGenerationOptions();

        opts.Purpose.Should().Be(KeyPurpose.DataEncryption);
        opts.KeySize.Should().Be(256);
        opts.ExpiresAt.Should().BeNull();
        opts.RotationInterval.Should().BeNull();
        opts.UsageRestrictions.Should().BeNull();
        opts.GeographicRestrictions.Should().BeEmpty();
        opts.ComplianceRequirements.Should().BeEmpty();
    }

    [Fact]
    public void KeyUsageRestrictions_DefaultConstruction_HasExpectedDefaults()
    {
        var restrictions = new KeyUsageRestrictions();

        restrictions.MaxEncryptionOperations.Should().BeNull();
        restrictions.MaxDataSize.Should().BeNull();
        restrictions.AllowedIpRanges.Should().BeEmpty();
        restrictions.AllowedRoles.Should().BeEmpty();
        restrictions.TimeRestrictions.Should().BeNull();
    }

    [Fact]
    public void TimeBasedRestrictions_DefaultConstruction_HasExpectedDefaults()
    {
        var restrictions = new TimeBasedRestrictions();

        restrictions.ValidFrom.Should().BeNull();
        restrictions.ValidUntil.Should().BeNull();
        restrictions.AllowedDaysOfWeek.Should().BeEmpty();
        restrictions.AllowedHoursOfDay.Should().BeEmpty();
    }

    [Fact]
    public void TimeBasedRestrictions_PropertyAssignment_Works()
    {
        var validFrom = DateTime.UtcNow.AddDays(-1);
        var validUntil = DateTime.UtcNow.AddDays(30);
        var restrictions = new TimeBasedRestrictions
        {
            ValidFrom = validFrom,
            ValidUntil = validUntil,
            AllowedDaysOfWeek = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Friday },
            AllowedHoursOfDay = new List<int> { 9, 10, 11, 12, 13, 14, 15, 16, 17 }
        };

        restrictions.ValidFrom.Should().Be(validFrom);
        restrictions.ValidUntil.Should().Be(validUntil);
        restrictions.AllowedDaysOfWeek.Should().Contain(DayOfWeek.Monday);
        restrictions.AllowedHoursOfDay.Should().Contain(9);
    }

    [Fact]
    public void FieldEncryptionInfo_DefaultConstruction_HasExpectedDefaults()
    {
        var info = new FieldEncryptionInfo();

        info.FieldName.Should().Be(string.Empty);
        info.KeyId.Should().Be(string.Empty);
        info.Algorithm.Should().Be(EncryptionAlgorithm.AES128GCM); // default enum value
    }

    [Fact]
    public void FieldEncryptionInfo_PropertyAssignment_Works()
    {
        var ts = DateTime.UtcNow;
        var info = new FieldEncryptionInfo
        {
            FieldName = "email",
            KeyId = "key-123",
            Algorithm = EncryptionAlgorithm.AES256GCM,
            EncryptionTimestamp = ts,
            Classification = DataClassification.Confidential
        };

        info.FieldName.Should().Be("email");
        info.KeyId.Should().Be("key-123");
        info.Algorithm.Should().Be(EncryptionAlgorithm.AES256GCM);
        info.Classification.Should().Be(DataClassification.Confidential);
    }

    [Fact]
    public void DataEncryptionOptions_DefaultConstruction_HasExpectedDefaults()
    {
        var opts = new DataEncryptionOptions();

        opts.DefaultAlgorithm.Should().Be(EncryptionAlgorithm.AES256GCM);
        opts.DefaultHashingAlgorithm.Should().Be(HashingAlgorithm.Argon2id);
        opts.DefaultPepper.Should().BeNull();
        opts.LogOperations.Should().BeTrue();
        opts.CacheKeyMetadata.Should().BeTrue();
        opts.KeyMetadataCacheDuration.Should().Be(TimeSpan.FromMinutes(15));
        opts.MaxDataSize.Should().BeGreaterThan(0);
        opts.CompressionThreshold.Should().BeGreaterThan(0);
    }

    [Fact]
    public void KeyManagementOptions_DefaultConstruction_HasExpectedDefaults()
    {
        var opts = new KeyManagementOptions();

        opts.AutoRotateKeys.Should().BeFalse();
        opts.DefaultRotationInterval.Should().Be(TimeSpan.FromDays(90));
        opts.MasterKey.Should().BeNull();
        opts.BackupEncryptionKey.Should().BeNull();
        opts.EnableUsageMonitoring.Should().BeTrue();
        opts.MaxKeyVersions.Should().Be(10);
        opts.AutoCreateBackups.Should().BeTrue();
    }

    #endregion

    #region EncryptionBuilder

    [Fact]
    public void EncryptionBuilder_Construction_RegistersServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<StackExchange.Redis.IConnectionMultiplexer>());
        services.AddOptions();

        var builder = new EncryptionBuilder(services);

        builder.Should().NotBeNull();
    }

    [Fact]
    public void EncryptionBuilder_ConfigureEncryption_ReturnsBuilder()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<StackExchange.Redis.IConnectionMultiplexer>());
        services.AddOptions();

        var builder = new EncryptionBuilder(services);
        var result = builder.ConfigureEncryption(opts => opts.LogOperations = false);

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void EncryptionBuilder_ConfigureKeyManagement_ReturnsBuilder()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<StackExchange.Redis.IConnectionMultiplexer>());
        services.AddOptions();

        var builder = new EncryptionBuilder(services);
        var result = builder.ConfigureKeyManagement(opts => opts.AutoRotateKeys = true);

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void EncryptionBuilder_EnableAutoKeyRotation_ReturnsBuilder()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<StackExchange.Redis.IConnectionMultiplexer>());
        services.AddOptions();

        var builder = new EncryptionBuilder(services);
        var result = builder.EnableAutoKeyRotation(TimeSpan.FromDays(30));

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void EncryptionBuilder_UseEncryptionAlgorithm_ReturnsBuilder()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<StackExchange.Redis.IConnectionMultiplexer>());
        services.AddOptions();

        var builder = new EncryptionBuilder(services);
        var result = builder.UseEncryptionAlgorithm(EncryptionAlgorithm.ChaCha20Poly1305);

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void EncryptionBuilder_UseHashingAlgorithm_ReturnsBuilder()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<StackExchange.Redis.IConnectionMultiplexer>());
        services.AddOptions();

        var builder = new EncryptionBuilder(services);
        var result = builder.UseHashingAlgorithm(HashingAlgorithm.SHA512);

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void EncryptionBuilder_EnableOperationLogging_ReturnsBuilder()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<StackExchange.Redis.IConnectionMultiplexer>());
        services.AddOptions();

        var builder = new EncryptionBuilder(services);
        var result = builder.EnableOperationLogging(false);

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void EncryptionBuilder_ConfigureMasterKeys_ReturnsBuilder()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<StackExchange.Redis.IConnectionMultiplexer>());
        services.AddOptions();

        var builder = new EncryptionBuilder(services);
        var result = builder.ConfigureMasterKeys("master-key", "backup-key");

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void EncryptionBuilder_ForDevelopment_ReturnsBuilder()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<StackExchange.Redis.IConnectionMultiplexer>());
        services.AddOptions();

        var builder = new EncryptionBuilder(services);
        var result = builder.ForDevelopment();

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void EncryptionBuilder_ForProduction_ReturnsBuilder()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<StackExchange.Redis.IConnectionMultiplexer>());
        services.AddOptions();

        var builder = new EncryptionBuilder(services);
        var result = builder.ForProduction();

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void EncryptionBuilder_AddKeyPurpose_ReturnsBuilder()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<StackExchange.Redis.IConnectionMultiplexer>());
        services.AddOptions();

        var builder = new EncryptionBuilder(services);
        var result = builder.AddKeyPurpose(KeyPurpose.Signing, new KeyGenerationOptions());

        result.Should().BeSameAs(builder);
    }

    #endregion

    #region KeyDerivationInfo

    [Fact]
    public void KeyDerivationInfo_DefaultConstruction_HasExpectedDefaults()
    {
        var info = new KeyDerivationInfo();

        info.Function.Should().Be(KeyDerivationFunction.PBKDF2); // default enum value
        info.Salt.Should().BeEmpty();
        info.Iterations.Should().Be(0);
        info.Parameters.Should().BeEmpty();
    }

    [Fact]
    public void KeyDerivationInfo_PropertyAssignment_Works()
    {
        var salt = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var info = new KeyDerivationInfo
        {
            Function = KeyDerivationFunction.Argon2id,
            Salt = salt,
            Iterations = 100000,
            Parameters = new Dictionary<string, object> { ["memoryCost"] = 65536, ["parallelism"] = 1 }
        };

        info.Function.Should().Be(KeyDerivationFunction.Argon2id);
        info.Salt.Should().BeEquivalentTo(salt);
        info.Iterations.Should().Be(100000);
        info.Parameters.Should().ContainKey("memoryCost");
        info.Parameters["memoryCost"].Should().Be(65536);
    }

    #endregion

    #region KeyRotationSchedule

    [Fact]
    public void KeyRotationSchedule_DefaultConstruction_HasExpectedDefaults()
    {
        var schedule = new KeyRotationSchedule();

        schedule.AutoRotateEnabled.Should().BeFalse();
        schedule.WarningThreshold.Should().Be(TimeSpan.FromDays(7));
        schedule.MaxKeyAge.Should().Be(TimeSpan.FromDays(365));
        schedule.RotationUsageThreshold.Should().BeNull();
        schedule.RotationDataThreshold.Should().BeNull();
    }

    [Fact]
    public void KeyRotationSchedule_PropertyAssignment_Works()
    {
        var interval = TimeSpan.FromDays(30);
        var nextRotation = DateTime.UtcNow.AddDays(30);
        var schedule = new KeyRotationSchedule
        {
            Interval = interval,
            NextRotation = nextRotation,
            AutoRotateEnabled = true,
            WarningThreshold = TimeSpan.FromDays(3),
            MaxKeyAge = TimeSpan.FromDays(180),
            RotationUsageThreshold = 1_000_000L,
            RotationDataThreshold = 10_000_000L
        };

        schedule.Interval.Should().Be(interval);
        schedule.NextRotation.Should().Be(nextRotation);
        schedule.AutoRotateEnabled.Should().BeTrue();
        schedule.WarningThreshold.Should().Be(TimeSpan.FromDays(3));
        schedule.MaxKeyAge.Should().Be(TimeSpan.FromDays(180));
        schedule.RotationUsageThreshold.Should().Be(1_000_000L);
        schedule.RotationDataThreshold.Should().Be(10_000_000L);
    }

    #endregion

    #region KeyBackupInfo

    [Fact]
    public void KeyBackupInfo_DefaultConstruction_HasExpectedDefaults()
    {
        var info = new KeyBackupInfo();

        info.BackupId.Should().Be(string.Empty);
        info.BackupLocation.Should().Be(string.Empty);
        info.VerificationHash.Should().Be(string.Empty);
        info.BackupEncryptionKeyId.Should().BeNull();
        info.RecoveryTestedDate.Should().BeNull();
        info.RetentionPeriod.Should().Be(TimeSpan.FromDays(2555));
        info.Status.Should().Be(BackupStatus.Valid);
    }

    [Fact]
    public void KeyBackupInfo_PropertyAssignment_Works()
    {
        var ts = DateTime.UtcNow;
        var tested = DateTime.UtcNow.AddDays(-10);
        var info = new KeyBackupInfo
        {
            BackupId = "backup-001",
            BackupTimestamp = ts,
            BackupLocation = "s3://bucket/keys",
            VerificationHash = "abc123",
            BackupEncryptionKeyId = "master-key-1",
            RecoveryTestedDate = tested,
            RetentionPeriod = TimeSpan.FromDays(365),
            Status = BackupStatus.NeedsVerification
        };

        info.BackupId.Should().Be("backup-001");
        info.BackupTimestamp.Should().Be(ts);
        info.BackupLocation.Should().Be("s3://bucket/keys");
        info.VerificationHash.Should().Be("abc123");
        info.BackupEncryptionKeyId.Should().Be("master-key-1");
        info.RecoveryTestedDate.Should().Be(tested);
        info.RetentionPeriod.Should().Be(TimeSpan.FromDays(365));
        info.Status.Should().Be(BackupStatus.NeedsVerification);
    }

    #endregion

    #region KeyMetadata

    [Fact]
    public void KeyMetadata_DefaultConstruction_HasExpectedDefaults()
    {
        var meta = new KeyMetadata();

        meta.Id.Should().Be(string.Empty);
        meta.KeySize.Should().Be(0);
        meta.Version.Should().Be(0);
        meta.UsageCount.Should().Be(0);
        meta.HasBackup.Should().BeFalse();
        meta.GeographicRestrictions.Should().BeEmpty();
        meta.ComplianceRequirements.Should().BeEmpty();
        meta.LastUsed.Should().BeNull();
        meta.NextRotation.Should().BeNull();
        meta.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public void KeyMetadata_PropertyAssignment_Works()
    {
        var created = DateTime.UtcNow.AddDays(-30);
        var expires = DateTime.UtcNow.AddYears(1);
        var lastUsed = DateTime.UtcNow.AddHours(-1);
        var nextRot = DateTime.UtcNow.AddDays(60);

        var meta = new KeyMetadata
        {
            Id = "key-abc",
            KeyType = KeyType.Symmetric,
            Purpose = KeyPurpose.DataEncryption,
            KeySize = 256,
            Status = KeyStatus.Active,
            Version = 3,
            CreatedAt = created,
            ExpiresAt = expires,
            LastUsed = lastUsed,
            UsageCount = 500,
            NextRotation = nextRot,
            HasBackup = true,
            GeographicRestrictions = new List<string> { "EU", "US" },
            ComplianceRequirements = new List<ComplianceRequirement> { ComplianceRequirement.GDPR, ComplianceRequirement.HIPAA }
        };

        meta.Id.Should().Be("key-abc");
        meta.KeyType.Should().Be(KeyType.Symmetric);
        meta.Purpose.Should().Be(KeyPurpose.DataEncryption);
        meta.KeySize.Should().Be(256);
        meta.Status.Should().Be(KeyStatus.Active);
        meta.Version.Should().Be(3);
        meta.CreatedAt.Should().Be(created);
        meta.ExpiresAt.Should().Be(expires);
        meta.LastUsed.Should().Be(lastUsed);
        meta.UsageCount.Should().Be(500);
        meta.NextRotation.Should().Be(nextRot);
        meta.HasBackup.Should().BeTrue();
        meta.GeographicRestrictions.Should().Contain("EU");
        meta.ComplianceRequirements.Should().Contain(ComplianceRequirement.GDPR);
    }

    #endregion

    #region DailyUsageStatistics

    [Fact]
    public void DailyUsageStatistics_DefaultConstruction_HasExpectedDefaults()
    {
        var stats = new DailyUsageStatistics();

        stats.EncryptionOperations.Should().Be(0);
        stats.DecryptionOperations.Should().Be(0);
        stats.DataEncrypted.Should().Be(0);
        stats.DataDecrypted.Should().Be(0);
        stats.PeakOperationsPerHour.Should().Be(0);
        stats.UniqueUsers.Should().BeEmpty();
    }

    [Fact]
    public void DailyUsageStatistics_PropertyAssignment_Works()
    {
        var date = DateTime.UtcNow.Date;
        var stats = new DailyUsageStatistics
        {
            Date = date,
            EncryptionOperations = 100,
            DecryptionOperations = 50,
            DataEncrypted = 4096,
            DataDecrypted = 2048,
            PeakOperationsPerHour = 20,
            UniqueUsers = new HashSet<string> { "user-1", "user-2" }
        };

        stats.Date.Should().Be(date);
        stats.EncryptionOperations.Should().Be(100);
        stats.DecryptionOperations.Should().Be(50);
        stats.DataEncrypted.Should().Be(4096);
        stats.DataDecrypted.Should().Be(2048);
        stats.PeakOperationsPerHour.Should().Be(20);
        stats.UniqueUsers.Should().Contain("user-1");
    }

    #endregion

    #region BackupStatus enum

    [Fact]
    public void BackupStatus_HasAllExpectedValues()
    {
        var values = Enum.GetValues<BackupStatus>();

        values.Should().Contain(BackupStatus.Valid);
        values.Should().Contain(BackupStatus.NeedsVerification);
        values.Should().Contain(BackupStatus.VerificationFailed);
        values.Should().Contain(BackupStatus.Corrupted);
        values.Should().Contain(BackupStatus.Expired);
        values.Should().Contain(BackupStatus.Missing);
    }

    #endregion

    #region Enumerations

    [Fact]
    public void Enumerations_HaveExpectedValues()
    {
        Enum.GetValues<DataClassification>().Should().Contain(DataClassification.TopSecret);
        Enum.GetValues<EncryptionPurpose>().Should().Contain(EncryptionPurpose.Sharing);
        Enum.GetValues<EncryptionAlgorithm>().Should().Contain(EncryptionAlgorithm.ChaCha20Poly1305);
        Enum.GetValues<HashingAlgorithm>().Should().Contain(HashingAlgorithm.PBKDF2);
        Enum.GetValues<KeyDerivationFunction>().Should().Contain(KeyDerivationFunction.HKDF);
        Enum.GetValues<KeyType>().Should().Contain(KeyType.Hybrid);
        Enum.GetValues<KeyPurpose>().Should().Contain(KeyPurpose.DerivedKey);
        Enum.GetValues<ComplianceRequirement>().Should().Contain(ComplianceRequirement.ISO_27001);
        Enum.GetValues<PiiCategory>().Should().Contain(PiiCategory.Criminal);
        Enum.GetValues<TokenizationMethod>().Should().Contain(TokenizationMethod.Database_Lookup);
        Enum.GetValues<MaskingMethod>().Should().Contain(MaskingMethod.Last_Character_Only);
        Enum.GetValues<AuditLevel>().Should().Contain(AuditLevel.None);
    }

    [Fact]
    public void DataSubjectRights_Flags_WorkCorrectly()
    {
        var allRights = DataSubjectRights.All;

        allRights.Should().HaveFlag(DataSubjectRights.Access);
        allRights.Should().HaveFlag(DataSubjectRights.Rectification);
        allRights.Should().HaveFlag(DataSubjectRights.Erasure);
        allRights.Should().HaveFlag(DataSubjectRights.Portability);
        allRights.Should().HaveFlag(DataSubjectRights.Restriction);
        allRights.Should().HaveFlag(DataSubjectRights.Objection);

        DataSubjectRights.None.Should().Be(0);
    }

    #endregion
}
