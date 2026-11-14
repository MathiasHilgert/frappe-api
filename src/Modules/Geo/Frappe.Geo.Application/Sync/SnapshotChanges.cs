using Frappe.Geo.Domain.ReferenceData;

namespace Frappe.Geo.Application.Sync;

/// <summary>What applying a snapshot changed, as the plans of every table it touched, added up.</summary>
public sealed record SnapshotChanges(int Inserted, int Updated, int Retired, int Revived, bool RetiresTooMuch)
{
    /// <summary>The manual overrides the source changed something under: they still win, and the run records them.</summary>
    public IReadOnlyList<OverrideConflict> Conflicts { get; init; } = [];

    public static readonly SnapshotChanges None = new(0, 0, 0, 0, RetiresTooMuch: false);

    public bool Any => Inserted + Updated + Retired + Revived > 0;

    /// <summary>The changes of several tables' plans; suspicious when any one of them retires too much.</summary>
    public static SnapshotChanges Of(params ReferenceDataPlan[] plans)
    {
        ArgumentNullException.ThrowIfNull(plans);
        return new(
            plans.Sum(p => p.Insert.Count),
            plans.Sum(p => p.Update.Count),
            plans.Sum(p => p.Retire.Count),
            plans.Sum(p => p.Revive.Count),
            plans.Any(p => p.RetiresTooMuch));
    }
}
