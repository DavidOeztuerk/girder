using FluentAssertions;
using Girder.Core.Logging;

namespace Girder.Core.Tests.Logging;

/// <summary>
/// What <see cref="Shape.Of"/> writes about an object, and what it must
/// survive: a command carrying <see cref="ReadOnlyMemory{T}"/> used to throw
/// before any of the shape was taken, because reflection cannot invoke a
/// getter that returns a ref struct.
/// </summary>
[Trait("Category", "Unit")]
public class ShapeTests
{
    /// <summary>
    /// Distinctive bytes so a leak cannot hide inside a type name like
    /// <c>int32</c>.
    /// </summary>
    private static readonly byte[] SentinelBytes = [201, 202, 203];

    [Fact]
    public void ReadOnlyMemory_does_not_throw_and_does_not_dump_bytes()
    {
        var taking = () => Shape.Of(new { Inhalt = new ReadOnlyMemory<byte>(SentinelBytes) });

        taking.Should().NotThrow();

        var shape = taking();
        shape.Should().Contain("Inhalt");
        shape.Should().Contain("Length");
        shape.Should().NotContain("201").And.NotContain("202").And.NotContain("203");
    }

    [Fact]
    public void A_ReadOnlySpan_property_does_not_throw()
    {
        var taking = () => Shape.Of(new SpanHolder(SentinelBytes));

        taking.Should().NotThrow();
        taking().Should().NotContain("201").And.NotContain("202").And.NotContain("203");
    }

    [Fact]
    public void An_upload_command_is_named_and_sized_not_dumped()
    {
        var command = new Upload("lebenslauf.png", new ReadOnlyMemory<byte>(SentinelBytes));

        var taking = () => Shape.Of(command);
        taking.Should().NotThrow();

        var shape = taking();
        shape.Should().Contain("Name").And.Contain("string(14)");
        shape.Should().Contain("Inhalt");
        shape.Should().NotContain("lebenslauf.png");
        shape.Should().NotContain("201").And.NotContain("202").And.NotContain("203");
    }

    [Fact]
    public void A_plain_record_is_still_described()
    {
        var shape = Shape.Of(new { Title = "Termin bei Dr. Weber", Note = (string?)null });

        shape.Should().Contain("Title").And.Contain("Note");
        shape.Should().Contain("string(20)");
        shape.Should().NotContain("Dr. Weber").And.NotContain("Termin");
    }

    [Fact]
    public void A_byte_array_is_counted_not_dumped()
    {
        var shape = Shape.Of(new { Data = SentinelBytes });

        shape.Should().Contain("[3 items]");
        shape.Should().NotContain("201").And.NotContain("202").And.NotContain("203");
    }

    [Fact]
    public void A_getter_that_throws_is_marked_not_fatal()
    {
        var shape = Shape.Of(new ThrowingHolder());

        shape.Should().Contain("Ok").And.Contain("string(6)");
        shape.Should().Contain("Bad").And.Contain("<threw>");
        shape.Should().NotContain("secret");
    }

    private sealed record Upload(string Name, ReadOnlyMemory<byte> Inhalt);

    private sealed class SpanHolder(byte[] bytes)
    {
        public ReadOnlySpan<byte> Span => bytes;
    }

    private sealed class ThrowingHolder
    {
        public string Ok => "secret";
        public string Bad => throw new InvalidOperationException("nope");
    }
}
