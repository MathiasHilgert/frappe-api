using Frappe.Notifications.Application.Preferences;
using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Domain.Preferences;

namespace Frappe.Notifications.UnitTests.Deliveries;

/// <summary>An in-memory preference store: the recipient keys that opted out, what it was asked and what was staged.</summary>
internal sealed class FakePreferences : IPreferenceStore
{
    public HashSet<string> OptedOut { get; } = [];

    /// <summary>The keys of every read, in order.</summary>
    public List<IReadOnlyList<RecipientKey>> Probed { get; } = [];

    public List<Preference> Staged { get; } = [];

    public Task<bool> IsOptedOutAsync(IReadOnlyList<RecipientKey> recipientKeys, NotificationCategory category, CancellationToken cancellationToken)
    {
        Probed.Add(recipientKeys);
        return Task.FromResult(recipientKeys.Any(key => OptedOut.Contains(key.Value)));
    }

    public void Stage(Preference preference)
    {
        ArgumentNullException.ThrowIfNull(preference);
        Staged.Add(preference);
        OptedOut.Add(preference.RecipientKey);
    }
}
