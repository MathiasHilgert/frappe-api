using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.Application.Contacts;

/// <summary>Sends each reference to the resolver of its kind; a kind no resolver answers for resolves to nobody.</summary>
public sealed class ContactDirectory(IEnumerable<IContactResolver> resolvers) : IContactDirectory
{
    private readonly Dictionary<string, IContactResolver> _byKind =
        resolvers.ToDictionary(resolver => resolver.ReferenceKind, StringComparer.Ordinal);

    public async Task<IReadOnlyList<ContactRecipient>> ResolveAsync(ReferenceRecipient reference, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return _byKind.TryGetValue(reference.Kind, out var resolver)
            ? await resolver.ResolveAsync(reference.Id, cancellationToken)
            : [];
    }
}
