using Girder.Core.Exceptions;
using FluentAssertions;

namespace Shared.Tests.Exceptions;

public class HelpUrlsTests
{
    [Theory]
    [InlineData(ErrorCodes.InvalidCredentials)]
    [InlineData(ErrorCodes.TokenExpired)]
    [InlineData(ErrorCodes.Unauthorized)]
    [InlineData(ErrorCodes.InsufficientPermissions)]
    [InlineData(ErrorCodes.RequiredFieldMissing)]
    [InlineData(ErrorCodes.InvalidFormat)]
    [InlineData(ErrorCodes.InvalidInput)]
    [InlineData(ErrorCodes.InvalidOperation)]
    [InlineData(ErrorCodes.ResourceNotFound)]
    [InlineData(ErrorCodes.ResourceAlreadyExists)]
    [InlineData(ErrorCodes.BusinessRuleViolation)]
    [InlineData(ErrorCodes.QuotaExceeded)]
    [InlineData(ErrorCodes.FeatureNotAvailable)]
    [InlineData(ErrorCodes.InternalError)]
    [InlineData(ErrorCodes.ServiceUnavailable)]
    [InlineData(ErrorCodes.NetworkError)]
    [InlineData(ErrorCodes.DatabaseError)]
    [InlineData(ErrorCodes.ExternalServiceError)]
    [InlineData(ErrorCodes.RateLimitExceeded)]
    [InlineData(ErrorCodes.TwoFactorRequired)]
    [InlineData(ErrorCodes.InvalidTwoFactorCode)]
    public void GetHelpUrl_MappedCode_ReturnsNonNullUrl(string errorCode)
    {
        var url = HelpUrls.GetHelpUrl(errorCode);

        url.Should().NotBeNull();
        url.Should().StartWith("https://docs.girder.com/errors");
    }

    [Fact]
    public void GetHelpUrl_UnmappedCode_ReturnsNull()
    {
        var url = HelpUrls.GetHelpUrl("ERR_UNMAPPED");

        url.Should().BeNull();
    }

    [Fact]
    public void GetHelpUrl_NullCode_ReturnsNull()
    {
        var url = HelpUrls.GetHelpUrl(null);

        url.Should().BeNull();
    }

    [Fact]
    public void Constants_AllStartWithBaseUrl()
    {
        HelpUrls.InvalidCredentials.Should().StartWith("https://docs.girder.com/errors/");
        HelpUrls.ResourceNotFound.Should().StartWith("https://docs.girder.com/errors/");
        HelpUrls.InternalError.Should().StartWith("https://docs.girder.com/errors/");
        HelpUrls.RateLimitExceeded.Should().StartWith("https://docs.girder.com/errors/");
    }
}
