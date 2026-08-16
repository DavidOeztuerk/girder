using Girder.Abstractions.Security.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Girder.Abstractions.Security.Authorization;

/// <summary>
/// What a conditional permission is evaluated against: who is asking, about
/// which resource, and the resource itself as the caller supplied it.
/// </summary>
public sealed record PermissionConditionContext(
    string UserId,
    string ResourceType,
    object ResourceData);

/// <summary>
/// Answers the conditions that <see cref="PermissionDefinition.Condition"/>
/// names. Both the wording of a condition and the shape of the resource it
/// reads belong to the application; the library only knows when to ask.
/// </summary>
public interface IPermissionConditions
{
    /// <summary>Whether this condition has been declared at all.</summary>
    bool Knows(string condition);

    /// <summary>
    /// Answers the condition. An undeclared condition is false — a permission
    /// whose condition nobody defined must not be granted.
    /// </summary>
    bool IsSatisfied(string condition, PermissionConditionContext context);
}

/// <summary>
/// An immutable <see cref="IPermissionConditions"/>. Build one with
/// <see cref="PermissionConditionsBuilder"/>.
/// </summary>
public sealed class PermissionConditions : IPermissionConditions
{
    private readonly IReadOnlyDictionary<string, Func<PermissionConditionContext, bool>> _conditions;

    internal PermissionConditions(
        IReadOnlyDictionary<string, Func<PermissionConditionContext, bool>> conditions)
    {
        _conditions = conditions;
    }

    /// <summary>
    /// Knows no condition, so every conditional permission is denied. This is
    /// the default: a library that guessed here would grant access on a
    /// sentence it invented itself.
    /// </summary>
    public static IPermissionConditions None { get; } =
        new PermissionConditions(new Dictionary<string, Func<PermissionConditionContext, bool>>());

    /// <inheritdoc />
    public bool Knows(string condition) => _conditions.ContainsKey(condition);

    /// <inheritdoc />
    public bool IsSatisfied(string condition, PermissionConditionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return _conditions.TryGetValue(condition, out var predicate) && predicate(context);
    }
}

/// <summary>
/// Declares which conditions exist and what each one means.
/// </summary>
/// <example>
/// <code>
/// services.AddPermissionConditions(c => c
///     .Condition("user is the author", ctx =>
///         ctx.ResourceData is Posting p &amp;&amp; p.AuthorId == ctx.UserId)
///     .Condition("user was invited", ctx =>
///         ctx.ResourceData is Booking b &amp;&amp; b.InviteeId == ctx.UserId));
/// </code>
/// </example>
public sealed class PermissionConditionsBuilder
{
    // Conditions are written out in a permission definition and matched as
    // written — a differently spelled condition is a different condition.
    private readonly Dictionary<string, Func<PermissionConditionContext, bool>> _conditions =
        new(StringComparer.Ordinal);

    /// <summary>Declares what <paramref name="condition"/> means.</summary>
    public PermissionConditionsBuilder Condition(
        string condition,
        Func<PermissionConditionContext, bool> predicate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(condition);
        ArgumentNullException.ThrowIfNull(predicate);

        if (_conditions.ContainsKey(condition))
        {
            throw new ArgumentException(
                $"'{condition}' is already defined. Two answers to one condition would "
                + "make access depend on registration order.",
                nameof(condition));
        }

        _conditions[condition] = predicate;
        return this;
    }

    public IPermissionConditions Build() =>
        new PermissionConditions(
            new Dictionary<string, Func<PermissionConditionContext, bool>>(
                _conditions, StringComparer.Ordinal));
}

public static class PermissionConditionsExtensions
{
    /// <summary>Registers the application's permission conditions.</summary>
    public static IServiceCollection AddPermissionConditions(
        this IServiceCollection services,
        Action<PermissionConditionsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new PermissionConditionsBuilder();
        configure(builder);

        return services.AddSingleton(builder.Build());
    }
}
