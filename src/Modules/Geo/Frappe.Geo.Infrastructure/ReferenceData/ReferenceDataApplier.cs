using Frappe.Geo.Domain.ReferenceData;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Frappe.Geo.Infrastructure.ReferenceData;

/// <summary>
/// Carries out a <see cref="ReferenceDataPlan"/> on a table: the domain decides what to insert, update, retire and
/// revive; this only loads the rows and applies the decision. The caller saves, so a whole snapshot lands in one
/// transaction, and checks <see cref="ReferenceDataPlan.RetiresTooMuch"/> before saving.
/// </summary>
internal static class ReferenceDataApplier
{
    /// <summary>Brings the whole table of <typeparamref name="TRow"/> to <paramref name="desired"/>.</summary>
    public static Task<ReferenceDataPlan> ApplyAsync<TRow>(DbContext db, IReadOnlyCollection<TRow> desired, Instant now, CancellationToken cancellationToken)
        where TRow : class, IReferenceRow<TRow>
    {
        ArgumentNullException.ThrowIfNull(db);
        return ApplyAsync(db, db.Set<TRow>(), desired, now, cancellationToken);
    }

    /// <summary>
    /// The same within <paramref name="scope"/> only: rows outside it are neither compared nor retired (one country's
    /// snapshot never retires another country's rows).
    /// </summary>
    public static async Task<ReferenceDataPlan> ApplyAsync<TRow>(DbContext db, IQueryable<TRow> scope, IReadOnlyCollection<TRow> desired, Instant now, CancellationToken cancellationToken)
        where TRow : class, IReferenceRow<TRow>
    {
        ArgumentNullException.ThrowIfNull(db);
        return Execute(db.Set<TRow>(), await scope.ToListAsync(cancellationToken), desired, now);
    }

    /// <summary>The synchronous twin, for EF Core's synchronous seeding entry point.</summary>
    public static ReferenceDataPlan Apply<TRow>(DbContext db, IReadOnlyCollection<TRow> desired, Instant now)
        where TRow : class, IReferenceRow<TRow>
    {
        ArgumentNullException.ThrowIfNull(db);
        return Execute(db.Set<TRow>(), db.Set<TRow>().ToList(), desired, now);
    }

    private static ReferenceDataPlan Execute<TRow>(DbSet<TRow> set, List<TRow> current, IReadOnlyCollection<TRow> desired, Instant now)
        where TRow : class, IReferenceRow<TRow>
    {
        ArgumentNullException.ThrowIfNull(desired);
        var stored = current.ToDictionary(row => row.Key, StringComparer.Ordinal);
        var wanted = desired.ToDictionary(row => row.Key, StringComparer.Ordinal);
        var plan = ReferenceDataPlan.For(
            stored.ToDictionary(entry => entry.Key, entry => entry.Value.RetiredAt is null, StringComparer.Ordinal),
            wanted.Keys,
            key => !stored[key].SameAs(wanted[key]));

        foreach (var key in plan.Insert)
        {
            set.Add(wanted[key]);
        }

        foreach (var key in plan.Update)
        {
            stored[key].CopyFrom(wanted[key]);
        }

        foreach (var key in plan.Revive)
        {
            stored[key].RetiredAt = null;
        }

        foreach (var key in plan.Retire)
        {
            stored[key].RetiredAt = now;
        }

        return plan;
    }

    /// <summary>
    /// Brings a row's child collection to <paramref name="desired"/> in place, by key: existing children are updated,
    /// missing ones removed, new ones added. Replacing the whole list would make EF Core track two children with the
    /// same key.
    /// </summary>
    public static void SyncChildren<TChild, TKey>(List<TChild> current, IEnumerable<TChild> desired, Func<TChild, TKey> keyOf, Action<TChild, TChild> copy)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(current);
        var wanted = desired.ToDictionary(keyOf);
        current.RemoveAll(child => !wanted.ContainsKey(keyOf(child)));
        var kept = current.ToDictionary(keyOf);
        foreach (var (key, child) in wanted)
        {
            if (kept.TryGetValue(key, out var existing))
            {
                copy(existing, child);
            }
            else
            {
                current.Add(child);
            }
        }
    }
}
