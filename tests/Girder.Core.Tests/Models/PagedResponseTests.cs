using CQRS.Models;
using FluentAssertions;

namespace Shared.Tests.Models;

public class PagedResponseTests
{
    #region Create

    [Fact]
    public void Create_SetsAllPaginationFields()
    {
        var data = new List<string> { "a", "b", "c" };

        var response = PagedResponse<string>.Create(data, pageNumber: 2, pageSize: 10, totalRecords: 35, "Fetched");

        response.Success.Should().BeTrue();
        response.Data.Should().BeEquivalentTo(data);
        response.Message.Should().Be("Fetched");
        response.PageNumber.Should().Be(2);
        response.PageSize.Should().Be(10);
        response.TotalRecords.Should().Be(35);
        response.TotalPages.Should().Be(4); // ceil(35/10) = 4
    }

    [Fact]
    public void Create_HasNextPage_WhenNotOnLastPage()
    {
        var response = PagedResponse<int>.Create(new List<int> { 1 }, pageNumber: 1, pageSize: 10, totalRecords: 25);

        response.HasNextPage.Should().BeTrue();
        response.HasPreviousPage.Should().BeFalse();
    }

    [Fact]
    public void Create_HasPreviousPage_WhenNotOnFirstPage()
    {
        var response = PagedResponse<int>.Create(new List<int> { 1 }, pageNumber: 3, pageSize: 10, totalRecords: 25);

        response.HasNextPage.Should().BeFalse(); // page 3 of 3
        response.HasPreviousPage.Should().BeTrue();
    }

    [Fact]
    public void Create_MiddlePage_HasBothNavigation()
    {
        var response = PagedResponse<int>.Create(new List<int> { 1 }, pageNumber: 2, pageSize: 10, totalRecords: 30);

        response.HasNextPage.Should().BeTrue();
        response.HasPreviousPage.Should().BeTrue();
    }

    [Fact]
    public void Create_SinglePage_HasNoNavigation()
    {
        var response = PagedResponse<int>.Create(new List<int> { 1, 2, 3 }, pageNumber: 1, pageSize: 10, totalRecords: 3);

        response.TotalPages.Should().Be(1);
        response.HasNextPage.Should().BeFalse();
        response.HasPreviousPage.Should().BeFalse();
    }

    [Fact]
    public void Create_EmptyData_HasZeroPages()
    {
        var response = PagedResponse<int>.Create(new List<int>(), pageNumber: 1, pageSize: 10, totalRecords: 0);

        response.TotalPages.Should().Be(0);
        response.TotalRecords.Should().Be(0);
        response.HasNextPage.Should().BeFalse();
        response.HasPreviousPage.Should().BeFalse();
    }

    [Theory]
    [InlineData(1, 10, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(11, 10, 2)]
    [InlineData(100, 10, 10)]
    [InlineData(101, 10, 11)]
    [InlineData(1, 1, 1)]
    [InlineData(5, 3, 2)]
    public void Create_CalculatesTotalPagesCorrectly(int totalRecords, int pageSize, int expectedTotalPages)
    {
        var response = PagedResponse<int>.Create(new List<int>(), pageNumber: 1, pageSize: pageSize, totalRecords: totalRecords);

        response.TotalPages.Should().Be(expectedTotalPages);
    }

    #endregion

    #region Error

    [Fact]
    public void Error_SetsErrorFields()
    {
        var response = PagedResponse<string>.Error("Not found", errorCode: "ERR_1002");

        response.Success.Should().BeFalse();
        response.Errors.Should().ContainSingle().Which.Should().Be("Not found");
        response.ErrorCode.Should().Be("ERR_1002");
    }

    [Fact]
    public void Error_SetsDefaultPaginationValues()
    {
        var response = PagedResponse<string>.Error("Error");

        response.Data.Should().NotBeNull();
        response.Data.Should().BeEmpty();
        response.PageNumber.Should().Be(1);
        response.PageSize.Should().Be(10);
        response.TotalPages.Should().Be(0);
        response.TotalRecords.Should().Be(0);
        response.HasNextPage.Should().BeFalse();
        response.HasPreviousPage.Should().BeFalse();
    }

    [Fact]
    public void Error_WithHelpUrl_SetsHelpUrl()
    {
        var response = PagedResponse<string>.Error("Error", helpUrl: "https://help.example.com");

        response.HelpUrl.Should().Be("https://help.example.com");
    }

    #endregion

    #region Inheritance

    [Fact]
    public void PagedResponse_InheritsFromApiResponse()
    {
        var response = PagedResponse<string>.Create(new List<string>(), 1, 10, 0);

        response.Should().BeAssignableTo<ApiResponse<List<string>>>();
    }

    #endregion
}
