using Frappe.Identity.Domain.Owners;

namespace Frappe.Identity.Application.Owners;

/// <summary>The owners. It stages and never saves: the command's unit of work does (UOW-01).</summary>
public interface IOwners
{
    /// <summary>The owner whose principal is <paramref name="principalId"/>, tracked for change, or <see langword="null"/>.</summary>
    Task<Owner?> FindAsync(Guid principalId, CancellationToken cancellationToken);

    /// <summary>Stages <paramref name="owner"/> to be inserted with the unit of work.</summary>
    void Add(Owner owner);
}
