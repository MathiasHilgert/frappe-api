using Frappe.Localization;

namespace Frappe.Notifications.Channels.Email;

/// <summary>The languages the notification texts are written in (S8): every kind has every one of them.</summary>
public static class NotificationLanguages
{
    /// <summary>Argentine Spanish, with voseo: the language served when the recipient's is unknown or not one of ours.</summary>
    public static readonly LanguageTag Default = LanguageTag.Create("es-AR").Value;

    /// <summary>Every language a kind is written in.</summary>
    public static IReadOnlyList<LanguageTag> All { get; } = [Default, .. new[] { "es", "en", "pt-BR" }.Select(tag => LanguageTag.Create(tag).Value)];

    /// <summary>The language a payload names, or <see langword="null"/> when it names none or a malformed one (the default is served).</summary>
    public static LanguageTag? Parse(string? tag)
    {
        var parsed = LanguageTag.Create(tag);
        return parsed.IsSuccess ? parsed.Value : null;
    }
}
