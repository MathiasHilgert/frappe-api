using Frappe.Notifications.Application.Ports;
using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.UnitTests.Dispatching;

/// <summary>An in-memory preference store: the recipient keys that opted out.</summary>
internal sealed class FakePreferences : IPreferenceStore
{
    public HashSet<string> OptedOut { get; } = [];

    public Task<bool> IsOptedOutAsync(RecipientKey recipientKey, NotificationCategory category, CancellationToken cancellationToken) =>
        Task.FromResult(OptedOut.Contains(recipientKey.Value));
}
