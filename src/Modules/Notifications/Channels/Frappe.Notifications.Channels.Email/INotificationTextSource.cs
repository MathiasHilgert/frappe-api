using Frappe.Localization;

namespace Frappe.Notifications.Channels.Email;

/// <summary>
/// Where the ICU texts of notification kinds come from (S6). Embedded JSON today; the seam keeps a database source possible later without
/// touching a kind.
/// </summary>
public interface INotificationTextSource
{
    /// <summary>The patterns of <paramref name="kind"/> in exactly <paramref name="language"/>, by key; <see langword="null"/> when none.</summary>
    IReadOnlyDictionary<string, string>? Find(string kind, LanguageTag language);
}
