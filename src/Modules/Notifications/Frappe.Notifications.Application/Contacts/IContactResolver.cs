using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.Application.Contacts;

/// <summary>
/// Turns the references of one kind (<c>account</c>, <c>customer</c>) into contacts. A plug-in contributes one per kind it owns, so the
/// core never learns where addresses live. None is registered in v1 (S3).
/// </summary>
public interface IContactResolver
{
    /// <summary>The reference kind this resolver answers for.</summary>
    string ReferenceKind { get; }

    /// <summary>The contacts of the reference, the preferred first; none when the reference is unknown.</summary>
    Task<IReadOnlyList<ContactRecipient>> ResolveAsync(string id, CancellationToken cancellationToken);
}
