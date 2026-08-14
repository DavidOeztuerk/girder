using Girder.Cqrs.Models;
using FluentAssertions;

namespace Shared.Tests.Models;

public class ApiResponseTests
{
    #region SuccessResult

    [Fact]
    public void SuccessResult_WithData_SetsAllFields()
    {
        var data = new TestDto { Name = "Test" };

        var response = ApiResponse<TestDto>.SuccessResult(data, "Created successfully");

        response.Success.Should().BeTrue();
        response.Data.Should().Be(data);
        response.Message.Should().Be("Created successfully");
        response.Errors.Should().BeNull();
        response.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        response.TraceId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void SuccessResult_WithNullData_SetsNullData()
    {
        var response = ApiResponse<TestDto>.SuccessResult(null);

        response.Success.Should().BeTrue();
        response.Data.Should().BeNull();
        response.Message.Should().BeNull();
    }

    [Fact]
    public void SuccessResult_WithoutMessage_SetsNullMessage()
    {
        var response = ApiResponse<string>.SuccessResult("value");

        response.Success.Should().BeTrue();
        response.Message.Should().BeNull();
    }

    #endregion

    #region ErrorResult (single error)

    [Fact]
    public void ErrorResult_SingleError_SetsFields()
    {
        var response = ApiResponse<TestDto>.ErrorResult("Something went wrong");

        response.Success.Should().BeFalse();
        response.Data.Should().BeNull();
        response.Errors.Should().ContainSingle().Which.Should().Be("Something went wrong");
    }

    [Fact]
    public void ErrorResult_WithTraceId_SetsTraceId()
    {
        var response = ApiResponse<TestDto>.ErrorResult("Error", traceId: "trace-123");

        response.TraceId.Should().Be("trace-123");
    }

    [Fact]
    public void ErrorResult_WithErrorCode_SetsErrorCode()
    {
        var response = ApiResponse<TestDto>.ErrorResult("Error", errorCode: "ERR_1000");

        response.ErrorCode.Should().Be("ERR_1000");
    }

    [Fact]
    public void ErrorResult_WithHelpUrl_SetsHelpUrl()
    {
        var response = ApiResponse<TestDto>.ErrorResult("Error", helpUrl: "https://docs.example.com/errors/1000");

        response.HelpUrl.Should().Be("https://docs.example.com/errors/1000");
    }

    #endregion

    #region ErrorResult (multiple errors)

    [Fact]
    public void ErrorResult_MultipleErrors_SetsAllErrors()
    {
        var errors = new List<string> { "Error 1", "Error 2", "Error 3" };

        var response = ApiResponse<TestDto>.ErrorResult(errors);

        response.Success.Should().BeFalse();
        response.Errors.Should().HaveCount(3);
        response.Errors.Should().ContainInOrder("Error 1", "Error 2", "Error 3");
    }

    [Fact]
    public void ErrorResult_MultipleErrors_WithAllOptionalParams()
    {
        var errors = new List<string> { "Validation failed" };

        var response = ApiResponse<TestDto>.ErrorResult(
            errors,
            traceId: "trace-456",
            errorCode: "ERR_3000",
            helpUrl: "https://docs.example.com/validation");

        response.TraceId.Should().Be("trace-456");
        response.ErrorCode.Should().Be("ERR_3000");
        response.HelpUrl.Should().Be("https://docs.example.com/validation");
    }

    #endregion

    #region Default Values

    [Fact]
    public void NewInstance_HasTimestampAndTraceId()
    {
        var response = new ApiResponse<string>();

        response.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        response.TraceId.Should().NotBeNullOrEmpty();
    }

    #endregion

    private class TestDto
    {
        public string Name { get; set; } = string.Empty;
    }
}
