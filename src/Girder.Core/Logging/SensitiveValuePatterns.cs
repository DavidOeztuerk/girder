using System.Text.RegularExpressions;

namespace Girder.Core.Logging;

/// <summary>
/// Values that must not reach a log wherever they appear, whatever the field
/// around them is called.
/// </summary>
/// <remarks>
/// Field names cannot catch what a person types. A todo titled "reach me at
/// ada@example.com" carries an address in a field called <c>title</c>, and no
/// list of names will ever cover that.
/// <para>
/// Deliberately few. Every pattern here is a shape that is unmistakable in
/// arbitrary text; anything looser would redact ordinary words and leave a log
/// nobody trusts.
/// </para>
/// </remarks>
public static partial class SensitiveValuePatterns
{
    /// <summary>An email address anywhere in a string.</summary>
    [GeneratedRegex(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}", RegexOptions.Compiled)]
    public static partial Regex EmailAddress();

    /// <summary>
    /// A payment card number, with or without the spaces people type.
    /// </summary>
    [GeneratedRegex(@"\b(?:\d[ -]*?){13,19}\b", RegexOptions.Compiled)]
    public static partial Regex CardNumber();

    /// <summary>A US social security number.</summary>
    [GeneratedRegex(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled)]
    public static partial Regex SocialSecurityNumber();

    /// <summary>An IBAN.</summary>
    [GeneratedRegex(@"\b[A-Z]{2}\d{2}[A-Z0-9]{11,30}\b", RegexOptions.Compiled)]
    public static partial Regex Iban();

    /// <summary>What every pattern replaces the value with.</summary>
    public const string Masked = "[REDACTED]";

    /// <summary>
    /// Masks every pattern in <paramref name="text"/>, leaving the rest as it
    /// is.
    /// </summary>
    public static string MaskAll(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        text = EmailAddress().Replace(text, Masked);
        text = Iban().Replace(text, Masked);
        text = SocialSecurityNumber().Replace(text, Masked);

        return CardNumber().Replace(text, Masked);
    }
}
