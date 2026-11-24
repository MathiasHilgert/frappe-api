using Frappe.Identity.Domain.Owners;

namespace Frappe.Identity.Application.Owners;

/// <summary>The owners. It stages and never saves: the command's unit of work does (UOW-01).</summary>
public interface IOwners
{
    /// <summary>The owner whose principal is <paramref name="principalId"/>, tracked for change, or <see langword="null"/>.</summary>
    Task<Owner?> FindAsync(Guid principalId, CancellationToken cancellationToken);

    /// <summary>
    /// The version of <paramref name="owner"/>'s row as it was read (its <c>xmin</c>), in the form <see cref="OwnerProfile.Version"/> has: what a
    /// caller's <c>If-Match</c> is compared with.
    /// </summary>
    string VersionOf(Owner owner);

    /// <summary>Stages <paramref name="owner"/> to be inserted with the unit of work.</summary>
    void Add(Owner owner);
}
