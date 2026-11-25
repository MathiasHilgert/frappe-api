using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.Application.Contacts;

/// <summary>Makes the keyed hash an address is stored as (S13). Hashing normalizes first, so spellings of one address hash alike.</summary>
public interface IAddressHasher
{
    /// <summary>The hash under the current key.</summary>
    AddressHash Hash(string address);

    /// <summary>The hash under every active key, which is what a lookup probes while keys rotate.</summary>
    IReadOnlyList<AddressHash> HashWithEveryKey(string address);
}
