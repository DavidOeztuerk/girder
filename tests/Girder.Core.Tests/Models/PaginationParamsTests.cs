using Girder.Application.Models;
using FluentAssertions;

namespace Shared.Tests.Models;

public class PaginationParamsTests
{
    [Fact]
    public void Defaults_PageNumber1_PageSize12()
    {
        var parms = new PaginationParams();

        parms.PageNumber.Should().Be(1);
        parms.PageSize.Should().Be(12);
    }

    [Fact]
    public void PageSize_UnderMax_AcceptsValue()
    {
        var parms = new PaginationParams { PageSize = 50 };

        parms.PageSize.Should().Be(50);
    }

    [Fact]
    public void PageSize_ExceedsMax_ClampedTo96()
    {
        var parms = new PaginationParams { PageSize = 200 };

        parms.PageSize.Should().Be(96);
    }

    [Fact]
    public void PageSize_ExactlyMax_Accepted()
    {
        var parms = new PaginationParams { PageSize = 96 };

        parms.PageSize.Should().Be(96);
    }

    [Theory]
    [InlineData(1, 10, 0)]
    [InlineData(2, 10, 10)]
    [InlineData(3, 10, 20)]
    [InlineData(1, 25, 0)]
    [InlineData(5, 25, 100)]
    public void Skip_CalculatesCorrectOffset(int pageNumber, int pageSize, int expectedSkip)
    {
        var parms = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };

        parms.Skip.Should().Be(expectedSkip);
    }
}
