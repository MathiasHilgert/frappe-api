using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Frappe.Geo.Application;

/// <summary>
/// Geo's business metrics on meter <c>Frappe.Geo</c> (<see cref="MeterName"/>), which every host exports through the
/// platform's <c>Frappe.*</c> wildcard. Created once through <see cref="IMeterFactory"/>. Tags carry country codes,
/// source ids, scopes and outcomes only: never what a user typed, a subdivision or anything personal (S15).
/// </summary>
public sealed class GeoMetrics
{
    /// <summary>The meter every Geo instrument belongs to.</summary>
    public const string MeterName = "Frappe.Geo";

    /// <summary>Counter: questions answered "not covered" (no calendar or no business-day rules), by country.</summary>
    public const string NotCoveredMetric = "frappe.geo.holidays.not_covered";

    /// <summary>Counter: sync runs, by source and outcome.</summary>
    public const string SyncRunsMetric = "frappe.geo.sync.runs";

    /// <summary>Counter: imported holidays a sync changed under a manual override (the override still wins), by source.</summary>
    public const string OverrideConflictsMetric = "frappe.geo.sync.override_conflicts";

    /// <summary>Observable gauge: seconds since a source's last successful run (applied or unchanged), by source.</summary>
    public const string LastSuccessAgeMetric = "frappe.geo.sync.last_success.age";

    /// <summary>Observable gauge: the last year Geo holds a country-level calendar for, by country and scope.</summary>
    public const string CoveredThroughMetric = "frappe.geo.holidays.covered_through";

    /// <summary>Counter: calls to the address provider, by provider, operation and outcome (<c>ok</c>, <c>not_found</c> or <c>unavailable</c>).</summary>
    public const string AddressRequestsMetric = "frappe.geo.addresses.provider.requests";

    /// <summary>Histogram: how long a call to the address provider took, in seconds, by provider, operation and outcome.</summary>
    public const string AddressDurationMetric = "frappe.geo.addresses.provider.duration";

    /// <summary>
    /// Counter: the estimated cost of the address provider's answered calls, in US dollars, by provider and operation, from
    /// the list prices the provider declares (<see cref="Addresses.IAddressProvider.EstimatedCostUsd"/>).
    /// </summary>
    public const string AddressCostMetric = "frappe.geo.addresses.provider.estimated_cost";

    /// <summary>Counter: resolved addresses whose country or subdivision Geo could not map to an ISO code, by country and part.</summary>
    public const string UnmappedAddressesMetric = "frappe.geo.addresses.unmapped";

    /// <summary>
    /// Counter: search sessions started, by provider: the first answered suggestion of a session. Abandoned sessions are
    /// started minus <see cref="AddressSessionsCompletedMetric"/>, over windows much longer than the session lifetime.
    /// </summary>
    public const string AddressSessionsStartedMetric = "frappe.geo.addresses.sessions.started";

    /// <summary>Counter: started search sessions closed by an answered resolution within the provider's session lifetime, by provider.</summary>
    public const string AddressSessionsCompletedMetric = "frappe.geo.addresses.sessions.completed";

    /// <summary>The address provider's name, for example <c>google</c>; <c>none</c> when no provider is configured.</summary>
    public const string AddressProviderTag = "frappe.geo.address.provider";

    /// <summary>The address provider's operation: <c>suggest</c> or <c>resolve</c>.</summary>
    public const string AddressOperationTag = "frappe.geo.address.operation";

    /// <summary>The part of an address Geo could not map: <c>country</c> or <c>subdivision</c>.</summary>
    public const string AddressPartTag = "frappe.geo.address.part";

    /// <summary>ISO 3166-1 alpha-2 country code, for example <c>AR</c>.</summary>
    public const string CountryTag = "frappe.geo.country";

    /// <summary>The stable error code of a "not covered" answer: <c>geo.holidays.not_covered</c> or <c>geo.business_days.not_covered</c>.</summary>
    public const string ErrorCodeTag = "frappe.error.code";

    /// <summary>A source's id, for example <c>ar-official-holidays</c>.</summary>
    public const string SourceTag = "frappe.geo.source";

    /// <summary>How a sync run ended: <c>applied</c>, <c>unchanged</c>, <c>not_published</c>, <c>refused</c> or <c>failed</c>.</summary>
    public const string OutcomeTag = "frappe.outcome";

    /// <summary>A calendar's scope: <c>general</c> or a sector such as <c>banking</c>.</summary>
    public const string ScopeTag = "frappe.geo.calendar.scope";

    private readonly Counter<long> _notCovered;
    private readonly Counter<long> _syncRuns;
    private readonly Counter<long> _overrideConflicts;
    private readonly Counter<long> _addressRequests;
    private readonly Histogram<double> _addressDuration;
    private readonly Counter<double> _addressCost;
    private readonly Counter<long> _unmappedAddresses;
    private readonly Counter<long> _addressSessionsStarted;
    private readonly Counter<long> _addressSessionsCompleted;

    public GeoMetrics(IMeterFactory meters)
    {
        ArgumentNullException.ThrowIfNull(meters);
        Meter = meters.Create(MeterName);
        _notCovered = Meter.CreateCounter<long>(
            NotCoveredMetric,
            unit: "{request}",
            description: "Questions about holidays or business days answered 'not covered', by country.");
        _syncRuns = Meter.CreateCounter<long>(
            SyncRunsMetric,
            unit: "{run}",
            description: "Reference data sync runs, by source and outcome.");
        _overrideConflicts = Meter.CreateCounter<long>(
            OverrideConflictsMetric,
            unit: "{conflict}",
            description: "Imported holidays a sync changed under a manual override, which still wins.");
        _addressRequests = Meter.CreateCounter<long>(
            AddressRequestsMetric,
            unit: "{request}",
            description: "Calls to the address provider, by provider, operation and outcome.");
        _addressDuration = Meter.CreateHistogram<double>(
            AddressDurationMetric,
            unit: "s",
            description: "Duration of the calls to the address provider, by provider, operation and outcome.");
        _addressCost = Meter.CreateCounter<double>(
            AddressCostMetric,
            unit: "USD",
            description: "Estimated cost of the address provider's answered calls, from its list prices.");
        _unmappedAddresses = Meter.CreateCounter<long>(
            UnmappedAddressesMetric,
            unit: "{address}",
            description: "Resolved addresses whose country or subdivision could not be mapped to an ISO code.");
        _addressSessionsStarted = Meter.CreateCounter<long>(
            AddressSessionsStartedMetric,
            unit: "{session}",
            description: "Address search sessions started: the first answered suggestion of a session.");
        _addressSessionsCompleted = Meter.CreateCounter<long>(
            AddressSessionsCompletedMetric,
            unit: "{session}",
            description: "Started address search sessions closed by an answered resolution within the session lifetime.");
    }

    /// <summary>The <c>Frappe.Geo</c> meter, for instruments declared next to the data they observe.</summary>
    public Meter Meter { get; }

    /// <summary>A question about <paramref name="country"/> was answered "not covered" with <paramref name="code"/>.</summary>
    public void NotCovered(string country, string code) =>
        _notCovered.Add(1, new(CountryTag, country), new(ErrorCodeTag, code));

    /// <summary>A run of <paramref name="source"/> ended with <paramref name="outcome"/> and <paramref name="conflicts"/> override conflicts.</summary>
    public void SyncRan(string source, string outcome, int conflicts)
    {
        _syncRuns.Add(1, new(SourceTag, source), new(OutcomeTag, outcome));
        if (conflicts > 0)
        {
            _overrideConflicts.Add(conflicts, new KeyValuePair<string, object?>(SourceTag, source));
        }
    }

    /// <summary>
    /// A call to the address provider ended with <paramref name="outcome"/> after <paramref name="elapsed"/>; an answered
    /// call adds <paramref name="costUsd"/>. Tags never carry what was searched or resolved (S15).
    /// </summary>
    public void AddressProviderCalled(string provider, string operation, string outcome, TimeSpan elapsed, double costUsd)
    {
        var tags = new TagList { { AddressProviderTag, provider }, { AddressOperationTag, operation }, { OutcomeTag, outcome } };
        _addressRequests.Add(1, tags);
        _addressDuration.Record(elapsed.TotalSeconds, tags);
        if (costUsd > 0)
        {
            _addressCost.Add(costUsd, new KeyValuePair<string, object?>(AddressProviderTag, provider), new KeyValuePair<string, object?>(AddressOperationTag, operation));
        }
    }

    /// <summary>A resolved address's <paramref name="part"/> (<c>country</c> or <c>subdivision</c>) could not be mapped to an ISO code.</summary>
    public void AddressUnmapped(string? country, string part) =>
        _unmappedAddresses.Add(1, new KeyValuePair<string, object?>(CountryTag, country ?? "unknown"), new KeyValuePair<string, object?>(AddressPartTag, part));

    /// <summary>A search session of <paramref name="provider"/> got its first answered suggestion. The session itself is never a tag (S15).</summary>
    public void AddressSessionStarted(string provider) =>
        _addressSessionsStarted.Add(1, new KeyValuePair<string, object?>(AddressProviderTag, provider));

    /// <summary>A started search session of <paramref name="provider"/> was closed by an answered resolution.</summary>
    public void AddressSessionCompleted(string provider) =>
        _addressSessionsCompleted.Add(1, new KeyValuePair<string, object?>(AddressProviderTag, provider));
}
