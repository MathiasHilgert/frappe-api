using Frappe.Results;

namespace Frappe.Identity.Domain.Credentials;

/// <summary>
/// A password a person chose, valid for its length alone: 10 to 128 characters, no composition rules, never trimmed or changed (S4). It
/// exists only in memory and never prints its text (DOM-06, SEC-07).
/// </summary>
public sealed record Password
{
    /// <summary>The fewest characters a password has.</summary>
    public const int MinLength = 10;

    /// <summary>The most characters a password has; it also bounds what one hash costs.</summary>
    public const int MaxLength = 128;

    private Password(string value) => Value = value;

    /// <summary>The text, as typed. Only a hasher or a breach check reads it.</summary>
    public string Value { get; }

    /// <summary>Whether <paramref name="raw"/> is too long to be any password; such a candidate is refused without being hashed.</summary>
    public static bool IsTooLong(string? raw) => raw is not null && CharacterCount(raw) > MaxLength;

    /// <summary>The password <paramref name="raw"/> spells, or why it is not one.</summary>
    public static Result<Password> Create(string? raw)
    {
        var length = raw is null ? 0 : CharacterCount(raw);
        if (length < MinLength)
        {
            return Error.RuleViolation(IdentityErrorCodes.PasswordTooShort, ("min", MinLength));
        }

        if (length > MaxLength)
        {
            return Error.RuleViolation(IdentityErrorCodes.PasswordTooLong, ("max", MaxLength));
        }

        return new Password(raw!);
    }

    public override string ToString() => "***";

    /// <summary>The characters a person sees: Unicode scalar values, so an emoji counts once.</summary>
    private static int CharacterCount(string text) => text.EnumerateRunes().Count();
}
