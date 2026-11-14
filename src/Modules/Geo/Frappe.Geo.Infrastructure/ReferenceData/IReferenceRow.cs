using NodaTime;

namespace Frappe.Geo.Infrastructure.ReferenceData;

/// <summary>
/// A row of reference data that other modules keep by its <see cref="Key"/>: it is never deleted, only retired (and
/// revived if its source publishes it again), so a code another module stored never stops resolving.
/// </summary>
internal interface IReferenceRow<in TRow>
{
    string Key { get; }

    Instant? RetiredAt { get; set; }

    /// <summary>Whether this row already holds everything <paramref name="desired"/> holds.</summary>
    bool SameAs(TRow desired);

    /// <summary>Takes every value of <paramref name="desired"/>, keeping this row's identity (and its child rows' keys).</summary>
    void CopyFrom(TRow desired);
}
