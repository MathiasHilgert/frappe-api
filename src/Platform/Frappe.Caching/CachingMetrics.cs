using System.Diagnostics.Metrics;

namespace Frappe.Caching;

/// <summary>
/// Instruments of the shared cache on meter <c>Frappe.Caching</c> (<see cref="MeterName"/>), which hosts export through
/// <c>Frappe.*</c>. Created once through <see cref="IMeterFactory"/>.
/// </summary>
public sealed class CachingMetrics
{
    /// <summary>The meter's name.</summary>
    public const string MeterName = "Frappe.Caching";

    /// <summary>
    /// Counter: delayed invalidations that failed, so a stale copy lives until its entry duration ends. Any sustained
    /// count means the cache or its backplane is failing to evict.
    /// </summary>
    public const string FailedMetric = "frappe.caching.invalidation.failed";

    /// <summary>The unit of <see cref="FailedMetric"/>.</summary>
    public const string FailedUnit = "{invalidation}";

    /// <summary>The description of <see cref="FailedMetric"/>.</summary>
    public const string FailedDescription = "Delayed cache invalidations that failed.";

    private readonly Counter<long> _failed;

    public CachingMetrics(IMeterFactory meters)
    {
        ArgumentNullException.ThrowIfNull(meters);
        _failed = meters.Create(MeterName).CreateCounter<long>(FailedMetric, unit: FailedUnit, description: FailedDescription);
    }

    internal void InvalidationFailed() => _failed.Add(1);
}
