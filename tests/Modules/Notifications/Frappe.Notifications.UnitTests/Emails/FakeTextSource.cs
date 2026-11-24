using Frappe.Localization;
using Frappe.Notifications.Channels.Email;

namespace Frappe.Notifications.UnitTests.Emails;

/// <summary>Texts held in memory: the languages and keys a test gives.</summary>
internal sealed class FakeTextSource : INotificationTextSource
{
    private readonly Dictionary<(string Kind, string Language), IReadOnlyDictionary<string, string>> _texts = [];

    public FakeTextSource With(string kind, string language, params (string Key, string Pattern)[] patterns)
    {
        _texts[(kind, language)] = patterns.ToDictionary(pair => pair.Key, pair => pair.Pattern);
        return this;
    }

    public IReadOnlyDictionary<string, string>? Find(string kind, LanguageTag language) =>
        _texts.GetValueOrDefault((kind, language.Value));
}
