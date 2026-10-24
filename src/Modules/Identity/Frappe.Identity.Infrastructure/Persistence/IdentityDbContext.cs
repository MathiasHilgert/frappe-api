using Frappe.Identity.Domain.Principals;
using Frappe.Modularity;
using Frappe.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Frappe.Identity.Infrastructure.Persistence;

/// <summary>
/// A principal's children live in their own tables, so changing only a session or a code would never touch the
/// principals row and its <c>xmin</c> concurrency token. Before every save, each principal with a changed child is
/// marked modified too: the aggregate is then saved as one unit, and a concurrent writer of the same aggregate fails
/// with <see cref="DbUpdateConcurrencyException"/> instead of being silently overwritten.
/// </summary>
[InlinedByWolverine]
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : ModuleDbContext(options, IdentityModule.Schema)
{
    public DbSet<Principal> Principals => Set<Principal>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        TouchChangedAggregates();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        TouchChangedAggregates();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void TouchChangedAggregates()
    {
        ChangeTracker.DetectChanges();
        var changedRoots = ChangeTracker.Entries()
            .Where(e => e.Metadata.IsOwned() && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(PrincipalIdOf)
            .ToHashSet();

        foreach (var root in ChangeTracker.Entries<Principal>())
        {
            if (root.State == EntityState.Unchanged && changedRoots.Contains(root.Entity.Id))
                // xmin cannot be written; rewriting an unchanged column is what makes PostgreSQL issue a new one.
                root.Property(p => p.RegisteredAt).IsModified = true;
        }
    }

    private static Guid PrincipalIdOf(EntityEntry owned) =>
        (Guid)owned.Property(PrincipalConfiguration.PrincipalIdColumn).CurrentValue!;
}
