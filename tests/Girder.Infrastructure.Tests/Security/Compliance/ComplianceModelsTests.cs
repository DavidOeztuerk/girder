using Infrastructure.Security.Compliance;

namespace Infrastructure.Tests.Security.Compliance;

[Trait("Category", "Unit")]
public class ComplianceModelsTests
{
    #region AnonymizationRequest

    [Fact]
    public void AnonymizationRequest_DefaultConstruction_HasExpectedDefaults()
    {
        var request = new AnonymizationRequest();

        request.Data.Should().NotBeNull();
        request.Technique.Should().Be(AnonymizationTechnique.KAnonymity);
        request.Parameters.Should().BeEmpty();
        request.FieldsToAnonymize.Should().BeEmpty();
        request.Level.Should().Be(AnonymizationLevel.Medium);
    }

    [Fact]
    public void AnonymizationRequest_PropertyAssignment_Works()
    {
        var request = new AnonymizationRequest
        {
            Data = new { Name = "test" },
            Technique = AnonymizationTechnique.DataMasking,
            Level = AnonymizationLevel.High,
            FieldsToAnonymize = new List<string> { "Name" },
            Parameters = new Dictionary<string, object> { ["k"] = 5 }
        };

        request.Technique.Should().Be(AnonymizationTechnique.DataMasking);
        request.Level.Should().Be(AnonymizationLevel.High);
        request.FieldsToAnonymize.Should().Contain("Name");
        request.Parameters.Should().ContainKey("k");
    }

    #endregion

    #region ConsentRecord

    [Fact]
    public void ConsentRecord_DefaultConstruction_HasExpectedDefaults()
    {
        var record = new ConsentRecord();

        record.Id.Should().NotBeNullOrEmpty(); // auto-generated GUID
        record.Status.Should().Be(ConsentStatus.Given);
        record.ConsentVersion.Should().Be("1.0");
        record.FreelyGiven.Should().BeTrue();
        record.Specific.Should().BeTrue();
        record.Informed.Should().BeTrue();
        record.Unambiguous.Should().BeTrue();
        record.DataCategories.Should().BeEmpty();
        record.Metadata.Should().BeEmpty();
    }

    [Fact]
    public void ConsentRecord_PropertyAssignment_Works()
    {
        var timestamp = DateTime.UtcNow.AddDays(-1);
        var record = new ConsentRecord
        {
            DataSubjectId = "user-1",
            ProcessingPurpose = "marketing",
            ConsentText = "I agree",
            Status = ConsentStatus.Withdrawn,
            ConsentTimestamp = timestamp,
            ConsentMethod = "web-form",
            IpAddress = "192.168.1.1",
            UserAgent = "Mozilla"
        };

        record.DataSubjectId.Should().Be("user-1");
        record.ProcessingPurpose.Should().Be("marketing");
        record.Status.Should().Be(ConsentStatus.Withdrawn);
        record.ConsentTimestamp.Should().Be(timestamp);
        record.IpAddress.Should().Be("192.168.1.1");
        record.UserAgent.Should().Be("Mozilla");
    }

    #endregion

    #region DataBreach

    [Fact]
    public void DataBreach_DefaultConstruction_HasExpectedDefaults()
    {
        var breach = new DataBreach();

        breach.Id.Should().NotBeNullOrEmpty();
        breach.BreachType.Should().Be(BreachType.DataLoss);
        breach.Severity.Should().Be(BreachSeverity.Medium);
        breach.RiskLevel.Should().Be(RiskLevel.Medium);
        breach.Status.Should().Be(BreachStatus.Discovered);
        breach.AffectedDataCategories.Should().BeEmpty();
        breach.AffectedDataSubjects.Should().BeEmpty();
        breach.LikelyConsequences.Should().BeEmpty();
        breach.MeasuresTaken.Should().BeEmpty();
    }

    [Fact]
    public void DataBreach_PropertyAssignment_Works()
    {
        var breach = new DataBreach
        {
            Description = "Unauthorized access",
            BreachType = BreachType.UnauthorizedAccess,
            Severity = BreachSeverity.Critical,
            RiskLevel = RiskLevel.VeryHigh,
            Status = BreachStatus.Contained,
            AffectedDataSubjectsCount = 100,
            AffectedDataCategories = new List<string> { "personal" }
        };

        breach.Description.Should().Be("Unauthorized access");
        breach.BreachType.Should().Be(BreachType.UnauthorizedAccess);
        breach.Severity.Should().Be(BreachSeverity.Critical);
        breach.AffectedDataSubjectsCount.Should().Be(100);
        breach.AffectedDataCategories.Should().Contain("personal");
    }

    #endregion

    #region DataCorrection

    [Fact]
    public void DataCorrection_DefaultConstruction_HasExpectedDefaults()
    {
        var correction = new DataCorrection();

        correction.FieldName.Should().Be(string.Empty);
        correction.CorrectedValue.Should().Be(string.Empty);
        correction.CurrentValue.Should().BeNull();
        correction.Reason.Should().Be(string.Empty);
    }

    [Fact]
    public void DataCorrection_PropertyAssignment_Works()
    {
        var correction = new DataCorrection
        {
            FieldName = "Email",
            CurrentValue = "old@email.com",
            CorrectedValue = "new@email.com",
            Reason = "User requested update"
        };

        correction.FieldName.Should().Be("Email");
        correction.CurrentValue.Should().Be("old@email.com");
        correction.CorrectedValue.Should().Be("new@email.com");
        correction.Reason.Should().Be("User requested update");
    }

    #endregion

    #region DataPortabilityRequest

    [Fact]
    public void DataPortabilityRequest_DefaultConstruction_HasExpectedDefaults()
    {
        var request = new DataPortabilityRequest();

        request.RequestId.Should().NotBeNullOrEmpty();
        request.ExportFormat.Should().Be(DataExportFormat.JSON);
        request.DataCategories.Should().BeEmpty();
        request.TargetSystem.Should().BeNull();
    }

    [Fact]
    public void DataPortabilityRequest_PropertyAssignment_Works()
    {
        var request = new DataPortabilityRequest
        {
            DataSubjectId = "user-1",
            ExportFormat = DataExportFormat.CSV,
            DataCategories = new List<string> { "profile", "activity" },
            TargetSystem = "another-service"
        };

        request.DataSubjectId.Should().Be("user-1");
        request.ExportFormat.Should().Be(DataExportFormat.CSV);
        request.DataCategories.Should().HaveCount(2);
        request.TargetSystem.Should().Be("another-service");
    }

    #endregion

    #region DataProcessingActivity

    [Fact]
    public void DataProcessingActivity_DefaultConstruction_HasExpectedDefaults()
    {
        var activity = new DataProcessingActivity();

        activity.Id.Should().NotBeNullOrEmpty();
        activity.Name.Should().Be(string.Empty);
        activity.Description.Should().Be(string.Empty);
        activity.RetentionPeriods.Should().BeEmpty();
        activity.CrossBorderTransfers.Should().BeEmpty();
    }

    [Fact]
    public void DataProcessingActivity_PropertyAssignment_Works()
    {
        var activity = new DataProcessingActivity
        {
            Name = "Profile Processing",
            Description = "Process user profiles",
            RetentionPeriods = new Dictionary<string, TimeSpan> { ["profile"] = TimeSpan.FromDays(365) }
        };

        activity.Name.Should().Be("Profile Processing");
        activity.RetentionPeriods.Should().ContainKey("profile");
    }

    #endregion

    #region DataRetentionPolicy

    [Fact]
    public void DataRetentionPolicy_DefaultConstruction_HasExpectedDefaults()
    {
        var policy = new DataRetentionPolicy();

        policy.Id.Should().NotBeNullOrEmpty();
        policy.DisposalMethod.Should().Be(DataDisposalMethod.SecureDelete);
        policy.RequireDisposalVerification.Should().BeTrue();
        policy.Version.Should().Be("1.0");
        policy.RetentionConditions.Should().BeEmpty();
    }

    [Fact]
    public void DataRetentionPolicy_PropertyAssignment_Works()
    {
        var policy = new DataRetentionPolicy
        {
            DataType = "UserProfile",
            ProcessingPurpose = "Account management",
            RetentionPeriod = TimeSpan.FromDays(730),
            LegalBasis = "Contract",
            DisposalMethod = DataDisposalMethod.Anonymization
        };

        policy.DataType.Should().Be("UserProfile");
        policy.LegalBasis.Should().Be("Contract");
        policy.RetentionPeriod.Should().Be(TimeSpan.FromDays(730));
        policy.DisposalMethod.Should().Be(DataDisposalMethod.Anonymization);
    }

    #endregion

    #region DataSubjectAccessRequest

    [Fact]
    public void DataSubjectAccessRequest_DefaultConstruction_HasExpectedDefaults()
    {
        var request = new DataSubjectAccessRequest();

        request.RequestId.Should().NotBeNullOrEmpty();
        request.PreferredFormat.Should().Be(DataExportFormat.JSON);
        request.RequestedDataCategories.Should().BeEmpty();
        request.AdditionalDetails.Should().BeEmpty();
        request.IdentityVerification.Should().NotBeNull();
    }

    [Fact]
    public void DataSubjectAccessRequest_PropertyAssignment_Works()
    {
        var request = new DataSubjectAccessRequest
        {
            DataSubjectId = "user-42",
            RequestSource = "web",
            PreferredFormat = DataExportFormat.XML,
            RequestedDataCategories = new List<string> { "profile" }
        };

        request.DataSubjectId.Should().Be("user-42");
        request.RequestSource.Should().Be("web");
        request.PreferredFormat.Should().Be(DataExportFormat.XML);
        request.RequestedDataCategories.Should().Contain("profile");
    }

    #endregion

    #region DataSubjectErasureRequest

    [Fact]
    public void DataSubjectErasureRequest_DefaultConstruction_HasExpectedDefaults()
    {
        var request = new DataSubjectErasureRequest();

        request.RequestId.Should().NotBeNullOrEmpty();
        request.ErasureType.Should().Be(ErasureType.SoftDelete);
        request.ErasureGrounds.Should().BeEmpty();
        request.DataCategoriesToErase.Should().BeEmpty();
    }

    [Fact]
    public void DataSubjectErasureRequest_PropertyAssignment_Works()
    {
        var request = new DataSubjectErasureRequest
        {
            DataSubjectId = "user-1",
            ErasureType = ErasureType.HardDelete,
            ErasureGrounds = new List<ErasureGround> { ErasureGround.ConsentWithdrawn },
            DataCategoriesToErase = new List<string> { "all" }
        };

        request.DataSubjectId.Should().Be("user-1");
        request.ErasureType.Should().Be(ErasureType.HardDelete);
        request.ErasureGrounds.Should().Contain(ErasureGround.ConsentWithdrawn);
    }

    #endregion

    #region DataSubjectRectificationRequest

    [Fact]
    public void DataSubjectRectificationRequest_DefaultConstruction_HasExpectedDefaults()
    {
        var request = new DataSubjectRectificationRequest();

        request.RequestId.Should().NotBeNullOrEmpty();
        request.Corrections.Should().BeEmpty();
        request.Justification.Should().Be(string.Empty);
        request.IdentityVerification.Should().NotBeNull();
    }

    [Fact]
    public void DataSubjectRectificationRequest_PropertyAssignment_Works()
    {
        var request = new DataSubjectRectificationRequest
        {
            DataSubjectId = "user-1",
            Justification = "Data is incorrect",
            Corrections = new List<DataCorrection>
            {
                new DataCorrection { FieldName = "Name", CorrectedValue = "New Name" }
            }
        };

        request.DataSubjectId.Should().Be("user-1");
        request.Justification.Should().Be("Data is incorrect");
        request.Corrections.Should().HaveCount(1);
    }

    #endregion

    #region IdentityVerification

    [Fact]
    public void IdentityVerification_DefaultConstruction_HasExpectedDefaults()
    {
        var verification = new IdentityVerification();

        verification.Method.Should().Be(VerificationMethod.Email);
        verification.Status.Should().Be(VerificationStatus.Pending);
        verification.VerificationTimestamp.Should().BeNull();
        verification.VerificationReference.Should().BeNull();
        verification.VerificationData.Should().BeEmpty();
    }

    [Fact]
    public void IdentityVerification_PropertyAssignment_Works()
    {
        var ts = DateTime.UtcNow;
        var verification = new IdentityVerification
        {
            Method = VerificationMethod.TwoFactor,
            Status = VerificationStatus.Verified,
            VerificationTimestamp = ts,
            VerificationReference = "ref-123"
        };

        verification.Method.Should().Be(VerificationMethod.TwoFactor);
        verification.Status.Should().Be(VerificationStatus.Verified);
        verification.VerificationTimestamp.Should().Be(ts);
        verification.VerificationReference.Should().Be("ref-123");
    }

    #endregion

    #region ProcessingRestrictionRequest

    [Fact]
    public void ProcessingRestrictionRequest_DefaultConstruction_HasExpectedDefaults()
    {
        var request = new ProcessingRestrictionRequest();

        request.RequestId.Should().NotBeNullOrEmpty();
        request.RestrictionGrounds.Should().BeEmpty();
        request.ProcessingActivitiesToRestrict.Should().BeEmpty();
        request.RestrictionPeriod.Should().BeNull();
    }

    [Fact]
    public void ProcessingRestrictionRequest_PropertyAssignment_Works()
    {
        var request = new ProcessingRestrictionRequest
        {
            DataSubjectId = "user-1",
            RestrictionGrounds = new List<RestrictionGround> { RestrictionGround.AccuracyContested },
            RestrictionPeriod = TimeSpan.FromDays(30)
        };

        request.DataSubjectId.Should().Be("user-1");
        request.RestrictionGrounds.Should().Contain(RestrictionGround.AccuracyContested);
        request.RestrictionPeriod.Should().Be(TimeSpan.FromDays(30));
    }

    #endregion

    #region PseudonymizationRequest

    [Fact]
    public void PseudonymizationRequest_DefaultConstruction_HasExpectedDefaults()
    {
        var request = new PseudonymizationRequest();

        request.Data.Should().NotBeNull();
        request.Technique.Should().Be(PseudonymizationTechnique.Tokenization);
        request.FieldsToPseudonymize.Should().BeEmpty();
        request.Reversible.Should().BeTrue();
        request.VaultId.Should().BeNull();
    }

    [Fact]
    public void PseudonymizationRequest_PropertyAssignment_Works()
    {
        var request = new PseudonymizationRequest
        {
            Data = new { Email = "test@test.com" },
            Technique = PseudonymizationTechnique.Encryption,
            Reversible = false,
            VaultId = "vault-1",
            FieldsToPseudonymize = new List<string> { "Email" }
        };

        request.Technique.Should().Be(PseudonymizationTechnique.Encryption);
        request.Reversible.Should().BeFalse();
        request.VaultId.Should().Be("vault-1");
        request.FieldsToPseudonymize.Should().Contain("Email");
    }

    #endregion

    #region Enumerations

    [Fact]
    public void Enumerations_HaveExpectedValues()
    {
        Enum.GetValues<ConsentStatus>().Should().Contain(ConsentStatus.Given);
        Enum.GetValues<BreachStatus>().Should().Contain(BreachStatus.Resolved);
        Enum.GetValues<BreachSeverity>().Should().Contain(BreachSeverity.Critical);
        Enum.GetValues<RiskLevel>().Should().Contain(RiskLevel.VeryHigh);
        Enum.GetValues<ErasureGround>().Should().Contain(ErasureGround.ConsentWithdrawn);
        Enum.GetValues<ErasureType>().Should().Contain(ErasureType.HardDelete);
        Enum.GetValues<RestrictionGround>().Should().Contain(RestrictionGround.ObjectionPending);
        Enum.GetValues<AnonymizationTechnique>().Should().Contain(AnonymizationTechnique.DifferentialPrivacy);
        Enum.GetValues<AnonymizationLevel>().Should().Contain(AnonymizationLevel.Maximum);
        Enum.GetValues<PseudonymizationTechnique>().Should().Contain(PseudonymizationTechnique.FormatPreservingEncryption);
        Enum.GetValues<DataExportFormat>().Should().Contain(DataExportFormat.PDF);
        Enum.GetValues<DataDisposalMethod>().Should().Contain(DataDisposalMethod.PhysicalDestruction);
        Enum.GetValues<VerificationMethod>().Should().Contain(VerificationMethod.DocumentUpload);
        Enum.GetValues<VerificationStatus>().Should().Contain(VerificationStatus.Expired);
    }

    #endregion
}
