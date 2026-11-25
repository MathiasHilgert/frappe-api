using Frappe.ModuleName.Domain.AreaName;

namespace Frappe.ModuleName.Application.AreaName;

/// <summary>The AreaName commands change (CQRS-08). It stages and never saves: the command's unit of work does (UOW-01).</summary>
public interface IAreaName
{
    /// <summary>The EntityName <paramref name="id"/>, tracked for change, or <see langword="null"/> when there is none.</summary>
    Task<EntityName?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Stages <paramref name="entity"/> to be inserted with the unit of work.</summary>
    void Add(EntityName entity);

    /// <summary>Stages <paramref name="entity"/> to be deleted with the unit of work (DATA-08: a real delete).</summary>
    void Remove(EntityName entity);
}
