using System.Diagnostics.Metrics;

namespace Frappe.Identity.Application.Telemetry;

/// <summary>
/// The Codes feature's instrument on meter <c>Frappe.Identity</c> (OBS-01, OBS-02), created once through <see cref="IMeterFactory"/>. The purpose
/// tag is the stored name of a code purpose, a small fixed set.
/// </summary>
public sealed class CodesMetrics
{
    /// <summary>Counter: one-time codes issued, by purpose.</summary>
    public const string CodesIssuedMetric = "frappe.identity.codes.issued";

    /// <summary>The purpose tag of <see cref="CodesIssuedMetric"/>: <c>email_verification</c>, <c>password_reset</c>, <c>email_change</c>.</summary>
    public const string PurposeTag = "frappe.purpose";

    private readonly Counter<long> _codesIssued;

    public CodesMetrics(IMeterFactory meters)
    {
        ArgumentNullException.ThrowIfNull(meters);
        _codesIssued = meters.Create(IdentityTelemetry.MeterName).CreateCounter<long>(
            CodesIssuedMetric, unit: "{code}", description: "One-time codes issued, by purpose.");
    }

    /// <summary>A one-time code was issued for <paramref name="purpose"/> (the name its stored form uses).</summary>
    public void CodeIssued(string purpose) => _codesIssued.Add(1, new KeyValuePair<string, object?>(PurposeTag, purpose));
}
