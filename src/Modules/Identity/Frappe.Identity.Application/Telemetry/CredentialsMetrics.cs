using System.Diagnostics;
using System.Diagnostics.Metrics;
using Frappe.Identity.Application.Credentials;

namespace Frappe.Identity.Application.Telemetry;

/// <summary>
/// The Credentials feature's instruments on meter <c>Frappe.Identity</c> (OBS-01, OBS-02), created once through <see cref="IMeterFactory"/>.
/// Tags are bounded: the breach outcome is one of <see cref="BreachCheckOutcome"/>, the kind of a password change one of two names.
/// </summary>
public sealed class CredentialsMetrics
{
    /// <summary>Histogram: seconds a password hash or verification took.</summary>
    public const string PasswordHashDurationMetric = "frappe.identity.password_hash.duration";

    /// <summary>Histogram: seconds the breach list took to answer, by outcome (<c>clean</c>, <c>breached</c> or <c>unavailable</c>).</summary>
    public const string BreachCheckDurationMetric = "frappe.identity.breach_check.duration";

    /// <summary>Counter: passwords replaced, by kind (<c>change</c> by the signed-in holder, <c>reset</c> with a code).</summary>
    public const string PasswordChangesMetric = "frappe.identity.password_changes";

    /// <summary>The kind tag of <see cref="PasswordChangesMetric"/>.</summary>
    public const string KindTag = "frappe.kind";

    private readonly Histogram<double> _passwordHashDuration;
    private readonly Histogram<double> _breachCheckDuration;
    private readonly Counter<long> _passwordChanges;

    public CredentialsMetrics(IMeterFactory meters)
    {
        ArgumentNullException.ThrowIfNull(meters);
        var meter = meters.Create(IdentityTelemetry.MeterName);
        _passwordHashDuration = meter.CreateHistogram<double>(
            PasswordHashDurationMetric, unit: "s", description: "Seconds a password hash or verification took.");
        _breachCheckDuration = meter.CreateHistogram<double>(
            BreachCheckDurationMetric, unit: "s", description: "Seconds the breach list took to answer, by outcome.");
        _passwordChanges = meter.CreateCounter<long>(
            PasswordChangesMetric, unit: "{change}", description: "Passwords replaced, by kind (change or reset).");
    }

    /// <summary>A password was hashed or verified in <paramref name="elapsed"/>.</summary>
    public void PasswordHashed(TimeSpan elapsed) => _passwordHashDuration.Record(elapsed.TotalSeconds);

    /// <summary>The breach list answered, or gave up, after <paramref name="elapsed"/>.</summary>
    public void BreachChecked(TimeSpan elapsed, BreachCheckOutcome outcome) =>
        _breachCheckDuration.Record(elapsed.TotalSeconds, new KeyValuePair<string, object?>(IdentityTelemetry.OutcomeTag, OutcomeName(outcome)));

    /// <summary>A password was replaced; <paramref name="reset"/> says it was with a reset code rather than by its signed-in holder.</summary>
    public void PasswordChanged(bool reset) =>
        _passwordChanges.Add(1, new KeyValuePair<string, object?>(KindTag, reset ? "reset" : "change"));

    /// <summary>The tag value of an outcome; each is named, none falls back (OBS-05).</summary>
    internal static string OutcomeName(BreachCheckOutcome outcome) => outcome switch
    {
        BreachCheckOutcome.Clean => "clean",
        BreachCheckOutcome.Breached => "breached",
        BreachCheckOutcome.Unavailable => "unavailable",
        _ => throw new UnreachableException($"Unknown outcome {outcome}."),
    };
}
