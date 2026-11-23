using System.Diagnostics.Metrics;

namespace Frappe.Http;

/// <summary>
/// Instruments of the shared rate limit on meter <c>Frappe.Http</c> (<see cref="MeterName"/>), which hosts export through
/// <c>Frappe.*</c>: how long the store takes, and how many requests each replica counted on its own instead. Created once
/// through <see cref="IMeterFactory"/>.
/// </summary>
public sealed class RateLimitMetrics
{
    /// <summary>The meter's name.</summary>
    public const string MeterName = "Frappe.Http";

    /// <summary>Histogram: how long the shared store took to count a request, by outcome.</summary>
    public const string StoreDurationMetric = "frappe.http.rate_limit.store.duration";

    /// <summary>Counter: requests a replica counted in its own window instead of the shared store, by reason.</summary>
    public const string CountedLocallyMetric = "frappe.http.rate_limit.counted_locally";

    /// <summary>The store call's outcome: <see cref="Ok"/> or <see cref="Failed"/> (an error or the timeout).</summary>
    public const string OutcomeTag = "frappe.http.rate_limit.store.outcome";

    /// <summary>Why a request was counted locally: <see cref="Failed"/>, or <see cref="Skipped"/> while the breaker is open.</summary>
    public const string ReasonTag = "frappe.http.rate_limit.reason";

    public const string Ok = "ok";
    public const string Failed = "failed";
    public const string Skipped = "skipped";

    private readonly Histogram<double> _storeDuration;
    private readonly Counter<long> _countedLocally;

    public RateLimitMetrics(IMeterFactory meters)
    {
        ArgumentNullException.ThrowIfNull(meters);
        var meter = meters.Create(MeterName);
        _storeDuration = meter.CreateHistogram<double>(
            StoreDurationMetric,
            unit: "s",
            description: "How long the shared rate limit store took to count a request.");
        _countedLocally = meter.CreateCounter<long>(
            CountedLocallyMetric,
            unit: "{request}",
            description: "Requests a replica counted in its own window because the shared store failed or was skipped.");
    }

    internal void StoreAnswered(TimeSpan duration, bool ok) =>
        _storeDuration.Record(duration.TotalSeconds, new KeyValuePair<string, object?>(OutcomeTag, ok ? Ok : Failed));

    internal void CountedLocally(string reason) =>
        _countedLocally.Add(1, new KeyValuePair<string, object?>(ReasonTag, reason));
}
