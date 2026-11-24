using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.Application.Contacts;

/// <summary>Finds the contacts of a recipient reference at send time (S3).</summary>
public interface IContactDirectory
{
    /// <summary>The contacts of the reference, the preferred first; none means the reference is unresolvable.</summary>
    Task<IReadOnlyList<ContactRecipient>> ResolveAsync(ReferenceRecipient reference, CancellationToken cancellationToken);
}
