namespace Frappe.Notifications.Application.Kinds;

/// <summary>The bounds payload texts share: what a person is called in a mail must not be a place to put a link (N3).</summary>
public static class PayloadText
{
    /// <summary>The longest name a payload carries.</summary>
    public const int MaxNameLength = 60;

    private static readonly string[] LinkMarks = ["http", "www.", "://"];

    /// <summary>Whether <paramref name="name"/> is short enough and does not look like a link (<c>http</c>, <c>www.</c>, <c>://</c>).</summary>
    public static bool IsPersonName(string? name) =>
        name is not null
        && name.Length <= MaxNameLength
        && !LinkMarks.Any(mark => name.Contains(mark, StringComparison.OrdinalIgnoreCase));
}
