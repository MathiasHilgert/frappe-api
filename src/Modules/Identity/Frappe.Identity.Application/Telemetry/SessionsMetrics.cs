using System.Diagnostics;
using System.Diagnostics.Metrics;
using Frappe.Identity.Application.Credentials;

namespace Frappe.Identity.Application.Telemetry;

/// <summary>
/// The Sessions feature's instruments on meter <c>Frappe.Identity</c> (OBS-01, OBS-02), created once through <see cref="IMeterFactory"/>. Tags are
/// bounded: the outcomes are <see cref="LoginOutcome"/> and <see cref="StepUpOutcome"/>, the audience and the reason come from stored enums.
/// </summary>
public sealed class SessionsMetrics
{
    /// <summary>Counter: touches of a session that lost the race to another request of the same session (harmless, expected).</summary>
    public const string SessionTouchLostMetric = "frappe.identity.session_touch.lost";

    /// <summary>Counter: log-in attempts, by outcome (<c>opened</c>, <c>pending</c>, <c>invalid</c> or <c>throttled</c>).</summary>
    public const string LoginsMetric = "frappe.identity.logins";

    /// <summary>Counter: sessions opened, by audience.</summary>
    public const string SessionsOpenedMetric = "frappe.identity.sessions.opened";

    /// <summary>The audience tag of <see cref="SessionsOpenedMetric"/>: <c>console</c> today.</summary>
    public const string AudienceTag = "frappe.audience";

    /// <summary>Counter: sessions revoked, by reason (the stored name of a revocation reason other than <c>logout</c>).</summary>
    public const string SessionsRevokedMetric = "frappe.identity.sessions.revoked";

    /// <summary>The reason tag of <see cref="SessionsRevokedMetric"/> and <see cref="SessionsEndedMetric"/>: a small fixed set.</summary>
    public const string ReasonTag = "frappe.reason";

    /// <summary>Counter: step-ups, by outcome (<c>completed</c>, <c>incorrect</c> or <c>throttled</c>).</summary>
    public const string StepUpsMetric = "frappe.identity.step_ups";

    /// <summary>Counter: sessions the sweep ended, by reason (<c>idle</c>, <c>absolute</c> or <c>revoked</c>).</summary>
    public const string SessionsEndedMetric = "frappe.identity.sessions.ended";

    private readonly Counter<long> _sessionTouchLost;
    private readonly Counter<long> _logins;
    private readonly Counter<long> _sessionsOpened;
    private readonly Counter<long> _sessionsRevoked;
    private readonly Counter<long> _stepUps;
    private readonly Counter<long> _sessionsEnded;

    public SessionsMetrics(IMeterFactory meters)
    {
        ArgumentNullException.ThrowIfNull(meters);
        var meter = meters.Create(IdentityTelemetry.MeterName);
        _sessionTouchLost = meter.CreateCounter<long>(
            SessionTouchLostMetric, unit: "{touch}",
            description: "Touches of a session that lost the race to another request of the same session.");
        _logins = meter.CreateCounter<long>(LoginsMetric, unit: "{login}", description: "Log-in attempts, by outcome.");
        _sessionsOpened = meter.CreateCounter<long>(SessionsOpenedMetric, unit: "{session}", description: "Sessions opened, by audience.");
        _sessionsRevoked = meter.CreateCounter<long>(
            SessionsRevokedMetric, unit: "{session}", description: "Sessions revoked, by reason.");
        _stepUps = meter.CreateCounter<long>(StepUpsMetric, unit: "{step-up}", description: "Step-ups, by outcome.");
        _sessionsEnded = meter.CreateCounter<long>(SessionsEndedMetric, unit: "{session}", description: "Sessions the sweep ended, by reason.");
    }

    /// <summary>A touch of a session lost the race to another request of the same session and committed nothing.</summary>
    public void SessionTouchLost() => _sessionTouchLost.Add(1);

    /// <summary>A log-in attempt ended with <paramref name="outcome"/>.</summary>
    public void LoggedIn(LoginOutcome outcome) =>
        _logins.Add(1, new KeyValuePair<string, object?>(IdentityTelemetry.OutcomeTag, OutcomeName(outcome)));

    /// <summary>A session opened for <paramref name="audience"/> (the name its stored form uses).</summary>
    public void SessionOpened(string audience) => _sessionsOpened.Add(1, new KeyValuePair<string, object?>(AudienceTag, audience));

    /// <summary>A session was revoked for <paramref name="reason"/> (the name its stored form uses).</summary>
    public void SessionRevoked(string reason) => _sessionsRevoked.Add(1, new KeyValuePair<string, object?>(ReasonTag, reason));

    /// <summary>A step-up ended with <paramref name="outcome"/>.</summary>
    public void SteppedUp(StepUpOutcome outcome) =>
        _stepUps.Add(1, new KeyValuePair<string, object?>(IdentityTelemetry.OutcomeTag, OutcomeName(outcome)));

    /// <summary>The sweep ended <paramref name="count"/> sessions for <paramref name="reason"/> and committed.</summary>
    public void SessionsEnded(string reason, long count) =>
        _sessionsEnded.Add(count, new KeyValuePair<string, object?>(ReasonTag, reason));

    /// <summary>The tag value of an outcome; each is named, none falls back (OBS-05).</summary>
    internal static string OutcomeName(LoginOutcome outcome) => outcome switch
    {
        LoginOutcome.Opened => "opened",
        LoginOutcome.Pending => "pending",
        LoginOutcome.Invalid => "invalid",
        LoginOutcome.Throttled => "throttled",
        _ => throw new UnreachableException($"Unknown outcome {outcome}."),
    };

    /// <summary>The tag value of an outcome; each is named, none falls back (OBS-05).</summary>
    internal static string OutcomeName(StepUpOutcome outcome) => outcome switch
    {
        StepUpOutcome.Completed => "completed",
        StepUpOutcome.Incorrect => "incorrect",
        StepUpOutcome.Throttled => "throttled",
        _ => throw new UnreachableException($"Unknown outcome {outcome}."),
    };
}
