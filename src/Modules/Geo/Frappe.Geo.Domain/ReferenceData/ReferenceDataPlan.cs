namespace Frappe.Geo.Domain.ReferenceData;

/// <summary>
/// How a set of reference data (countries, currencies, territory) moves to a new snapshot of its source. Other modules
/// keep these records by key, so the rules are the business's, not the database's:
/// <list type="bullet">
/// <item>a record is never deleted: a key the snapshot no longer has is <see cref="Retire">retired</see> and keeps
/// resolving by key;</item>
/// <item>a retired key the snapshot has again is <see cref="Revive">revived</see>;</item>
/// <item>a snapshot that would retire more than <see cref="MaxRetiredShare"/> of the active records is
/// <see cref="RetiresTooMuch">suspicious</see> (a truncated download) and must not be applied.</item>
/// </list>
/// </summary>
public sealed record ReferenceDataPlan(
    IReadOnlyList<string> Insert,
    IReadOnlyList<string> Update,
    IReadOnlyList<string> Retire,
    IReadOnlyList<string> Revive,
    int ActiveBefore)
{
    /// <summary>The share of the active records one snapshot may retire at most.</summary>
    public const double MaxRetiredShare = 0.05;

    public bool RetiresTooMuch => ActiveBefore > 0 && Retire.Count > ActiveBefore * MaxRetiredShare;

    public bool ChangesAnything => Insert.Count + Update.Count + Retire.Count + Revive.Count > 0;

    /// <summary>
    /// The plan from the <paramref name="stored"/> records (key → whether it is active) to the <paramref name="desired"/>
    /// keys, where <paramref name="differs"/> says whether a stored key's values differ from the snapshot's.
    /// </summary>
    public static ReferenceDataPlan For(IReadOnlyDictionary<string, bool> stored, IEnumerable<string> desired, Func<string, bool> differs)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(differs);

        var wanted = desired.ToHashSet(StringComparer.Ordinal);
        var insert = wanted.Where(key => !stored.ContainsKey(key));
        var kept = wanted.Where(stored.ContainsKey).ToArray();
        var revive = kept.Where(key => !stored[key]);
        var update = kept.Where(differs);
        var retire = stored.Where(entry => entry.Value && !wanted.Contains(entry.Key)).Select(entry => entry.Key);
        return new ReferenceDataPlan(Sorted(insert), Sorted(update), Sorted(retire), Sorted(revive), stored.Count(entry => entry.Value));
    }

    private static string[] Sorted(IEnumerable<string> keys) => [.. keys.Order(StringComparer.Ordinal)];
}
