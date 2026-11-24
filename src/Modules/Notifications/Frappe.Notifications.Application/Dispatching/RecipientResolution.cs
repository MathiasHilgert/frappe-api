using Frappe.Notifications.Application.Contacts;
using Frappe.Notifications.Application.Kinds;
using Frappe.Notifications.Application.Ports;
using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.Application.Dispatching;

/// <summary>Pipeline step 1: turns the kind's recipient into contacts, asking the directory for a reference and hashing addresses (S3).</summary>
public sealed class RecipientResolution(IContactDirectory directory, IAddressHasher hasher)
{
    public async Task<Resolution> ResolveAsync(NotificationKind kind, object payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(kind);
        switch (kind.RecipientOf(payload))
        {
            case ContactRecipient contact:
                var hash = hasher.Hash(contact.Address);
                return new Resolution.Reachable([new ResolvedContact(contact, RecipientKey.Of(hash), hash)]);
            case ReferenceRecipient reference:
                var key = RecipientKey.Of(reference);
                var contacts = await directory.ResolveAsync(reference, cancellationToken);
                return contacts.Count == 0
                    ? new Resolution.Unresolved(key)
                    : new Resolution.Reachable([.. contacts.Select(contact => new ResolvedContact(contact, key, hasher.Hash(contact.Address)))]);
            default:
                throw new System.Diagnostics.UnreachableException($"Unknown recipient {kind.RecipientOf(payload)}.");
        }
    }
}
