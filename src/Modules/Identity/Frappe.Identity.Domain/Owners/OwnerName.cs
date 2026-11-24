using System.Globalization;
using System.Text;
using Frappe.Results;

namespace Frappe.Identity.Domain.Owners;

/// <summary>
/// What an owner is called: a given name and an optional family name. Mails greet with both, so together they stay within
/// <see cref="MaxMailedLength"/> characters and carry nothing that looks like a link, or a code mail would be refused by the mail side and
/// parked (S3), nor a control or format character (Unicode categories Cc and Cf: line
/// breaks, bidi marks, zero-width). It prints nothing of itself (DOM-06).
/// </summary>
public sealed record OwnerName
{
    /// <summary>The most characters the given name, a space and the family name take together.</summary>
    public const int MaxMailedLength = 60;

    private static readonly string[] LinkMarks = ["http", "www.", "://"];

    private OwnerName(string given, string? family)
    {
        Given = given;
        Family = family;
    }

    /// <summary>The given name, trimmed; never empty.</summary>
    public string Given { get; }

    /// <summary>The family name, trimmed; empty when none was given.</summary>
    public string? Family { get; }

    /// <summary>The name as a mail says it: given plus family.</summary>
    public string Mailed => Family is null ? Given : $"{Given} {Family}";

    /// <summary>The name <paramref name="given"/> and <paramref name="family"/> spell, or <see cref="IdentityErrorCodes.NameInvalid"/>.</summary>
    public static Result<OwnerName> Create(string? given, string? family)
    {
        var first = given?.Trim();
        var last = string.IsNullOrWhiteSpace(family) ? null : family.Trim();
        if (string.IsNullOrEmpty(first))
        {
            return Error.InvalidValue(IdentityErrorCodes.NameInvalid);
        }

        var name = new OwnerName(first, last);
        var mailed = name.Mailed;
        return mailed.Length > MaxMailedLength
            || LinkMarks.Any(mark => mailed.Contains(mark, StringComparison.OrdinalIgnoreCase))
            || mailed.EnumerateRunes().Any(IsInvisible)
            ? Error.InvalidValue(IdentityErrorCodes.NameInvalid)
            : name;
    }

    /// <summary>
    /// A control (Cc), format (Cf), line separator (Zl) or paragraph separator (Zp) character: it hides text, breaks a line or reorders what
    /// is around it (bidi marks, zero-width).
    /// </summary>
    private static bool IsInvisible(Rune rune) =>
        Rune.GetUnicodeCategory(rune)
            is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;

    public override string ToString() => "***";
}
