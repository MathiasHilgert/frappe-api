using Frappe.ModuleName.Application.AreaName;
using Frappe.ModuleName.Domain.AreaName;
using Frappe.ModuleName.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.ModuleName.Infrastructure.AreaName;

/// <summary>Stages AreaName on the module's context and never saves: the command's unit of work does (UOW-01).</summary>
internal sealed class EfAreaName(ModuleNameDbContext db) : IAreaName
{
    public Task<EntityName?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<EntityName>().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public void Add(EntityName entity) => db.Set<EntityName>().Add(entity);

    public void Remove(EntityName entity) => db.Set<EntityName>().Remove(entity);
}
