using System.Globalization;
using Frappe.ModuleName.Application.AreaName;
using Frappe.ModuleName.Domain.AreaName;
using Frappe.ModuleName.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.ModuleName.Infrastructure.AreaName;

/// <summary>Reads AreaName untracked, straight into the view a query answers with (CQRS-08), in one statement.</summary>
internal sealed class EfEntityNameReads(ModuleNameDbContext db) : IEntityNameReads
{
    public async Task<EntityNameView?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await db.Set<EntityName>().AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new { x.Id, x.CreatedAt, Version = EF.Property<uint>(x, "xmin") })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : new EntityNameView(row.Id, row.CreatedAt, row.Version.ToString(CultureInfo.InvariantCulture));
    }

    public async Task<IReadOnlyList<EntityNameView>> ListAsync(int limit, Guid? after, CancellationToken cancellationToken)
    {
        var rows = await db.Set<EntityName>().AsNoTracking()
            .Where(x => after == null || x.Id > after)
            .OrderBy(x => x.Id)
            .Take(limit)
            .Select(x => new { x.Id, x.CreatedAt, Version = EF.Property<uint>(x, "xmin") })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(row => new EntityNameView(row.Id, row.CreatedAt, row.Version.ToString(CultureInfo.InvariantCulture)))];
    }
}
