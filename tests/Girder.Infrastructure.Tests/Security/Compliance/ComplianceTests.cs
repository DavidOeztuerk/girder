using Infrastructure.Security.Audit;
using Infrastructure.Security.Compliance;
using Infrastructure.Security.Encryption;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Infrastructure.Tests.Security.Compliance;

[Trait("Category", "Unit")]
public class ComplianceTests
{
    #region DTOs — DataProtectionOptions

    [Fact]
    public void DataProtectionOptions_DefaultValues_AreCorrect()
    {
        var options = new DataProtectionOptions();

        options.BypassIdentityVerification.Should().BeFalse();
        options.ExportDirectory.Should().Be("/tmp/exports");
        options.InlineDataThreshold.Should().Be(100);
        options.NotifyRecipientsOnRectification.Should().BeTrue();
        options.NotifyThirdPartiesOnErasure.Should().BeTrue();
        options.MaxConsentAge.Should().Be(TimeSpan.FromDays(730));
        options.DefaultRetentionPeriod.Should().Be(TimeSpan.FromDays(2555));
        options.EnableAutomatedAnonymization.Should().BeFalse();
        options.EnableAutomatedPseudonymization.Should().BeTrue();
        // Extended options (partial class)
        options.EnableAutomatedDataRetention.Should().BeFalse();
        options.DataRetentionCheckInterval.Should().Be(TimeSpan.FromHours(24));
        options.SupportedExportFormats.Should().BeEquivalentTo(
            new[] { DataExportFormat.JSON, DataExportFormat.CSV, DataExportFormat.PDF });
        options.DefaultAnonymizationTechnique.Should().Be(AnonymizationTechnique.KAnonymity);
        options.DefaultAnonymizationLevel.Should().Be(AnonymizationLevel.Medium);
        options.DefaultPseudonymizationTechnique.Should().Be(PseudonymizationTechnique.Tokenization);
        options.DefaultPseudonymizationReversible.Should().BeTrue();
        options.DataSources.Should().BeEmpty();
    }

    [Fact]
    public void DataProtectionOptions_PropertyAssignment_Works()
    {
        var options = new DataProtectionOptions
        {
            BypassIdentityVerification = true,
            ExportDirectory = "/custom/path",
            InlineDataThreshold = 200,
            MaxConsentAge = TimeSpan.FromDays(365),
            EnableAutomatedDataRetention = true,
            DataRetentionCheckInterval = TimeSpan.FromHours(6)
        };

        options.BypassIdentityVerification.Should().BeTrue();
        options.ExportDirectory.Should().Be("/custom/path");
        options.InlineDataThreshold.Should().Be(200);
        options.MaxConsentAge.Should().Be(TimeSpan.FromDays(365));
        options.EnableAutomatedDataRetention.Should().BeTrue();
        options.DataRetentionCheckInterval.Should().Be(TimeSpan.FromHours(6));
    }

    [Fact]
    public void DataProtectionOptions_DataSources_CanBePopulated()
    {
        var options = new DataProtectionOptions();
        options.DataSources.Add("users", typeof(string));
        options.DataSources.Add("skills", typeof(int));

        options.DataSources.Should().HaveCount(2);
        options.DataSources["users"].Should().Be(typeof(string));
    }

    #endregion

    #region DTOs — ConsentManagementOptions

    [Fact]
    public void ConsentManagementOptions_DefaultValues_AreCorrect()
    {
        var options = new ConsentManagementOptions();

        options.RequireExplicitConsent.Should().BeTrue();
        options.EnableConsentMonitoring.Should().BeTrue();
        options.AutoWithdrawExpiredConsent.Should().BeFalse();
        options.ConsentExpirationWarningDays.Should().Be(30);
        options.MaxConsentAge.Should().Be(TimeSpan.FromDays(730));
        options.VerificationMethod.Should().Be(ConsentVerificationMethod.DoubleOptIn);
    }

    [Fact]
    public void ConsentManagementOptions_PropertyAssignment_Works()
    {
        var options = new ConsentManagementOptions
        {
            RequireExplicitConsent = false,
            EnableConsentMonitoring = false,
            AutoWithdrawExpiredConsent = true,
            ConsentExpirationWarningDays = 7,
            VerificationMethod = ConsentVerificationMethod.ExplicitAction
        };

        options.RequireExplicitConsent.Should().BeFalse();
        options.EnableConsentMonitoring.Should().BeFalse();
        options.AutoWithdrawExpiredConsent.Should().BeTrue();
        options.ConsentExpirationWarningDays.Should().Be(7);
        options.VerificationMethod.Should().Be(ConsentVerificationMethod.ExplicitAction);
    }

    #endregion

    #region DTOs — DataBreachOptions

    [Fact]
    public void DataBreachOptions_DefaultValues_AreCorrect()
    {
        var options = new DataBreachOptions();

        options.AutoNotifyAuthority.Should().BeFalse();
        options.RequireManualApproval.Should().BeTrue();
        options.NotificationDelayMinutes.Should().Be(30);
        options.SupervisoryAuthorityDeadlineHours.Should().Be(72);
        options.DataSubjectNotificationThreshold.Should().Be(10);
        options.AutoNotificationSeverityThreshold.Should().Be(BreachSeverity.High);
    }

    [Fact]
    public void DataBreachOptions_PropertyAssignment_Works()
    {
        var options = new DataBreachOptions
        {
            AutoNotifyAuthority = true,
            RequireManualApproval = false,
            NotificationDelayMinutes = 5,
            SupervisoryAuthorityDeadlineHours = 48,
            DataSubjectNotificationThreshold = 50,
            AutoNotificationSeverityThreshold = BreachSeverity.Critical
        };

        options.AutoNotifyAuthority.Should().BeTrue();
        options.RequireManualApproval.Should().BeFalse();
        options.NotificationDelayMinutes.Should().Be(5);
        options.SupervisoryAuthorityDeadlineHours.Should().Be(48);
        options.DataSubjectNotificationThreshold.Should().Be(50);
        options.AutoNotificationSeverityThreshold.Should().Be(BreachSeverity.Critical);
    }

    #endregion

    #region DTOs — ComplianceReportRequest

    [Fact]
    public void ComplianceReportRequest_DefaultValues_AreCorrect()
    {
        var request = new ComplianceReportRequest();

        request.ReportType.Should().Be(ComplianceReportType.DataSubjectRights);
        request.Period.Should().NotBeNull();
        request.IncludeRecommendations.Should().BeTrue();
        request.Filters.Should().BeEmpty();
    }

    [Fact]
    public void ComplianceReportRequest_PropertyAssignment_Works()
    {
        var request = new ComplianceReportRequest
        {
            ReportType = ComplianceReportType.DataBreaches,
            IncludeRecommendations = false,
            Filters = new List<string> { "severity:high" }
        };

        request.ReportType.Should().Be(ComplianceReportType.DataBreaches);
        request.IncludeRecommendations.Should().BeFalse();
        request.Filters.Should().Contain("severity:high");
    }

    #endregion

    #region DTOs — ConsentWithdrawalRequest

    [Fact]
    public void ConsentWithdrawalRequest_DefaultValues_AreCorrect()
    {
        var request = new ConsentWithdrawalRequest();

        request.ConsentId.Should().Be(string.Empty);
        request.DataSubjectId.Should().Be(string.Empty);
        request.WithdrawalReason.Should().Be(string.Empty);
        request.ProcessErasure.Should().BeFalse();
        request.WithdrawalTimestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ConsentWithdrawalRequest_PropertyAssignment_Works()
    {
        var ts = DateTime.UtcNow.AddHours(-1);
        var request = new ConsentWithdrawalRequest
        {
            ConsentId = "consent-1",
            DataSubjectId = "user-1",
            WithdrawalReason = "No longer needed",
            WithdrawalTimestamp = ts,
            ProcessErasure = true
        };

        request.ConsentId.Should().Be("consent-1");
        request.DataSubjectId.Should().Be("user-1");
        request.WithdrawalReason.Should().Be("No longer needed");
        request.WithdrawalTimestamp.Should().Be(ts);
        request.ProcessErasure.Should().BeTrue();
    }

    #endregion

    #region DTOs — DataRetentionRequest

    [Fact]
    public void DataRetentionRequest_DefaultValues_AreCorrect()
    {
        var request = new DataRetentionRequest();

        request.RequestId.Should().Be(string.Empty);
        request.AutomatedExecution.Should().BeFalse();
        request.DataTypes.Should().BeEmpty();
        request.ProcessingPurposes.Should().BeEmpty();
        request.CheckDate.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void DataRetentionRequest_PropertyAssignment_Works()
    {
        var request = new DataRetentionRequest
        {
            RequestId = "req-1",
            AutomatedExecution = true,
            DataTypes = new List<string> { "profile", "activity" },
            ProcessingPurposes = new List<string> { "marketing" }
        };

        request.RequestId.Should().Be("req-1");
        request.AutomatedExecution.Should().BeTrue();
        request.DataTypes.Should().HaveCount(2);
        request.ProcessingPurposes.Should().Contain("marketing");
    }

    #endregion

    #region DTOs — DataTransfer

    [Fact]
    public void DataTransfer_DefaultValues_AreCorrect()
    {
        var transfer = new DataTransfer();

        transfer.DestinationCountry.Should().Be(string.Empty);
        transfer.TransferMechanism.Should().Be(string.Empty);
        transfer.LegalBasis.Should().Be(string.Empty);
        transfer.RecipientName.Should().Be(string.Empty);
    }

    [Fact]
    public void DataTransfer_PropertyAssignment_Works()
    {
        var transfer = new DataTransfer
        {
            DestinationCountry = "US",
            TransferMechanism = "Standard Contractual Clauses",
            LegalBasis = "Adequacy decision",
            RecipientName = "Acme Corp"
        };

        transfer.DestinationCountry.Should().Be("US");
        transfer.TransferMechanism.Should().Be("Standard Contractual Clauses");
        transfer.LegalBasis.Should().Be("Adequacy decision");
        transfer.RecipientName.Should().Be("Acme Corp");
    }

    #endregion

    #region DTOs — ReportPeriod

    [Fact]
    public void ReportPeriod_DefaultValues_AreReasonable()
    {
        var period = new ReportPeriod();

        period.StartDate.Should().BeCloseTo(DateTime.UtcNow.Date.AddDays(-30), TimeSpan.FromSeconds(5));
        period.EndDate.Should().BeCloseTo(DateTime.UtcNow.Date, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ReportPeriod_PropertyAssignment_Works()
    {
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        var period = new ReportPeriod { StartDate = start, EndDate = end };

        period.StartDate.Should().Be(start);
        period.EndDate.Should().Be(end);
    }

    #endregion

    #region DTOs — ConsentValidity

    [Fact]
    public void ConsentValidity_DefaultValues_AreCorrect()
    {
        var validity = new ConsentValidity();

        validity.IsValid.Should().BeTrue();
        validity.ValidationIssues.Should().BeEmpty();
    }

    [Fact]
    public void ConsentValidity_CanAddValidationIssues()
    {
        var validity = new ConsentValidity { IsValid = false };
        validity.ValidationIssues.Add("Missing explicit consent");

        validity.IsValid.Should().BeFalse();
        validity.ValidationIssues.Should().Contain("Missing explicit consent");
    }

    #endregion

    #region DTOs — ConsentVerificationMethod Enum

    [Fact]
    public void ConsentVerificationMethod_HasExpectedValues()
    {
        Enum.GetValues<ConsentVerificationMethod>().Should().HaveCount(4);
        Enum.GetValues<ConsentVerificationMethod>().Should().Contain(ConsentVerificationMethod.SingleOptIn);
        Enum.GetValues<ConsentVerificationMethod>().Should().Contain(ConsentVerificationMethod.DoubleOptIn);
        Enum.GetValues<ConsentVerificationMethod>().Should().Contain(ConsentVerificationMethod.ExplicitAction);
        Enum.GetValues<ConsentVerificationMethod>().Should().Contain(ConsentVerificationMethod.DocumentedConsent);
    }

    #endregion

    #region DTOs — ComplianceReportType Enum

    [Fact]
    public void ComplianceReportType_HasExpectedValues()
    {
        Enum.GetValues<ComplianceReportType>().Should().HaveCount(5);
        Enum.GetValues<ComplianceReportType>().Should().Contain(ComplianceReportType.DataSubjectRights);
        Enum.GetValues<ComplianceReportType>().Should().Contain(ComplianceReportType.DataBreaches);
        Enum.GetValues<ComplianceReportType>().Should().Contain(ComplianceReportType.ConsentManagement);
        Enum.GetValues<ComplianceReportType>().Should().Contain(ComplianceReportType.DataRetention);
        Enum.GetValues<ComplianceReportType>().Should().Contain(ComplianceReportType.OverallCompliance);
    }

    #endregion

    #region ComplianceExtensions — AddDataProtectionCompliance(IConfiguration)

    [Fact]
    public void AddDataProtectionCompliance_WithConfiguration_RegistersAllExpectedServices()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        // Add required dependencies that DataProtectionService needs
        AddRequiredDependencies(services);

        services.AddDataProtectionCompliance(configuration);

        // Verify service registrations
        services.Should().Contain(sd => sd.ServiceType == typeof(DataProtectionService));
        services.Should().Contain(sd => sd.ServiceType == typeof(IDataProtectionService));
        services.Should().Contain(sd => sd.ServiceType == typeof(IConsentManagementService));
        services.Should().Contain(sd => sd.ServiceType == typeof(IDataBreachNotificationService));

        // Verify background services are registered
        services.Should().Contain(sd =>
            sd.ServiceType == typeof(IHostedService) &&
            sd.ImplementationType == typeof(DataRetentionBackgroundService));
        services.Should().Contain(sd =>
            sd.ServiceType == typeof(IHostedService) &&
            sd.ImplementationType == typeof(ConsentMaintenanceBackgroundService));
        services.Should().Contain(sd =>
            sd.ServiceType == typeof(IHostedService) &&
            sd.ImplementationType == typeof(ComplianceMonitoringBackgroundService));
    }

    [Fact]
    public void AddDataProtectionCompliance_WithConfiguration_ConfiguresOptions()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataProtection:ExportDirectory"] = "/custom/exports",
                ["ConsentManagement:RequireExplicitConsent"] = "false",
                ["DataBreach:AutoNotifyAuthority"] = "true"
            })
            .Build();

        AddRequiredDependencies(services);
        services.AddDataProtectionCompliance(configuration);

        var sp = services.BuildServiceProvider();

        var dpOptions = sp.GetRequiredService<IOptions<DataProtectionOptions>>().Value;
        dpOptions.ExportDirectory.Should().Be("/custom/exports");

        var consentOptions = sp.GetRequiredService<IOptions<ConsentManagementOptions>>().Value;
        consentOptions.RequireExplicitConsent.Should().BeFalse();

        var breachOptions = sp.GetRequiredService<IOptions<DataBreachOptions>>().Value;
        breachOptions.AutoNotifyAuthority.Should().BeTrue();
    }

    [Fact]
    public void AddDataProtectionCompliance_WithConfiguration_ReturnsServiceCollection()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        AddRequiredDependencies(services);

        var result = services.AddDataProtectionCompliance(configuration);

        result.Should().BeSameAs(services);
    }

    #endregion

    #region ComplianceExtensions — AddDataProtectionCompliance(Action overloads)

    [Fact]
    public void AddDataProtectionCompliance_WithActionOverload_RegistersServices()
    {
        var services = new ServiceCollection();
        AddRequiredDependencies(services);

        services.AddDataProtectionCompliance(
            dp => dp.ExportDirectory = "/action/exports",
            consent => consent.RequireExplicitConsent = false,
            breach => breach.AutoNotifyAuthority = true);

        services.Should().Contain(sd => sd.ServiceType == typeof(DataProtectionService));
        services.Should().Contain(sd => sd.ServiceType == typeof(IDataProtectionService));
        services.Should().Contain(sd => sd.ServiceType == typeof(IConsentManagementService));
        services.Should().Contain(sd => sd.ServiceType == typeof(IDataBreachNotificationService));

        var sp = services.BuildServiceProvider();

        var dpOptions = sp.GetRequiredService<IOptions<DataProtectionOptions>>().Value;
        dpOptions.ExportDirectory.Should().Be("/action/exports");

        var consentOptions = sp.GetRequiredService<IOptions<ConsentManagementOptions>>().Value;
        consentOptions.RequireExplicitConsent.Should().BeFalse();

        var breachOptions = sp.GetRequiredService<IOptions<DataBreachOptions>>().Value;
        breachOptions.AutoNotifyAuthority.Should().BeTrue();
    }

    [Fact]
    public void AddDataProtectionCompliance_WithActionOverload_NullOptionalActions_DoesNotThrow()
    {
        var services = new ServiceCollection();
        AddRequiredDependencies(services);

        var act = () => services.AddDataProtectionCompliance(
            dp => dp.ExportDirectory = "/test",
            configureConsent: null,
            configureBreach: null);

        act.Should().NotThrow();
    }

    #endregion

    #region ComplianceExtensions — AddDataProtectionCompliance(Action<IComplianceBuilder>)

    [Fact]
    public void AddDataProtectionCompliance_WithBuilderAction_RegistersServices()
    {
        var services = new ServiceCollection();
        AddRequiredDependencies(services);

        services.AddDataProtectionCompliance(builder =>
        {
            builder.ConfigureDataProtection(dp => dp.ExportDirectory = "/builder/exports");
        });

        services.Should().Contain(sd => sd.ServiceType == typeof(DataProtectionService));
        services.Should().Contain(sd => sd.ServiceType == typeof(IDataProtectionService));
    }

    #endregion

    #region ComplianceBuilder — Fluent Methods

    [Fact]
    public void ComplianceBuilder_ConfigureDataProtection_SetsOptionsAndReturnsSelf()
    {
        var services = new ServiceCollection();
        AddRequiredDependencies(services);
        var builder = new ComplianceBuilder(services);

        var result = builder.ConfigureDataProtection(dp =>
        {
            dp.ExportDirectory = "/custom";
            dp.InlineDataThreshold = 500;
        });

        result.Should().BeSameAs(builder);

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<DataProtectionOptions>>().Value;
        options.ExportDirectory.Should().Be("/custom");
        options.InlineDataThreshold.Should().Be(500);
    }

    [Fact]
    public void ComplianceBuilder_ConfigureConsentManagement_SetsOptionsAndReturnsSelf()
    {
        var services = new ServiceCollection();
        AddRequiredDependencies(services);
        var builder = new ComplianceBuilder(services);

        var result = builder.ConfigureConsentManagement(c =>
        {
            c.RequireExplicitConsent = false;
            c.ConsentExpirationWarningDays = 14;
        });

        result.Should().BeSameAs(builder);

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<ConsentManagementOptions>>().Value;
        options.RequireExplicitConsent.Should().BeFalse();
        options.ConsentExpirationWarningDays.Should().Be(14);
    }

    [Fact]
    public void ComplianceBuilder_ConfigureDataBreachNotification_SetsOptionsAndReturnsSelf()
    {
        var services = new ServiceCollection();
        AddRequiredDependencies(services);
        var builder = new ComplianceBuilder(services);

        var result = builder.ConfigureDataBreachNotification(b =>
        {
            b.AutoNotifyAuthority = true;
            b.NotificationDelayMinutes = 10;
        });

        result.Should().BeSameAs(builder);

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<DataBreachOptions>>().Value;
        options.AutoNotifyAuthority.Should().BeTrue();
        options.NotificationDelayMinutes.Should().Be(10);
    }

    [Fact]
    public void ComplianceBuilder_EnableAutomatedDataRetention_SetsFlag()
    {
        var services = new ServiceCollection();
        AddRequiredDependencies(services);
        var builder = new ComplianceBuilder(services);

        var result = builder.EnableAutomatedDataRetention(TimeSpan.FromHours(12));

        result.Should().BeSameAs(builder);

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<DataProtectionOptions>>().Value;
        options.EnableAutomatedDataRetention.Should().BeTrue();
        options.DataRetentionCheckInterval.Should().Be(TimeSpan.FromHours(12));
    }

    [Fact]
    public void ComplianceBuilder_EnableConsentMonitoring_SetsFlag()
    {
        var services = new ServiceCollection();
        AddRequiredDependencies(services);
        var builder = new ComplianceBuilder(services);

        builder.EnableConsentMonitoring(true);

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<ConsentManagementOptions>>().Value;
        options.EnableConsentMonitoring.Should().BeTrue();
    }

    [Fact]
    public void ComplianceBuilder_EnableConsentMonitoring_CanDisable()
    {
        var services = new ServiceCollection();
        AddRequiredDependencies(services);
        var builder = new ComplianceBuilder(services);

        builder.EnableConsentMonitoring(false);

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<ConsentManagementOptions>>().Value;
        options.EnableConsentMonitoring.Should().BeFalse();
    }

    [Fact]
    public void ComplianceBuilder_ConfigureDataExport_SetsOptions()
    {
        var services = new ServiceCollection();
        AddRequiredDependencies(services);
        var builder = new ComplianceBuilder(services);

        var formats = new List<DataExportFormat> { DataExportFormat.JSON, DataExportFormat.XML };
        var result = builder.ConfigureDataExport("/exports", formats);

        result.Should().BeSameAs(builder);

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<DataProtectionOptions>>().Value;
        options.ExportDirectory.Should().Be("/exports");
        options.SupportedExportFormats.Should().BeEquivalentTo(formats);
    }

    [Fact]
    public void ComplianceBuilder_ConfigureAnonymization_SetsOptionsAndEnablesFeature()
    {
        var services = new ServiceCollection();
        AddRequiredDependencies(services);
        var builder = new ComplianceBuilder(services);

        var result = builder.ConfigureAnonymization(
            AnonymizationTechnique.DifferentialPrivacy,
            AnonymizationLevel.High);

        result.Should().BeSameAs(builder);

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<DataProtectionOptions>>().Value;
        options.DefaultAnonymizationTechnique.Should().Be(AnonymizationTechnique.DifferentialPrivacy);
        options.DefaultAnonymizationLevel.Should().Be(AnonymizationLevel.High);
        options.EnableAutomatedAnonymization.Should().BeTrue();
    }

    [Fact]
    public void ComplianceBuilder_ConfigurePseudonymization_SetsOptionsAndEnablesFeature()
    {
        var services = new ServiceCollection();
        AddRequiredDependencies(services);
        var builder = new ComplianceBuilder(services);

        var result = builder.ConfigurePseudonymization(
            PseudonymizationTechnique.Encryption,
            false);

        result.Should().BeSameAs(builder);

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<DataProtectionOptions>>().Value;
        options.DefaultPseudonymizationTechnique.Should().Be(PseudonymizationTechnique.Encryption);
        options.DefaultPseudonymizationReversible.Should().BeFalse();
        options.EnableAutomatedPseudonymization.Should().BeTrue();
    }

    [Fact]
    public void ComplianceBuilder_AddDataSource_AddsToOptions()
    {
        var services = new ServiceCollection();
        AddRequiredDependencies(services);
        var builder = new ComplianceBuilder(services);

        var result = builder.AddDataSource("users", typeof(string));

        result.Should().BeSameAs(builder);

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<DataProtectionOptions>>().Value;
        options.DataSources.Should().ContainKey("users");
        options.DataSources["users"].Should().Be(typeof(string));
    }

    [Fact]
    public void ComplianceBuilder_ForDevelopment_SetsDevelopmentDefaults()
    {
        var services = new ServiceCollection();
        AddRequiredDependencies(services);
        var builder = new ComplianceBuilder(services);

        var result = builder.ForDevelopment();

        result.Should().BeSameAs(builder);

        var sp = services.BuildServiceProvider();

        var dpOptions = sp.GetRequiredService<IOptions<DataProtectionOptions>>().Value;
        dpOptions.BypassIdentityVerification.Should().BeTrue();
        dpOptions.NotifyRecipientsOnRectification.Should().BeFalse();
        dpOptions.NotifyThirdPartiesOnErasure.Should().BeFalse();
        dpOptions.ExportDirectory.Should().Be("./exports");
        dpOptions.InlineDataThreshold.Should().Be(1000);
        dpOptions.DefaultRetentionPeriod.Should().Be(TimeSpan.FromDays(30));

        var consentOptions = sp.GetRequiredService<IOptions<ConsentManagementOptions>>().Value;
        consentOptions.RequireExplicitConsent.Should().BeFalse();
        consentOptions.ConsentExpirationWarningDays.Should().Be(7);
        consentOptions.EnableConsentMonitoring.Should().BeFalse();

        var breachOptions = sp.GetRequiredService<IOptions<DataBreachOptions>>().Value;
        breachOptions.AutoNotifyAuthority.Should().BeFalse();
        breachOptions.RequireManualApproval.Should().BeTrue();
        breachOptions.NotificationDelayMinutes.Should().Be(60);
    }

    [Fact]
    public void ComplianceBuilder_ForProduction_SetsProductionDefaults()
    {
        var services = new ServiceCollection();
        AddRequiredDependencies(services);
        var builder = new ComplianceBuilder(services);

        var result = builder.ForProduction();

        result.Should().BeSameAs(builder);

        var sp = services.BuildServiceProvider();

        var dpOptions = sp.GetRequiredService<IOptions<DataProtectionOptions>>().Value;
        dpOptions.BypassIdentityVerification.Should().BeFalse();
        dpOptions.NotifyRecipientsOnRectification.Should().BeTrue();
        dpOptions.NotifyThirdPartiesOnErasure.Should().BeTrue();
        dpOptions.ExportDirectory.Should().Be("/secure/exports");
        dpOptions.InlineDataThreshold.Should().Be(50);
        dpOptions.DefaultRetentionPeriod.Should().Be(TimeSpan.FromDays(2555));
        dpOptions.EnableAutomatedDataRetention.Should().BeTrue();
        dpOptions.DataRetentionCheckInterval.Should().Be(TimeSpan.FromHours(6));

        var consentOptions = sp.GetRequiredService<IOptions<ConsentManagementOptions>>().Value;
        consentOptions.RequireExplicitConsent.Should().BeTrue();
        consentOptions.ConsentExpirationWarningDays.Should().Be(30);
        consentOptions.EnableConsentMonitoring.Should().BeTrue();
        consentOptions.AutoWithdrawExpiredConsent.Should().BeTrue();

        var breachOptions = sp.GetRequiredService<IOptions<DataBreachOptions>>().Value;
        breachOptions.AutoNotifyAuthority.Should().BeTrue();
        breachOptions.RequireManualApproval.Should().BeFalse();
        breachOptions.NotificationDelayMinutes.Should().Be(5);
        breachOptions.SupervisoryAuthorityDeadlineHours.Should().Be(72);
    }

    [Fact]
    public void ComplianceBuilder_FluentChaining_WorksAcrossMultipleMethods()
    {
        var services = new ServiceCollection();
        AddRequiredDependencies(services);
        var builder = new ComplianceBuilder(services);

        var result = builder
            .ConfigureDataProtection(dp => dp.ExportDirectory = "/chained")
            .ConfigureConsentManagement(c => c.RequireExplicitConsent = false)
            .ConfigureDataBreachNotification(b => b.AutoNotifyAuthority = true)
            .EnableAutomatedDataRetention(TimeSpan.FromHours(1))
            .EnableConsentMonitoring(true);

        result.Should().BeSameAs(builder);
    }

    #endregion

    #region DataProtectionService — Constructor + CheckConsentStatusAsync

    [Fact]
    public async Task CheckConsentStatusAsync_WhenNoConsentStored_ReturnsInvalid()
    {
        var (service, database, _, _) = CreateDataProtectionService();

        database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        var result = await service.CheckConsentStatusAsync("user-1", "marketing");

        result.Should().Be(ConsentStatus.Invalid);
    }

    [Fact]
    public async Task CheckConsentStatusAsync_WhenDecryptionFails_ReturnsInvalid()
    {
        var (service, database, encryptionService, _) = CreateDataProtectionService();

        database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns((RedisValue)"encrypted-data");

        encryptionService.DecryptAsync(Arg.Any<string>(), Arg.Any<EncryptionContext>(), Arg.Any<CancellationToken>())
            .Returns(new DecryptionResult { Success = false, Data = "encrypted-data" });

        // When decryption fails, it falls back to parsing plaintext.
        // "encrypted-data" is not valid JSON, so deserialization returns null -> Invalid
        var result = await service.CheckConsentStatusAsync("user-1", "marketing");

        result.Should().Be(ConsentStatus.Invalid);
    }

    [Fact]
    public async Task CheckConsentStatusAsync_WhenConsentWithdrawn_ReturnsWithdrawn()
    {
        var (service, database, encryptionService, _) = CreateDataProtectionService();

        var consent = new ConsentRecord
        {
            DataSubjectId = "user-1",
            ProcessingPurpose = "marketing",
            Status = ConsentStatus.Withdrawn
        };
        var json = JsonSerializer.Serialize(consent);

        database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns((RedisValue)"encrypted");

        encryptionService.DecryptAsync(Arg.Any<string>(), Arg.Any<EncryptionContext>(), Arg.Any<CancellationToken>())
            .Returns(new DecryptionResult { Success = true, Data = json });

        var result = await service.CheckConsentStatusAsync("user-1", "marketing");

        result.Should().Be(ConsentStatus.Withdrawn);
    }

    [Fact]
    public async Task CheckConsentStatusAsync_WhenConsentGivenAndNotExpired_ReturnsGiven()
    {
        var (service, database, encryptionService, _) = CreateDataProtectionService();

        var consent = new ConsentRecord
        {
            DataSubjectId = "user-1",
            ProcessingPurpose = "marketing",
            Status = ConsentStatus.Given,
            ConsentTimestamp = DateTime.UtcNow.AddDays(-1)
        };
        var json = JsonSerializer.Serialize(consent);

        database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns((RedisValue)"encrypted");

        encryptionService.DecryptAsync(Arg.Any<string>(), Arg.Any<EncryptionContext>(), Arg.Any<CancellationToken>())
            .Returns(new DecryptionResult { Success = true, Data = json });

        var result = await service.CheckConsentStatusAsync("user-1", "marketing");

        result.Should().Be(ConsentStatus.Given);
    }

    [Fact]
    public async Task CheckConsentStatusAsync_WhenConsentExpired_ReturnsExpired()
    {
        var (service, database, encryptionService, _) = CreateDataProtectionService(
            configureOptions: o => o.MaxConsentAge = TimeSpan.FromDays(1));

        var consent = new ConsentRecord
        {
            DataSubjectId = "user-1",
            ProcessingPurpose = "marketing",
            Status = ConsentStatus.Given,
            ConsentTimestamp = DateTime.UtcNow.AddDays(-5)
        };
        var json = JsonSerializer.Serialize(consent);

        database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns((RedisValue)"encrypted");

        encryptionService.DecryptAsync(Arg.Any<string>(), Arg.Any<EncryptionContext>(), Arg.Any<CancellationToken>())
            .Returns(new DecryptionResult { Success = true, Data = json });

        var result = await service.CheckConsentStatusAsync("user-1", "marketing");

        result.Should().Be(ConsentStatus.Expired);
    }

    [Fact]
    public async Task CheckConsentStatusAsync_WhenExceptionThrown_ReturnsInvalid()
    {
        var (service, database, _, _) = CreateDataProtectionService();

        database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("connection failed"));

        var result = await service.CheckConsentStatusAsync("user-1", "marketing");

        result.Should().Be(ConsentStatus.Invalid);
    }

    #endregion

    #region DataProtectionService — GetDataRetentionPolicyAsync

    [Fact]
    public async Task GetDataRetentionPolicyAsync_WhenNoPolicyStored_ReturnsDefaultPolicy()
    {
        var (service, database, _, _) = CreateDataProtectionService();

        database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        var result = await service.GetDataRetentionPolicyAsync("UserProfile", "account-management");

        result.Should().NotBeNull();
        result.DataType.Should().Be("UserProfile");
        result.ProcessingPurpose.Should().Be("account-management");
        result.LegalBasis.Should().Be("Legitimate interest");
        result.DisposalMethod.Should().Be(DataDisposalMethod.SecureDelete);
        result.RetentionPeriod.Should().Be(TimeSpan.FromDays(2555));
    }

    [Fact]
    public async Task GetDataRetentionPolicyAsync_WhenPolicyStored_ReturnsStoredPolicy()
    {
        var (service, database, _, _) = CreateDataProtectionService();

        var storedPolicy = new DataRetentionPolicy
        {
            DataType = "UserProfile",
            ProcessingPurpose = "marketing",
            RetentionPeriod = TimeSpan.FromDays(365),
            LegalBasis = "Consent"
        };
        var json = JsonSerializer.Serialize(storedPolicy);

        database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns((RedisValue)json);

        var result = await service.GetDataRetentionPolicyAsync("UserProfile", "marketing");

        result.LegalBasis.Should().Be("Consent");
        result.RetentionPeriod.Should().Be(TimeSpan.FromDays(365));
    }

    [Fact]
    public async Task GetDataRetentionPolicyAsync_WhenExceptionThrown_ReturnsDefaultPolicy()
    {
        var (service, database, _, _) = CreateDataProtectionService();

        database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("connection failed"));

        var result = await service.GetDataRetentionPolicyAsync("UserProfile", "account");

        result.Should().NotBeNull();
        result.DataType.Should().Be("UserProfile");
        result.LegalBasis.Should().Be("Legitimate interest");
    }

    #endregion

    #region DataProtectionService — RecordConsentAsync

    [Fact]
    public async Task RecordConsentAsync_WithValidConsent_ReturnsConsentId()
    {
        var (service, database, encryptionService, auditService) = CreateDataProtectionService();

        SetupEncryptionSuccess(encryptionService);
        SetupAuditSuccess(auditService);

        var consent = CreateValidConsent();

        var result = await service.RecordConsentAsync(consent);

        result.Should().Be(consent.Id);
        await database.Received(1).StringSetAsync(
            Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(), Arg.Any<bool>(), Arg.Any<When>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task RecordConsentAsync_WithInvalidConsent_ReturnsEmpty()
    {
        var (service, _, _, _) = CreateDataProtectionService();

        var consent = new ConsentRecord
        {
            FreelyGiven = false,  // Invalid: not freely given
            Specific = true,
            Informed = true,
            Unambiguous = true
        };

        var result = await service.RecordConsentAsync(consent);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordConsentAsync_WhenEncryptionFails_ReturnsEmpty()
    {
        var (service, _, encryptionService, _) = CreateDataProtectionService();

        encryptionService.EncryptAsync(Arg.Any<string>(), Arg.Any<EncryptionContext>(), Arg.Any<CancellationToken>())
            .Returns(new EncryptionResult { Success = false });

        var consent = CreateValidConsent();

        var result = await service.RecordConsentAsync(consent);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordConsentAsync_WhenExceptionThrown_ReturnsEmpty()
    {
        var (service, database, encryptionService, _) = CreateDataProtectionService();

        SetupEncryptionSuccess(encryptionService);

        database.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(), Arg.Any<bool>(), Arg.Any<When>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("write failed"));

        var consent = CreateValidConsent();

        var result = await service.RecordConsentAsync(consent);

        result.Should().BeEmpty();
    }

    #endregion

    #region DataProtectionService — GetConsentHistoryAsync

    [Fact]
    public async Task GetConsentHistoryAsync_WhenNoHistory_ReturnsEmptyList()
    {
        var (service, database, _, _) = CreateDataProtectionService();

        database.ListRangeAsync(Arg.Any<RedisKey>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CommandFlags>())
            .Returns(Array.Empty<RedisValue>());

        var result = await service.GetConsentHistoryAsync("user-1");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetConsentHistoryAsync_WhenExceptionThrown_ReturnsEmptyList()
    {
        var (service, database, _, _) = CreateDataProtectionService();

        database.ListRangeAsync(Arg.Any<RedisKey>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("read failed"));

        var result = await service.GetConsentHistoryAsync("user-1");

        result.Should().BeEmpty();
    }

    #endregion

    #region DataProtectionService — IsProcessingConsentedAsync

    [Fact]
    public async Task IsProcessingConsentedAsync_WhenConsentGiven_ReturnsTrue()
    {
        var (service, database, encryptionService, _) = CreateDataProtectionService();

        var consent = new ConsentRecord
        {
            Status = ConsentStatus.Given,
            ConsentTimestamp = DateTime.UtcNow.AddDays(-1)
        };
        var json = JsonSerializer.Serialize(consent);

        database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns((RedisValue)"encrypted");

        encryptionService.DecryptAsync(Arg.Any<string>(), Arg.Any<EncryptionContext>(), Arg.Any<CancellationToken>())
            .Returns(new DecryptionResult { Success = true, Data = json });

        var result = await service.IsProcessingConsentedAsync("user-1", "marketing");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsProcessingConsentedAsync_WhenConsentNotGiven_ReturnsFalse()
    {
        var (service, database, _, _) = CreateDataProtectionService();

        database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        var result = await service.IsProcessingConsentedAsync("user-1", "marketing");

        result.Should().BeFalse();
    }

    #endregion

    #region DataProtectionService — GetActiveConsentsAsync

    [Fact]
    public async Task GetActiveConsentsAsync_WhenExceptionThrown_ReturnsEmptyList()
    {
        var (service, database, _, _) = CreateDataProtectionService();

        database.ListRangeAsync(Arg.Any<RedisKey>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("read failed"));

        var result = await service.GetActiveConsentsAsync("user-1");

        result.Should().BeEmpty();
    }

    #endregion

    #region DataProtectionService — ReportDataBreachAsync

    [Fact]
    public async Task ReportDataBreachAsync_WithValidBreach_ReturnsBreachId()
    {
        var (service, database, encryptionService, auditService) = CreateDataProtectionService();

        SetupEncryptionSuccess(encryptionService);
        SetupAuditSuccess(auditService);

        var breach = new DataBreach
        {
            Description = "Unauthorized access detected",
            BreachType = BreachType.UnauthorizedAccess,
            Severity = BreachSeverity.High,
            AffectedDataSubjectsCount = 50
        };

        var result = await service.ReportDataBreachAsync(breach);

        result.Should().Be(breach.Id);
        await database.Received(1).StringSetAsync(
            Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(), Arg.Any<bool>(), Arg.Any<When>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task ReportDataBreachAsync_WhenEncryptionFails_ReturnsEmpty()
    {
        var (service, _, encryptionService, _) = CreateDataProtectionService();

        encryptionService.EncryptAsync(Arg.Any<string>(), Arg.Any<EncryptionContext>(), Arg.Any<CancellationToken>())
            .Returns(new EncryptionResult { Success = false });

        var breach = new DataBreach { Description = "test breach" };

        var result = await service.ReportDataBreachAsync(breach);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ReportDataBreachAsync_WhenExceptionThrown_ReturnsEmpty()
    {
        var (service, database, encryptionService, _) = CreateDataProtectionService();

        SetupEncryptionSuccess(encryptionService);

        database.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(), Arg.Any<bool>(), Arg.Any<When>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("write failed"));

        var breach = new DataBreach { Description = "test breach" };

        var result = await service.ReportDataBreachAsync(breach);

        result.Should().BeEmpty();
    }

    #endregion

    #region DataProtectionService — UpdateBreachStatusAsync

    [Fact]
    public async Task UpdateBreachStatusAsync_WhenBreachNotFound_DoesNotThrow()
    {
        var (service, database, _, _) = CreateDataProtectionService();

        database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        var act = () => service.UpdateBreachStatusAsync("nonexistent", BreachStatus.Resolved, "Fixed");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task UpdateBreachStatusAsync_WhenBreachExists_UpdatesStatus()
    {
        var (service, database, encryptionService, auditService) = CreateDataProtectionService();

        var breach = new DataBreach
        {
            Id = "breach-1",
            Status = BreachStatus.Discovered,
            Description = "test"
        };
        var json = JsonSerializer.Serialize(breach);

        database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns((RedisValue)"encrypted");

        encryptionService.DecryptAsync(Arg.Any<string>(), Arg.Any<EncryptionContext>(), Arg.Any<CancellationToken>())
            .Returns(new DecryptionResult { Success = true, Data = json });

        SetupEncryptionSuccess(encryptionService);
        SetupAuditSuccess(auditService);

        await service.UpdateBreachStatusAsync("breach-1", BreachStatus.Resolved, "Issue fixed");

        await database.Received(1).StringSetAsync(
            Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(), Arg.Any<bool>(), Arg.Any<When>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task UpdateBreachStatusAsync_WhenReEncryptionFails_DoesNotOverwrite()
    {
        var (service, database, encryptionService, _) = CreateDataProtectionService();

        var breach = new DataBreach { Id = "breach-1", Status = BreachStatus.Discovered };
        var json = JsonSerializer.Serialize(breach);

        database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns((RedisValue)"encrypted");

        encryptionService.DecryptAsync(Arg.Any<string>(), Arg.Any<EncryptionContext>(), Arg.Any<CancellationToken>())
            .Returns(new DecryptionResult { Success = true, Data = json });

        encryptionService.EncryptAsync(Arg.Any<string>(), Arg.Any<EncryptionContext>(), Arg.Any<CancellationToken>())
            .Returns(new EncryptionResult { Success = false });

        await service.UpdateBreachStatusAsync("breach-1", BreachStatus.Resolved, "fixed");

        // Should NOT call StringSetAsync because re-encryption failed
        await database.DidNotReceive().StringSetAsync(
            Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(), Arg.Any<bool>(), Arg.Any<When>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task UpdateBreachStatusAsync_WhenExceptionThrown_DoesNotThrow()
    {
        var (service, database, _, _) = CreateDataProtectionService();

        database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("read failed"));

        var act = () => service.UpdateBreachStatusAsync("breach-1", BreachStatus.Resolved, "fixed");

        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Background Services — DataRetentionBackgroundService

    [Fact]
    public async Task DataRetentionBackgroundService_WhenRetentionDisabled_ExitsImmediately()
    {
        var dataProtectionService = Substitute.For<IDataProtectionService>();
        var logger = Substitute.For<ILogger<DataRetentionBackgroundService>>();
        var options = Options.Create(new DataProtectionOptions
        {
            EnableAutomatedDataRetention = false
        });

        var service = new DataRetentionBackgroundService(dataProtectionService, logger, options);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await service.StartAsync(cts.Token);
        // Allow time for ExecuteAsync to run
        await Task.Delay(100);
        await service.StopAsync(CancellationToken.None);

        // Should have logged that it is disabled — no exception thrown
    }

    [Fact]
    public async Task DataRetentionBackgroundService_WhenEnabled_RespondsToCancel()
    {
        var dataProtectionService = Substitute.For<IDataProtectionService>();
        var logger = Substitute.For<ILogger<DataRetentionBackgroundService>>();
        var options = Options.Create(new DataProtectionOptions
        {
            EnableAutomatedDataRetention = true,
            DataRetentionCheckInterval = TimeSpan.FromMilliseconds(50)
        });

        var service = new DataRetentionBackgroundService(dataProtectionService, logger, options);
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        // Let the loop run at least one iteration
        await Task.Delay(200);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Should complete without throwing
    }

    #endregion

    #region Background Services — ConsentMaintenanceBackgroundService

    [Fact]
    public async Task ConsentMaintenanceBackgroundService_WhenMonitoringDisabled_ExitsImmediately()
    {
        var consentService = Substitute.For<IConsentManagementService>();
        var logger = Substitute.For<ILogger<ConsentMaintenanceBackgroundService>>();
        var options = Options.Create(new ConsentManagementOptions
        {
            EnableConsentMonitoring = false
        });

        var dataProtectionService = Substitute.For<IDataProtectionService>();
        var service = new ConsentMaintenanceBackgroundService(consentService, dataProtectionService, logger, options);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await service.StartAsync(cts.Token);
        await Task.Delay(100);
        await service.StopAsync(CancellationToken.None);

        // Should complete without throwing
    }

    [Fact]
    public async Task ConsentMaintenanceBackgroundService_WhenEnabled_RespondsToCancel()
    {
        var consentService = Substitute.For<IConsentManagementService>();
        var logger = Substitute.For<ILogger<ConsentMaintenanceBackgroundService>>();
        var options = Options.Create(new ConsentManagementOptions
        {
            EnableConsentMonitoring = true
        });

        var dataProtectionService = Substitute.For<IDataProtectionService>();
        var service = new ConsentMaintenanceBackgroundService(consentService, dataProtectionService, logger, options);
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        await Task.Delay(100);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Should complete without throwing
    }

    #endregion

    #region Background Services — ComplianceMonitoringBackgroundService

    [Fact]
    public async Task ComplianceMonitoringBackgroundService_RespondsToCancel()
    {
        var dataProtectionService = Substitute.For<IDataProtectionService>();
        var logger = Substitute.For<ILogger<ComplianceMonitoringBackgroundService>>();

        var options = Options.Create(new DataProtectionOptions { EnableComplianceMonitoring = true });
        var service = new ComplianceMonitoringBackgroundService(dataProtectionService, logger, options);
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        await Task.Delay(100);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Should complete without throwing
    }

    [Fact]
    public async Task ComplianceMonitoringBackgroundService_Constructor_DoesNotThrow()
    {
        var dataProtectionService = Substitute.For<IDataProtectionService>();
        var logger = Substitute.For<ILogger<ComplianceMonitoringBackgroundService>>();

        var options = Options.Create(new DataProtectionOptions { EnableComplianceMonitoring = true });
        var act = () => new ComplianceMonitoringBackgroundService(dataProtectionService, logger, options);

        act.Should().NotThrow();
    }

    #endregion

    #region DataProtectionService — Interfaces resolution through DI

    [Fact]
    public void DataProtectionService_RegisteredAsAllThreeInterfaces_ResolvesToSameInstance()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        AddRequiredDependencies(services);
        services.AddDataProtectionCompliance(configuration);

        var sp = services.BuildServiceProvider();

        var dpService = sp.GetRequiredService<IDataProtectionService>();
        var consentService = sp.GetRequiredService<IConsentManagementService>();
        var breachService = sp.GetRequiredService<IDataBreachNotificationService>();
        var concrete = sp.GetRequiredService<DataProtectionService>();

        dpService.Should().BeSameAs(concrete);
        consentService.Should().BeSameAs(concrete);
        breachService.Should().BeSameAs(concrete);
    }

    #endregion

    #region ApplyDataRetentionAsync

    [Fact]
    public async Task ApplyDataRetentionAsync_MarksExpiredConsents_ReturnsCorrectCount()
    {
        var (service, database, encryptionService, auditService) = CreateDataProtectionServiceWithServer(
            configureOptions: o => o.MaxConsentAge = TimeSpan.FromDays(1));

        // Setup an expired consent key
        var expiredConsent = new ConsentRecord
        {
            DataSubjectId = "user-1",
            ProcessingPurpose = "marketing",
            Status = ConsentStatus.Given,
            ConsentTimestamp = DateTime.UtcNow.AddDays(-10) // Expired (MaxConsentAge = 1 day)
        };
        var expiredJson = JsonSerializer.Serialize(expiredConsent);

        SetupDecryptionSuccess(encryptionService, expiredJson);
        SetupEncryptionSuccess(encryptionService);
        SetupAuditSuccess(auditService);

        var server = GetMockedServer(database);
        SetupServerKeys(server, "gdpr:consent:*", new RedisKey[] { "gdpr:consent:user-1:marketing" });
        SetupServerKeys(server, "gdpr:token:*", Array.Empty<RedisKey>());
        SetupServerKeys(server, "gdpr:audit:*", Array.Empty<RedisKey>());

        database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns((RedisValue)"encrypted-data");

        var result = await service.ApplyDataRetentionAsync();

        result.ExpiredConsentsMarked.Should().Be(1);
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ApplyDataRetentionAsync_SetsExpiryOnOrphanedTokenKeys()
    {
        var (service, database, encryptionService, auditService) = CreateDataProtectionServiceWithServer();

        SetupAuditSuccess(auditService);

        var server = GetMockedServer(database);
        SetupServerKeys(server, "gdpr:consent:*", Array.Empty<RedisKey>());
        SetupServerKeys(server, "gdpr:token:*", new RedisKey[] { "gdpr:token:orphaned-1" });
        SetupServerKeys(server, "gdpr:audit:*", Array.Empty<RedisKey>());

        // Token key has no TTL (orphaned) — NSubstitute returns default null for unmatched calls,
        // but we set it up explicitly for clarity
        database.KeyTimeToLiveAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns((TimeSpan?)null);

        var result = await service.ApplyDataRetentionAsync();

        result.ExpiredTokensRemoved.Should().Be(1);
    }

    [Fact]
    public async Task ApplyDataRetentionAsync_ReturnsSuccessResult_WhenNoErrors()
    {
        var (service, database, _, auditService) = CreateDataProtectionServiceWithServer();

        SetupAuditSuccess(auditService);

        var server = GetMockedServer(database);
        SetupServerKeys(server, "gdpr:consent:*", Array.Empty<RedisKey>());
        SetupServerKeys(server, "gdpr:token:*", Array.Empty<RedisKey>());
        SetupServerKeys(server, "gdpr:audit:*", Array.Empty<RedisKey>());

        var result = await service.ApplyDataRetentionAsync();

        result.Success.Should().BeTrue();
        result.ExpiredConsentsMarked.Should().Be(0);
        result.ExpiredTokensRemoved.Should().Be(0);
        result.AuditLogsArchived.Should().Be(0);
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyDataRetentionAsync_HandlesRedisError_ReturnsErrorInResult()
    {
        var connectionMultiplexer = Substitute.For<IConnectionMultiplexer>();
        var database = Substitute.For<IDatabase>();
        connectionMultiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(database);

        // Make GetEndPoints throw to simulate Redis failure
        connectionMultiplexer.GetEndPoints(Arg.Any<bool>())
            .Returns(_ => throw new RedisException("connection failed"));
        database.Multiplexer.Returns(connectionMultiplexer);

        var logger = Substitute.For<ILogger<DataProtectionService>>();
        var auditService = Substitute.For<ISecurityAuditService>();
        var encryptionService = Substitute.For<IDataEncryptionService>();
        var options = Options.Create(new DataProtectionOptions());

        var service = new DataProtectionService(connectionMultiplexer, logger, auditService, encryptionService, options);

        var result = await service.ApplyDataRetentionAsync();

        result.Errors.Should().NotBeEmpty();
        result.Success.Should().BeFalse();
    }

    #endregion

    #region GenerateComplianceReportAsync

    [Fact]
    public async Task GenerateComplianceReportAsync_CountsConsentsByStatus()
    {
        var (service, database, encryptionService, auditService) = CreateDataProtectionServiceWithServer();

        SetupAuditSuccess(auditService);

        var server = GetMockedServer(database);

        // Setup consent keys (3 consents with different statuses)
        SetupServerKeys(server, "gdpr:consent:*", new RedisKey[]
        {
            "gdpr:consent:user-1:marketing",
            "gdpr:consent:user-2:analytics",
            "gdpr:consent:user-3:personalization"
        });
        SetupServerKeys(server, "gdpr:breach:*", Array.Empty<RedisKey>());

        var givenConsent = JsonSerializer.Serialize(new ConsentRecord { Status = ConsentStatus.Given });
        var expiredConsent = JsonSerializer.Serialize(new ConsentRecord { Status = ConsentStatus.Expired });
        var withdrawnConsent = JsonSerializer.Serialize(new ConsentRecord { Status = ConsentStatus.Withdrawn });

        // Return different encrypted data per key so we can map decryption results
        database.StringGetAsync(Arg.Is<RedisKey>(k => k.ToString().Contains("user-1")), Arg.Any<CommandFlags>())
            .Returns((RedisValue)"enc-given");
        database.StringGetAsync(Arg.Is<RedisKey>(k => k.ToString().Contains("user-2")), Arg.Any<CommandFlags>())
            .Returns((RedisValue)"enc-expired");
        database.StringGetAsync(Arg.Is<RedisKey>(k => k.ToString().Contains("user-3")), Arg.Any<CommandFlags>())
            .Returns((RedisValue)"enc-withdrawn");

        encryptionService.DecryptAsync("enc-given", Arg.Any<EncryptionContext>(), Arg.Any<CancellationToken>())
            .Returns(new DecryptionResult { Success = true, Data = givenConsent });
        encryptionService.DecryptAsync("enc-expired", Arg.Any<EncryptionContext>(), Arg.Any<CancellationToken>())
            .Returns(new DecryptionResult { Success = true, Data = expiredConsent });
        encryptionService.DecryptAsync("enc-withdrawn", Arg.Any<EncryptionContext>(), Arg.Any<CancellationToken>())
            .Returns(new DecryptionResult { Success = true, Data = withdrawnConsent });

        var report = await service.GenerateComplianceReportAsync();

        report.ActiveConsentsCount.Should().Be(1);
        report.ExpiredConsentsCount.Should().Be(1);
        report.WithdrawnConsentsCount.Should().Be(1);
    }

    [Fact]
    public async Task GenerateComplianceReportAsync_CountsBreaches()
    {
        var (service, database, encryptionService, auditService) = CreateDataProtectionServiceWithServer();

        SetupAuditSuccess(auditService);

        var server = GetMockedServer(database);
        SetupServerKeys(server, "gdpr:consent:*", Array.Empty<RedisKey>());
        SetupServerKeys(server, "gdpr:breach:*", new RedisKey[]
        {
            "gdpr:breach:b-1",
            "gdpr:breach:b-2"
        });

        var openBreach = JsonSerializer.Serialize(new DataBreach { Status = BreachStatus.Discovered });
        var resolvedBreach = JsonSerializer.Serialize(new DataBreach { Status = BreachStatus.Resolved });

        database.StringGetAsync(Arg.Is<RedisKey>(k => k.ToString().Contains("b-1")), Arg.Any<CommandFlags>())
            .Returns((RedisValue)"enc-open");
        database.StringGetAsync(Arg.Is<RedisKey>(k => k.ToString().Contains("b-2")), Arg.Any<CommandFlags>())
            .Returns((RedisValue)"enc-resolved");

        encryptionService.DecryptAsync("enc-open", Arg.Any<EncryptionContext>(), Arg.Any<CancellationToken>())
            .Returns(new DecryptionResult { Success = true, Data = openBreach });
        encryptionService.DecryptAsync("enc-resolved", Arg.Any<EncryptionContext>(), Arg.Any<CancellationToken>())
            .Returns(new DecryptionResult { Success = true, Data = resolvedBreach });

        var report = await service.GenerateComplianceReportAsync();

        report.DataBreachesOpen.Should().Be(1);
        report.DataBreachesResolved.Should().Be(1);
    }

    [Fact]
    public async Task GenerateComplianceReportAsync_GeneratesWarningForOpenBreaches()
    {
        var (service, database, encryptionService, auditService) = CreateDataProtectionServiceWithServer();

        SetupAuditSuccess(auditService);

        var server = GetMockedServer(database);
        SetupServerKeys(server, "gdpr:consent:*", Array.Empty<RedisKey>());
        SetupServerKeys(server, "gdpr:breach:*", new RedisKey[] { "gdpr:breach:b-1" });

        var openBreach = JsonSerializer.Serialize(new DataBreach { Status = BreachStatus.Discovered });
        database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns((RedisValue)"enc-open");
        encryptionService.DecryptAsync(Arg.Any<string>(), Arg.Any<EncryptionContext>(), Arg.Any<CancellationToken>())
            .Returns(new DecryptionResult { Success = true, Data = openBreach });

        var report = await service.GenerateComplianceReportAsync();

        report.Warnings.Should().Contain(w => w.Contains("Art. 33"));
    }

    [Fact]
    public async Task GenerateComplianceReportAsync_EmptyRedis_ReturnsEmptyReport()
    {
        var (service, database, _, auditService) = CreateDataProtectionServiceWithServer();

        SetupAuditSuccess(auditService);

        var server = GetMockedServer(database);
        SetupServerKeys(server, "gdpr:consent:*", Array.Empty<RedisKey>());
        SetupServerKeys(server, "gdpr:breach:*", Array.Empty<RedisKey>());

        var report = await service.GenerateComplianceReportAsync();

        report.ActiveConsentsCount.Should().Be(0);
        report.ExpiredConsentsCount.Should().Be(0);
        report.WithdrawnConsentsCount.Should().Be(0);
        report.DataBreachesOpen.Should().Be(0);
        report.DataBreachesResolved.Should().Be(0);
        report.Warnings.Should().BeEmpty();
    }

    #endregion

    #region Background Services — ComplianceMonitoringBackgroundService (Extended)

    [Fact]
    public async Task ComplianceMonitoringBackgroundService_WhenDisabled_ExitsImmediately()
    {
        var dataProtectionService = Substitute.For<IDataProtectionService>();
        var logger = Substitute.For<ILogger<ComplianceMonitoringBackgroundService>>();
        var options = Options.Create(new DataProtectionOptions
        {
            EnableComplianceMonitoring = false
        });

        var service = new ComplianceMonitoringBackgroundService(dataProtectionService, logger, options);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await service.StartAsync(cts.Token);
        await Task.Delay(200);
        await service.StopAsync(CancellationToken.None);

        await dataProtectionService.DidNotReceive()
            .GenerateComplianceReportAsync(Arg.Any<CancellationToken>());
    }

    #endregion

    #region Background Services — ConsentMaintenanceBackgroundService (Extended)

    [Fact]
    public async Task ConsentMaintenanceBackgroundService_UsesDataProtectionService_NotCast()
    {
        var consentService = Substitute.For<IConsentManagementService>();
        var dataProtectionService = Substitute.For<IDataProtectionService>();
        var logger = Substitute.For<ILogger<ConsentMaintenanceBackgroundService>>();
        var options = Options.Create(new ConsentManagementOptions
        {
            EnableConsentMonitoring = true
        });

        dataProtectionService.GenerateComplianceReportAsync(Arg.Any<CancellationToken>())
            .Returns(new ComplianceReport());

        var service = new ConsentMaintenanceBackgroundService(consentService, dataProtectionService, logger, options);
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        // Let one iteration complete
        await Task.Delay(200);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        await dataProtectionService.Received()
            .GenerateComplianceReportAsync(Arg.Any<CancellationToken>());
    }

    #endregion

    #region Helper Methods

    private static void AddRequiredDependencies(IServiceCollection services)
    {
        // IConnectionMultiplexer for Redis
        var connectionMultiplexer = Substitute.For<IConnectionMultiplexer>();
        var database = Substitute.For<IDatabase>();
        connectionMultiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(database);
        services.AddSingleton(connectionMultiplexer);

        // ISecurityAuditService
        var auditService = Substitute.For<ISecurityAuditService>();
        auditService.LogSecurityEventAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SecurityEventSeverity>(),
                Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns("audit-id");
        services.AddSingleton(auditService);

        // IDataEncryptionService
        var encryptionService = Substitute.For<IDataEncryptionService>();
        encryptionService.EncryptAsync(Arg.Any<string>(), Arg.Any<EncryptionContext>(), Arg.Any<CancellationToken>())
            .Returns(new EncryptionResult { Success = true, EncryptedData = "encrypted" });
        encryptionService.DecryptAsync(Arg.Any<string>(), Arg.Any<EncryptionContext>(), Arg.Any<CancellationToken>())
            .Returns(new DecryptionResult { Success = true, Data = "{}" });
        services.AddSingleton(encryptionService);

        // Logging
        services.AddLogging();
    }

    private static (DataProtectionService service, IDatabase database, IDataEncryptionService encryption, ISecurityAuditService audit)
        CreateDataProtectionService(Action<DataProtectionOptions>? configureOptions = null)
    {
        var connectionMultiplexer = Substitute.For<IConnectionMultiplexer>();
        var database = Substitute.For<IDatabase>();
        connectionMultiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(database);

        var logger = Substitute.For<ILogger<DataProtectionService>>();
        var auditService = Substitute.For<ISecurityAuditService>();
        var encryptionService = Substitute.For<IDataEncryptionService>();

        var dpOptions = new DataProtectionOptions();
        configureOptions?.Invoke(dpOptions);
        var options = Options.Create(dpOptions);

        var service = new DataProtectionService(
            connectionMultiplexer, logger, auditService, encryptionService, options);

        return (service, database, encryptionService, auditService);
    }

    private static ConsentRecord CreateValidConsent()
    {
        return new ConsentRecord
        {
            DataSubjectId = "user-1",
            ProcessingPurpose = "marketing",
            ConsentText = "I agree to marketing communications",
            ConsentMethod = "web-form",
            FreelyGiven = true,
            Specific = true,
            Informed = true,
            Unambiguous = true
        };
    }

    private static void SetupEncryptionSuccess(IDataEncryptionService encryptionService)
    {
        encryptionService.EncryptAsync(Arg.Any<string>(), Arg.Any<EncryptionContext>(), Arg.Any<CancellationToken>())
            .Returns(new EncryptionResult { Success = true, EncryptedData = "encrypted-data" });
    }

    private static void SetupAuditSuccess(ISecurityAuditService auditService)
    {
        auditService.LogSecurityEventAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SecurityEventSeverity>(),
                Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns("audit-id");
    }

    private static void SetupDecryptionSuccess(IDataEncryptionService encryptionService, string decryptedData)
    {
        encryptionService.DecryptAsync(Arg.Any<string>(), Arg.Any<EncryptionContext>(), Arg.Any<CancellationToken>())
            .Returns(new DecryptionResult { Success = true, Data = decryptedData });
    }

    /// <summary>
    /// Creates a DataProtectionService with a fully-mocked IServer wired through
    /// IDatabase.Multiplexer so that KeysAsync scans work.
    /// </summary>
    private static (DataProtectionService service, IDatabase database, IDataEncryptionService encryption, ISecurityAuditService audit)
        CreateDataProtectionServiceWithServer(Action<DataProtectionOptions>? configureOptions = null)
    {
        var connectionMultiplexer = Substitute.For<IConnectionMultiplexer>();
        var database = Substitute.For<IDatabase>();
        var server = Substitute.For<IServer>();

        connectionMultiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(database);

        var endpoint = new DnsEndPoint("localhost", 6379);
        connectionMultiplexer.GetEndPoints(Arg.Any<bool>()).Returns(new EndPoint[] { endpoint });
        connectionMultiplexer.GetServer(Arg.Any<EndPoint>(), Arg.Any<object>()).Returns(server);

        // Wire IDatabase.Multiplexer back to the same mock
        database.Multiplexer.Returns(connectionMultiplexer);

        var logger = Substitute.For<ILogger<DataProtectionService>>();
        var auditService = Substitute.For<ISecurityAuditService>();
        var encryptionService = Substitute.For<IDataEncryptionService>();

        var dpOptions = new DataProtectionOptions();
        configureOptions?.Invoke(dpOptions);
        var options = Options.Create(dpOptions);

        var service = new DataProtectionService(
            connectionMultiplexer, logger, auditService, encryptionService, options);

        return (service, database, encryptionService, auditService);
    }

    /// <summary>
    /// Gets the mocked IServer from the IDatabase.Multiplexer chain.
    /// Must be called after CreateDataProtectionServiceWithServer.
    /// </summary>
    private static IServer GetMockedServer(IDatabase database)
    {
        return database.Multiplexer.GetServer(database.Multiplexer.GetEndPoints()[0]);
    }

    /// <summary>
    /// Sets up IServer.KeysAsync to return the given keys for a specific pattern.
    /// </summary>
    private static void SetupServerKeys(IServer server, string pattern, RedisKey[] keys)
    {
        server.KeysAsync(
            Arg.Any<int>(),
            Arg.Is<RedisValue>(v => v.ToString() == pattern),
            Arg.Any<int>(),
            Arg.Any<long>(),
            Arg.Any<int>(),
            Arg.Any<CommandFlags>())
            .Returns(ToAsyncEnumerable(keys));
    }

    private static async IAsyncEnumerable<RedisKey> ToAsyncEnumerable(
        RedisKey[] keys,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var key in keys)
        {
            if (cancellationToken.IsCancellationRequested) yield break;
            yield return key;
        }
        await Task.CompletedTask; // Ensure the method is truly async
    }

    #endregion
}
