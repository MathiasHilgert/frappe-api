using Frappe.Kernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Frappe.Platform.Persistence;

// Increments Revision once per save on every tracked aggregate root that changed, in its own columns,
// an owned value or a child row, so no mutating method can forget it and switch off optimistic
// concurrency. A new aggregate is inserted at revision 0 and a deleted one is left alone.
internal sealed class AggregateRevisionInterceptor : SaveChangesInterceptor
{
    public static AggregateRevisionInterceptor Instance { get; } = new();

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result
    )
    {
        IncrementChangedRevisions(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        IncrementChangedRevisions(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void IncrementChangedRevisions(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        // SaveChanges detects changes only after the interceptors run.
        context.ChangeTracker.DetectChanges();
        EntityEntry[] tracked = [.. context.ChangeTracker.Entries()];
        foreach (
            EntityEntry changed in tracked.Where(static entry =>
                entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
            )
        )
        {
            if (
                FindRoot(tracked, changed) is
                { State: EntityState.Unchanged or EntityState.Modified } root
            )
            {
                PropertyEntry revision = root.Property(nameof(IAggregateRoot.Revision));
                // From the original value, so a root reached through several changed entries moves by one.
                if (revision.OriginalValue is int original)
                {
                    revision.CurrentValue = original + 1;
                }
            }
        }
    }

    // An owned value climbs its ownership; a child climbs the relationship whose principal holds it in a
    // navigation, so an ID-only reference to another aggregate is never followed.
    // shortcut: only a tracked root is found, so a child saved without loading its root is not covered
    // (guarded writes bump the root themselves); the lookup scans the tracked entries, fine for
    // aggregate-sized saves, index it by key if a save ever tracks thousands of entries.
    private static EntityEntry? FindRoot(EntityEntry[] tracked, EntityEntry entry)
    {
        while (entry.Entity is not IAggregateRoot)
        {
            IForeignKey? parent =
                entry.Metadata.FindOwnership()
                ?? entry
                    .Metadata.GetForeignKeys()
                    .FirstOrDefault(static key => key.PrincipalToDependent is not null);
            if (parent is null || FindPrincipal(tracked, entry, parent) is not { } principal)
            {
                return null;
            }

            entry = principal;
        }

        return entry;
    }

    private static EntityEntry? FindPrincipal(
        EntityEntry[] tracked,
        EntityEntry dependent,
        IForeignKey foreignKey
    )
    {
        // The original value still names the parent of a deleted or orphaned child.
        object?[] key =
        [
            .. foreignKey.Properties.Select(property =>
                dependent.Property(property.Name).OriginalValue
                ?? dependent.Property(property.Name).CurrentValue
            ),
        ];
        return tracked.FirstOrDefault(candidate =>
            foreignKey.PrincipalEntityType.IsAssignableFrom(candidate.Metadata)
            && foreignKey
                .PrincipalKey.Properties.Select(property =>
                    candidate.Property(property.Name).CurrentValue
                )
                .SequenceEqual(key)
        );
    }
}
