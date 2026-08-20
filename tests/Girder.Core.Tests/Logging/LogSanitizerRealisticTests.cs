using FluentAssertions;
using Girder.Core.Logging;

namespace Girder.Core.Tests.Logging;

/// <summary>
/// What a real command looks like after sanitising.
/// </summary>
/// <remarks>
/// The CQRS logging behaviour writes the whole request at debug level. In
/// production the level filters it out; in development it does not, and then
/// this is the only thing between the log and every registration password.
/// </remarks>
[Trait("Category", "Unit")]
public class LogSanitizerRealisticTests
{
    private readonly LogSanitizer _sanitizer = new();

    private sealed record RegisterCommand(string DisplayName, string Email, string Password);

    private sealed record ProfileCommand(string FirstName, string LastName, string Biography);

    [Fact]
    public void A_password_never_survives()
    {
        var sanitized = Render(new RegisterCommand("Ada Lovelace", "ada@example.com", "hunter2-and-then-some"));

        sanitized.Should().NotContain("hunter2");
    }

    [Fact]
    public void An_email_never_survives()
    {
        var sanitized = Render(new RegisterCommand("Ada Lovelace", "ada@example.com", "hunter2-and-then-some"));

        sanitized.Should().NotContain("ada@example.com");
    }

    /// <summary>
    /// A person's name is personal data too, and it is the field most likely to
    /// be in a command that is logged.
    /// </summary>
    [Fact]
    public void A_display_name_never_survives()
    {
        var sanitized = Render(new RegisterCommand("Ada Lovelace", "ada@example.com", "hunter2-and-then-some"));

        sanitized.Should().NotContain("Ada Lovelace");
    }

    [Fact]
    public void Neither_do_the_parts_of_a_name()
    {
        var sanitized = Render(new ProfileCommand("Ada", "Lovelace", "Worked on the Analytical Engine."));

        sanitized.Should().NotContain("Ada").And.NotContain("Lovelace");
    }

    /// <summary>
    /// An address written inside free text is still an address.
    /// </summary>
    [Fact]
    public void An_email_hidden_in_free_text_is_caught()
    {
        var sanitized = Render(new ProfileCommand("A", "B", "Reach me at ada@example.com any time."));

        sanitized.Should().NotContain("ada@example.com");
    }

    /// <summary>
    /// And the parts that are not about a person stay readable, or the log
    /// stops being worth keeping.
    /// </summary>
    [Fact]
    public void What_is_not_personal_stays_legible()
    {
        var sanitized = Render(new ProfileCommand("A", "B", "Worked on the Analytical Engine."));

        sanitized.Should().Contain("Analytical Engine");
    }

    /// <summary>
    /// Names of software are not names of people.
    /// </summary>
    /// <remarks>
    /// The match is exact, never a substring: redacting anything containing
    /// "name" would take <c>RequestName</c> and <c>ServiceName</c> with it and
    /// leave a log nobody can follow.
    /// </remarks>
    [Fact]
    public void What_is_named_but_not_a_person_stays_readable()
    {
        var sanitized = Render(new DiagnosticContext(
            "RegisterUserCommand", "user-service", "appsettings.json"));

        sanitized.Should().Contain("RegisterUserCommand")
            .And.Contain("user-service")
            .And.Contain("appsettings.json");
    }

    /// <summary>Where someone lives, and what identifies them to a bank.</summary>
    [Fact]
    public void An_address_and_an_account_never_survive()
    {
        var sanitized = Render(new PayoutCommand(
            "Hauptstraße 1", "10115", "Berlin", "DE89370400440532013000"));

        sanitized.Should().NotContain("Hauptstraße")
            .And.NotContain("10115")
            .And.NotContain("Berlin")
            .And.NotContain("DE89370400440532013000");
    }

    private sealed record DiagnosticContext(string RequestName, string ServiceName, string FileName);

    private sealed record PayoutCommand(string Street, string Postcode, string City, string Iban);

    private string Render(object command) =>
        System.Text.Json.JsonSerializer.Serialize(_sanitizer.Sanitize(command));
}
