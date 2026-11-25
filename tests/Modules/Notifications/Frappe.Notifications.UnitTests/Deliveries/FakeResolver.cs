using Frappe.Notifications.Application.Contacts;
using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.UnitTests.Deliveries;

/// <summary>A resolver that knows the references of one kind and their contacts.</summary>
internal sealed class FakeResolver(string kind, Dictionary<string, ContactRecipient[]> contacts) : IContactResolver
{
    public string ReferenceKind { get; } = kind;

    public Task<IReadOnlyList<ContactRecipient>> ResolveAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ContactRecipient>>(contacts.TryGetValue(id, out var found) ? found : []);
}
