using System.Net;

namespace Girder.Infrastructure.Communication;

/// <summary>
/// What a service answered: the status it answered with, and the value if it
/// carried one.
/// </summary>
/// <remarks>
/// A status code is an answer. Reducing everything that is not a success to
/// <c>null</c> makes "the user does not exist", "the user exists and the field
/// is empty" and "the far service is broken" indistinguishable, and the caller
/// then has to guess which it was — usually by retrying, which is wrong for two
/// of the three.
/// </remarks>
/// <typeparam name="T">The value the endpoint returns when it succeeds.</typeparam>
public sealed record ServiceResponse<T>(HttpStatusCode Status, T? Value, string? Body)
    where T : class
{
    /// <summary>Whether the far service considered the call successful.</summary>
    public bool IsSuccess => (int)Status is >= 200 and < 300;

    /// <summary>
    /// The raw body. Present on failure, where the far service usually says
    /// what was wrong; on success the parsed <see cref="Value"/> is the useful
    /// form.
    /// </summary>
    public string? Body { get; } = Body;

    /// <summary>The answer, if it succeeded and carried one; otherwise <c>null</c>.</summary>
    public T? Value { get; } = Value;
}
