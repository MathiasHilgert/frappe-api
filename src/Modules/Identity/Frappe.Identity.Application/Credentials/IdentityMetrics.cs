using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Frappe.Identity.Application.Credentials;

/// <summary>
/// The Identity module's instruments on meter <c>Frappe.Identity</c> (OBS-01), created once through <see cref="IMeterFactory"/>. Tags are
/// bounded: the outcome is one of <see cref="BreachCheckOutcome"/>. The names are the ones the feature's signals list gives.
/// </summary>
public sealed class IdentityMetrics
{
    /// <summary>The meter's name.</summary>
    public const string MeterName = "Frappe.Identity";

    /// <summary>Histogram: seconds a password hash or verification took.</summary>
    public const string PasswordHashDurationMetric = "identity.password_hash.duration";

    /// <summary>Histogram: seconds the breach list took to answer, by outcome (<c>clean</c>, <c>breached</c> or <c>unavailable</c>).</summary>
    public const string BreachCheckDurationMetric = "identity.breach_check.duration";

    /// <summary>Counter: attempts an attempt limiter could not count because its store was unreachable, by flow.</summary>
    public const string LimiterUnavailableMetric = "identity.limiter.unavailable";

    /// <summary>The flow tag of <see cref="LimiterUnavailableMetric"/>: the name of a declared limit flow, a small fixed set.</summary>
    public const string FlowTag = "frappe.flow";

    /// <summary>The outcome tag every module uses (OBS-04).</summary>
    public const string OutcomeTag = "frappe.outcome";

    private readonly Histogram<double> _passwordHashDuration;
    private readonly Histogram<double> _breachCheckDuration;
    private readonly Counter<long> _limiterUnavailable;

    public IdentityMetrics(IMeterFactory meters)
    {
        ArgumentNullException.ThrowIfNull(meters);
        var meter = meters.Create(MeterName);
        _passwordHashDuration = meter.CreateHistogram<double>(
            PasswordHashDurationMetric, unit: "s", description: "Seconds a password hash or verification took.");
        _breachCheckDuration = meter.CreateHistogram<double>(
            BreachCheckDurationMetric, unit: "s", description: "Seconds the breach list took to answer, by outcome.");
        _limiterUnavailable = meter.CreateCounter<long>(
            LimiterUnavailableMetric, description: "Attempts the limiter could not count because its store was unreachable, by flow.");
    }

    /// <summary>A password was hashed or verified in <paramref name="elapsed"/>.</summary>
    public void PasswordHashed(TimeSpan elapsed) => _passwordHashDuration.Record(elapsed.TotalSeconds);

    /// <summary>The breach list answered, or gave up, after <paramref name="elapsed"/>.</summary>
    public void BreachChecked(TimeSpan elapsed, BreachCheckOutcome outcome) =>
        _breachCheckDuration.Record(elapsed.TotalSeconds, new KeyValuePair<string, object?>(OutcomeTag, OutcomeName(outcome)));

    /// <summary>An attempt of <paramref name="flow"/> could not be counted: the store was unreachable (its failure mode decided the rest).</summary>
    public void LimiterUnavailable(string flow) => _limiterUnavailable.Add(1, new KeyValuePair<string, object?>(FlowTag, flow));

    /// <summary>The tag value of an outcome; each is named, none falls back (OBS-05).</summary>
    internal static string OutcomeName(BreachCheckOutcome outcome) => outcome switch
    {
        BreachCheckOutcome.Clean => "clean",
        BreachCheckOutcome.Breached => "breached",
        BreachCheckOutcome.Unavailable => "unavailable",
        _ => throw new UnreachableException($"Unknown outcome {outcome}."),
    };
}
