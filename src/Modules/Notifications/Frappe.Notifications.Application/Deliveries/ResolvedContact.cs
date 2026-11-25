using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.Application.Deliveries;

/// <summary>A way to reach the recipient, with the key the recipient is known by and the hash of the address.</summary>
/// <param name="Contact">Where to reach them.</param>
/// <param name="Key">The recipient's key: the reference's for a resolved reference, the address hash's for an explicit contact.</param>
/// <param name="Hash">The keyed hash of the contact's address.</param>
public sealed record ResolvedContact(ContactRecipient Contact, RecipientKey Key, AddressHash Hash);
