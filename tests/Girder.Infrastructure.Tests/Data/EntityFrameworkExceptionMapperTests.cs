using System.Net;
using Girder.Abstractions.Diagnostics;
using Girder.Core.Exceptions;
using Girder.Data.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Girder.Infrastructure.Tests.Data;

/// <summary>
/// The persistence failures the exception handler used to name itself, now
/// answered by the EF Core package.
/// </summary>
[Trait("Category", "Unit")]
public class EntityFrameworkExceptionMapperTests
{
    private readonly EntityFrameworkExceptionMapper _sut = new();

    [Fact]
    public void A_concurrency_conflict_becomes_409()
    {
        var mapped = _sut.Map(new DbUpdateConcurrencyException("row changed"));

        mapped.Should().NotBeNull();
        mapped!.Value.Status.Should().Be(HttpStatusCode.Conflict);
        mapped.Value.ErrorCode.Should().Be(ErrorCodes.ConcurrencyConflict);
    }

    [Fact]
    public void A_duplicate_key_becomes_409_with_its_own_code()
    {
        // Distinguishable from a concurrency conflict, because the caller's
        // remedy differs: change the value, do not retry the same write.
        var mapped = _sut.Map(new DbUpdateException(
            "save failed", new Exception("duplicate key value violates unique constraint")));

        mapped!.Value.Status.Should().Be(HttpStatusCode.Conflict);
        mapped.Value.ErrorCode.Should().Be(ErrorCodes.DuplicateKey);
    }

    [Fact]
    public void The_duplicate_check_ignores_case()
    {
        var mapped = _sut.Map(new DbUpdateException(
            "save failed", new Exception("Violation of UNIQUE KEY: Duplicate entry")));

        mapped!.Value.ErrorCode.Should().Be(ErrorCodes.DuplicateKey);
    }

    [Fact]
    public void Any_other_write_failure_becomes_500()
    {
        // Deliberately not 409: telling a caller to retry a write that cannot
        // succeed is worse than admitting the server failed.
        var mapped = _sut.Map(new DbUpdateException("connection lost", new Exception("timeout")));

        mapped!.Value.Status.Should().Be(HttpStatusCode.InternalServerError);
        mapped.Value.ErrorCode.Should().Be(ErrorCodes.DatabaseError);
    }

    [Fact]
    public void A_write_failure_without_an_inner_exception_still_maps()
    {
        _sut.Map(new DbUpdateException("save failed"))!.Value
            .ErrorCode.Should().Be(ErrorCodes.DatabaseError);
    }

    [Theory]
    [InlineData(typeof(System.InvalidOperationException))]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(ArgumentException))]
    public void Anything_that_is_not_a_persistence_failure_is_passed_on(Type exceptionType)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType)!;

        _sut.Map(exception).Should().BeNull("returning null means: not mine");
    }

    [Fact]
    public void No_detail_carries_the_exception_message()
    {
        // The detail reaches the caller. A driver message can contain the
        // connection string, the table, or the value that collided.
        const string secret = "Host=db.internal;Password=hunter2";
        var mapped = _sut.Map(new DbUpdateException(secret, new Exception(secret)));

        mapped!.Value.FallbackDetail.Should().NotContain("hunter2");
        mapped.Value.Title.Should().NotContain("hunter2");
    }
}
