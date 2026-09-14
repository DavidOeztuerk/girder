using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Noelia.Contracts;

namespace Noelia.Core.Tests.Contracts;

public sealed class PagedRequestTests
{
    [Theory]
    [InlineData(0, 12, "PageNumber")]
    [InlineData(1, 0, "PageSize")]
    [InlineData(1, 101, "PageSize")]
    public void Positional_record_parameters_are_validated_as_properties(
        int pageNumber,
        int pageSize,
        string member)
    {
        var request = new PagedRequest(pageNumber, pageSize);
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(request, new ValidationContext(request), results, true)
            .Should().BeFalse();
        results.Should().ContainSingle(result => result.MemberNames.Contains(member));
    }
}
