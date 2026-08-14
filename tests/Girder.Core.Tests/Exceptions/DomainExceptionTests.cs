using Core.Common.Exceptions;
using FluentAssertions;

namespace Shared.Tests.Exceptions;

public class DomainExceptionTests
{
    #region ResourceNotFoundException

    [Fact]
    public void ResourceNotFoundException_SetsAllProperties()
    {
        var ex = new ResourceNotFoundException("User", "user-123");

        ex.ResourceType.Should().Be("User");
        ex.ResourceId.Should().Be("user-123");
        ex.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
        ex.Message.Should().Contain("User").And.Contain("user-123");
        ex.Details.Should().Contain("user");
        ex.GetHttpStatusCode().Should().Be(404);
        ex.AdditionalData.Should().ContainKey("ResourceType").WhoseValue.Should().Be("User");
        ex.AdditionalData.Should().ContainKey("ResourceId").WhoseValue.Should().Be("user-123");
    }

    [Fact]
    public void ResourceNotFoundException_WithCustomMessage_OverridesDefault()
    {
        var ex = new ResourceNotFoundException("Skill", "skill-1", "Custom not found message");

        ex.Message.Should().Be("Custom not found message");
    }

    #endregion

    #region ResourceAlreadyExistsException

    [Fact]
    public void ResourceAlreadyExistsException_SetsAllProperties()
    {
        var ex = new ResourceAlreadyExistsException("User", "Email", "test@example.com");

        ex.ResourceType.Should().Be("User");
        ex.DuplicateField.Should().Be("Email");
        ex.DuplicateValue.Should().Be("test@example.com");
        ex.ErrorCode.Should().Be(ErrorCodes.ResourceAlreadyExists);
        ex.Message.Should().Contain("user").And.Contain("Email").And.Contain("test@example.com");
        ex.Details.Should().Contain("email").And.Contain("user");
        ex.GetHttpStatusCode().Should().Be(409);
    }

    [Fact]
    public void ResourceAlreadyExistsException_WithCustomMessage_OverridesDefault()
    {
        var ex = new ResourceAlreadyExistsException("User", "Email", "test@example.com", "Duplicate email");

        ex.Message.Should().Be("Duplicate email");
    }

    #endregion

    #region BusinessRuleViolationException

    [Fact]
    public void BusinessRuleViolationException_SetsAllProperties()
    {
        var ex = new BusinessRuleViolationException(
            ErrorCodes.BusinessRuleViolation,
            "MaxSkillsPerUser",
            "User cannot have more than 10 skills",
            "Limit reached");

        ex.RuleName.Should().Be("MaxSkillsPerUser");
        ex.ErrorCode.Should().Be(ErrorCodes.BusinessRuleViolation);
        ex.Message.Should().Be("User cannot have more than 10 skills");
        ex.Details.Should().Be("Limit reached");
        ex.GetHttpStatusCode().Should().Be(422);
    }

    [Fact]
    public void BusinessRuleViolationException_WithAdditionalData()
    {
        var data = new Dictionary<string, object> { ["CurrentCount"] = 10, ["MaxCount"] = 10 };
        var ex = new BusinessRuleViolationException(
            ErrorCodes.BusinessRuleViolation,
            "MaxSkills",
            "Too many skills",
            additionalData: data);

        ex.AdditionalData.Should().ContainKey("CurrentCount").WhoseValue.Should().Be(10);
    }

    #endregion

    #region InsufficientPermissionsException

    [Fact]
    public void InsufficientPermissionsException_SetsAllProperties()
    {
        var ex = new InsufficientPermissionsException("admin:manage-users", "user-1");

        ex.RequiredPermission.Should().Be("admin:manage-users");
        ex.UserId.Should().Be("user-1");
        ex.ErrorCode.Should().Be(ErrorCodes.InsufficientPermissions);
        ex.Message.Should().Contain("admin:manage-users");
        ex.GetHttpStatusCode().Should().Be(403);
        ex.AdditionalData.Should().ContainKey("UserId").WhoseValue.Should().Be("user-1");
    }

    [Fact]
    public void InsufficientPermissionsException_NullUserId_SetsAnonymous()
    {
        var ex = new InsufficientPermissionsException("admin:delete");

        ex.UserId.Should().BeNull();
        ex.AdditionalData.Should().ContainKey("UserId").WhoseValue.Should().Be("Anonymous");
    }

    #endregion

    #region InvalidOperationException

    [Fact]
    public void InvalidOperationException_SetsAllProperties()
    {
        var ex = new Core.Common.Exceptions.InvalidOperationException("Cancel", "Completed");

        ex.Operation.Should().Be("Cancel");
        ex.CurrentState.Should().Be("Completed");
        ex.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
        ex.Message.Should().Contain("Cancel").And.Contain("Completed");
        ex.GetHttpStatusCode().Should().Be(400);
    }

    [Fact]
    public void InvalidOperationException_WithCustomMessage_OverridesDefault()
    {
        var ex = new Core.Common.Exceptions.InvalidOperationException(
            "Start", "Active", "Session is already active");

        ex.Message.Should().Be("Session is already active");
    }

    #endregion

    #region InvalidCredentialsException

    [Fact]
    public void InvalidCredentialsException_NotLocked_SetsDefaultMessage()
    {
        var ex = new InvalidCredentialsException("test@test.com");

        ex.Email.Should().Be("test@test.com");
        ex.IsLocked.Should().BeFalse();
        ex.RemainingAttempts.Should().BeNull();
        ex.ErrorCode.Should().Be(ErrorCodes.InvalidCredentials);
        ex.Message.Should().Contain("Ungültige E-Mail-Adresse oder ungültiges Passwort");
        ex.GetHttpStatusCode().Should().Be(401);
    }

    [Fact]
    public void InvalidCredentialsException_Locked_SetsLockedMessage()
    {
        var ex = new InvalidCredentialsException("test@test.com", isLocked: true);

        ex.IsLocked.Should().BeTrue();
        ex.Message.Should().Contain("gesperrt");
        // Details (developer-facing) remain in English for log-grep stability.
        ex.Details.Should().Contain("locked");
    }

    [Fact]
    public void InvalidCredentialsException_WithRemainingAttempts_IncludesInDetails()
    {
        var ex = new InvalidCredentialsException(remainingAttempts: 3);

        ex.RemainingAttempts.Should().Be(3);
        ex.Details.Should().Contain("3");
    }

    #endregion

    #region EmailVerificationRequiredException

    [Fact]
    public void EmailVerificationRequiredException_SetsAllProperties()
    {
        var createdAt = DateTime.UtcNow.AddDays(-2);
        var ex = new EmailVerificationRequiredException("test@test.com", createdAt);

        ex.Email.Should().Be("test@test.com");
        ex.AccountCreatedAt.Should().Be(createdAt);
        ex.CanResendVerification.Should().BeTrue();
        ex.ErrorCode.Should().Be(ErrorCodes.AccountNotVerified);
        ex.Message.Should().Contain("verification");
        ex.Details.Should().Contain("Resend Verification");
        ex.GetHttpStatusCode().Should().Be(403);
    }

    [Fact]
    public void EmailVerificationRequiredException_CannotResend_AlternateDetails()
    {
        var ex = new EmailVerificationRequiredException("test@test.com", DateTime.UtcNow, canResendVerification: false);

        ex.CanResendVerification.Should().BeFalse();
        ex.Details.Should().Contain("spam folder");
    }

    #endregion

    #region ExternalServiceException

    [Fact]
    public void ExternalServiceException_SetsAllProperties()
    {
        var inner = new TimeoutException("timed out");
        var ex = new ExternalServiceException(
            "PaymentService", "Payment failed", "/api/payments", 503, inner);

        ex.ServiceName.Should().Be("PaymentService");
        ex.Endpoint.Should().Be("/api/payments");
        ex.StatusCode.Should().Be(503);
        ex.ErrorCode.Should().Be(ErrorCodes.ExternalServiceError);
        ex.InnerException.Should().Be(inner);
        ex.GetHttpStatusCode().Should().Be(503);
    }

    [Fact]
    public void ExternalServiceException_MinimalParams_SetsDefaults()
    {
        var ex = new ExternalServiceException("UserService", "Service down");

        ex.Endpoint.Should().BeNull();
        ex.StatusCode.Should().BeNull();
        ex.InnerException.Should().BeNull();
    }

    #endregion

    #region ConfigurationException

    [Fact]
    public void ConfigurationException_WithKey_SetsProperties()
    {
        var ex = new ConfigurationException("JwtSecret", "Authentication");

        ex.ConfigurationKey.Should().Be("JwtSecret");
        ex.ConfigurationSection.Should().Be("Authentication");
        ex.ErrorCode.Should().Be(ErrorCodes.ConfigurationMissing);
        ex.Message.Should().Contain("JwtSecret").And.Contain("Authentication");
        ex.GetHttpStatusCode().Should().Be(500);
    }

    [Fact]
    public void ConfigurationException_WithKeyOnly_NoSection()
    {
        var ex = new ConfigurationException("ConnectionString");

        ex.ConfigurationSection.Should().BeNull();
        ex.Message.Should().Contain("ConnectionString");
        ex.Message.Should().NotContain("section");
    }

    [Fact]
    public void ConfigurationException_WithInnerException_SetsFields()
    {
        var inner = new FormatException("bad format");
        var ex = new ConfigurationException("Config parse error", inner);

        ex.ConfigurationKey.Should().Be(string.Empty);
        ex.ErrorCode.Should().Be(ErrorCodes.ConfigurationError);
        ex.InnerException.Should().Be(inner);
    }

    #endregion
}
