using System.Reflection;
using Frappe.Messaging.Abstractions;

namespace Frappe.Notifications.IntegrationTests;

/// <summary>Sets the id a consumer would have filled, for tests that stand in for it.</summary>
internal static class CurrentMessageTesting
{
    public static void WithId(this CurrentMessage current, Guid id) =>
        typeof(CurrentMessage).GetProperty(nameof(CurrentMessage.Id), BindingFlags.Public | BindingFlags.Instance)!.SetValue(current, id);
}
