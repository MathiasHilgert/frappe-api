using Frappe.Identity.Domain.Principals;

namespace Frappe.Identity.Application.Principals;

/// <summary>The principals. It stages and never saves: the command's unit of work does (UOW-01).</summary>
public interface IPrincipals
{
    /// <summary>The principal <paramref name="id"/>, tracked for change, or <see langword="null"/>.</summary>
    Task<Principal?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Stages <paramref name="principal"/> to be inserted with the unit of work.</summary>
    void Add(Principal principal);

    /// <summary>Stages <paramref name="principal"/> to be deleted with the unit of work, and with it everything that belongs to it.</summary>
    void Remove(Principal principal);
}
