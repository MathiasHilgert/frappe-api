using System.Globalization;
using System.Text;

namespace Frappe.Notifications.Application.Messages;

/// <summary>The bounds payload texts share: what a person is called in a mail must not be a place to put a link (N3).</summary>
public static class PayloadText
{
    /// <summary>The longest name a payload carries.</summary>
    public const int MaxNameLength = 60;

    private static readonly string[] LinkMarks = ["http", "www.", "://"];

    /// <summary>
    /// Whether <paramref name="name"/> is short enough, does not look like a link (<c>http</c>, <c>www.</c>, <c>://</c>) and holds nothing
    /// invisible: no control, format, line or paragraph separator character, the same rule Identity applies to owner names.
    /// </summary>
    public static bool IsPersonName(string? name) =>
        name is not null
        && name.Length <= MaxNameLength
        && !LinkMarks.Any(mark => name.Contains(mark, StringComparison.OrdinalIgnoreCase))
        && !name.EnumerateRunes().Any(IsInvisible);

    private static bool IsInvisible(Rune rune) =>
        Rune.GetUnicodeCategory(rune)
            is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;
}
