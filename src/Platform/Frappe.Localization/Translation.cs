namespace Frappe.Localization;

/// <summary>A text written in one language.</summary>
public sealed record Translation(LanguageTag Language, string Text)
{
    public const int MaxLength = 200;
}
