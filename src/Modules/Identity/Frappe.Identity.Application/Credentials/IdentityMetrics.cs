using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Frappe.Identity.Application.Credentials;

/// <summary>
/// The Identity module's instruments on meter <c>Frappe.Identity</c> (OBS-01), created once through <see cref="IMeterFactory"/>. Tags are
/// bounded: the outcome is one of <see cref="BreachCheckOutcome"/>.
/// </summary>
public sealed class IdentityMetrics
{
    /// <summary>The meter's name.</summary>
    public const string MeterName = "Frappe.Identity";

    /// <summary>Histogram: seconds a password hash or verification took.</summary>
    public const string PasswordHashDurationMetric = "frappe.identity.password_hash.duration";

    /// <summary>Histogram: seconds the breach list took to answer, by outcome (<c>clean</c>, <c>breached</c> or <c>unavailable</c>).</summary>
    public const string BreachCheckDurationMetric = "frappe.identity.breach_check.duration";

    /// <summary>Counter: attempts an attempt limiter could not count because its store was unreachable, by flow.</summary>
    public const string LimiterUnavailableMetric = "identity.limiter.unavailable";

    /// <summary>Counter: touches of a session that lost the race to another request of the same session (harmless, expected).</summary>
    public const string SessionTouchLostMetric = "frappe.identity.session_touch.lost";

    /// <summary>Counter: registrations, by outcome (<c>created</c>, <c>replaced</c> or <c>taken</c>).</summary>
    public const string RegistrationsMetric = "frappe.identity.registrations";

    /// <summary>Counter: email verifications, by outcome (<c>verified</c>, <c>invalid</c> or <c>unavailable</c>).</summary>
    public const string VerificationsMetric = "frappe.identity.verifications";

    /// <summary>Counter: log-in attempts, by outcome (<c>opened</c>, <c>pending</c>, <c>invalid</c> or <c>throttled</c>).</summary>
    public const string LoginsMetric = "frappe.identity.logins";

    /// <summary>Counter: sessions opened, by audience.</summary>
    public const string SessionsOpenedMetric = "frappe.identity.sessions.opened";

    /// <summary>The audience tag of <see cref="SessionsOpenedMetric"/>: <c>console</c> today.</summary>
    public const string AudienceTag = "frappe.audience";

    /// <summary>Counter: one-time codes issued, by purpose.</summary>
    public const string CodesIssuedMetric = "frappe.identity.codes.issued";

    /// <summary>The purpose tag of <see cref="CodesIssuedMetric"/>: <c>email_verification</c>, <c>password_reset</c>, <c>email_change</c>.</summary>
    public const string PurposeTag = "frappe.purpose";

    /// <summary>Counter: passwords replaced, by kind (<c>change</c> by the signed-in holder, <c>reset</c> with a code).</summary>
    public const string PasswordChangesMetric = "frappe.identity.password_changes";

    /// <summary>The kind tag of <see cref="PasswordChangesMetric"/>.</summary>
    public const string KindTag = "frappe.kind";

    /// <summary>Counter: sessions revoked, by reason (the stored name of a revocation reason other than <c>logout</c>).</summary>
    public const string SessionsRevokedMetric = "frappe.identity.sessions.revoked";

    /// <summary>The reason tag of <see cref="SessionsRevokedMetric"/>: a small fixed set.</summary>
    public const string ReasonTag = "frappe.reason";

    /// <summary>Counter: step-ups, by outcome (<c>completed</c>, <c>incorrect</c> or <c>throttled</c>).</summary>
    public const string StepUpsMetric = "frappe.identity.step_ups";

    /// <summary>The flow tag of <see cref="LimiterUnavailableMetric"/>: the name of a declared limit flow, a small fixed set.</summary>
    public const string FlowTag = "frappe.flow";

    /// <summary>Counter: sessions the sweep ended, by reason (<c>idle</c>, <c>absolute</c> or <c>revoked</c>).</summary>
    public const string SessionsEndedMetric = "frappe.identity.sessions.ended";

    /// <summary>Counter: rows the retention purge deleted, by target (<c>sessions</c>, <c>owners</c> or <c>codes</c>).</summary>
    public const string PurgedMetric = "frappe.identity.purged";

    /// <summary>The target tag of <see cref="PurgedMetric"/>: the kind of data purged, a small fixed set.</summary>
    public const string TargetTag = "frappe.target";

    /// <summary>The outcome tag every module uses (OBS-04).</summary>
    public const string OutcomeTag = "frappe.outcome";

    private readonly Histogram<double> _passwordHashDuration;
    private readonly Histogram<double> _breachCheckDuration;
    private readonly Counter<long> _limiterUnavailable;
    private readonly Counter<long> _sessionTouchLost;
    private readonly Counter<long> _registrations;
    private readonly Counter<long> _verifications;
    private readonly Counter<long> _codesIssued;
    private readonly Counter<long> _logins;
    private readonly Counter<long> _sessionsOpened;
    private readonly Counter<long> _passwordChanges;
    private readonly Counter<long> _sessionsRevoked;
    private readonly Counter<long> _stepUps;
    private readonly Counter<long> _sessionsEnded;
    private readonly Counter<long> _purged;

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
        _sessionTouchLost = meter.CreateCounter<long>(
            SessionTouchLostMetric, unit: "{touch}",
            description: "Touches of a session that lost the race to another request of the same session.");
        _registrations = meter.CreateCounter<long>(
            RegistrationsMetric, unit: "{registration}", description: "Owner registrations, by outcome.");
        _verifications = meter.CreateCounter<long>(
            VerificationsMetric, unit: "{verification}", description: "Owner email verifications, by outcome.");
        _codesIssued = meter.CreateCounter<long>(CodesIssuedMetric, unit: "{code}", description: "One-time codes issued, by purpose.");
        _logins = meter.CreateCounter<long>(LoginsMetric, unit: "{login}", description: "Log-in attempts, by outcome.");
        _sessionsOpened = meter.CreateCounter<long>(SessionsOpenedMetric, unit: "{session}", description: "Sessions opened, by audience.");
        _passwordChanges = meter.CreateCounter<long>(
            PasswordChangesMetric, unit: "{change}", description: "Passwords replaced, by kind (change or reset).");
        _sessionsRevoked = meter.CreateCounter<long>(
            SessionsRevokedMetric, unit: "{session}", description: "Sessions revoked, by reason.");
        _stepUps = meter.CreateCounter<long>(StepUpsMetric, unit: "{step-up}", description: "Step-ups, by outcome.");
        _sessionsEnded = meter.CreateCounter<long>(SessionsEndedMetric, unit: "{session}", description: "Sessions the sweep ended, by reason.");
        _purged = meter.CreateCounter<long>(PurgedMetric, unit: "{row}", description: "Rows the retention purge deleted, by target.");
    }

    /// <summary>A password was hashed or verified in <paramref name="elapsed"/>.</summary>
    public void PasswordHashed(TimeSpan elapsed) => _passwordHashDuration.Record(elapsed.TotalSeconds);

    /// <summary>The breach list answered, or gave up, after <paramref name="elapsed"/>.</summary>
    public void BreachChecked(TimeSpan elapsed, BreachCheckOutcome outcome) =>
        _breachCheckDuration.Record(elapsed.TotalSeconds, new KeyValuePair<string, object?>(OutcomeTag, OutcomeName(outcome)));

    /// <summary>An attempt of <paramref name="flow"/> could not be counted: the store was unreachable (its failure mode decided the rest).</summary>
    public void LimiterUnavailable(string flow) => _limiterUnavailable.Add(1, new KeyValuePair<string, object?>(FlowTag, flow));

    /// <summary>A touch of a session lost the race to another request of the same session and committed nothing.</summary>
    public void SessionTouchLost() => _sessionTouchLost.Add(1);

    /// <summary>A registration committed with <paramref name="outcome"/>.</summary>
    public void Registered(RegistrationOutcome outcome) =>
        _registrations.Add(1, new KeyValuePair<string, object?>(OutcomeTag, OutcomeName(outcome)));

    /// <summary>An email verification ended with <paramref name="outcome"/>.</summary>
    public void Verified(VerificationOutcome outcome) =>
        _verifications.Add(1, new KeyValuePair<string, object?>(OutcomeTag, OutcomeName(outcome)));

    /// <summary>A log-in attempt ended with <paramref name="outcome"/>.</summary>
    public void LoggedIn(LoginOutcome outcome) => _logins.Add(1, new KeyValuePair<string, object?>(OutcomeTag, OutcomeName(outcome)));

    /// <summary>A session opened for <paramref name="audience"/> (the name its stored form uses).</summary>
    public void SessionOpened(string audience) => _sessionsOpened.Add(1, new KeyValuePair<string, object?>(AudienceTag, audience));

    /// <summary>A password was replaced; <paramref name="reset"/> says it was with a reset code rather than by its signed-in holder.</summary>
    public void PasswordChanged(bool reset) =>
        _passwordChanges.Add(1, new KeyValuePair<string, object?>(KindTag, reset ? "reset" : "change"));

    /// <summary>A step-up ended with <paramref name="outcome"/>.</summary>
    public void SteppedUp(StepUpOutcome outcome) => _stepUps.Add(1, new KeyValuePair<string, object?>(OutcomeTag, OutcomeName(outcome)));

    /// <summary>A session was revoked for <paramref name="reason"/> (the name its stored form uses).</summary>
    public void SessionRevoked(string reason) => _sessionsRevoked.Add(1, new KeyValuePair<string, object?>(ReasonTag, reason));
    /// <summary>The sweep ended <paramref name="count"/> sessions for <paramref name="reason"/> and committed.</summary>
    public void SessionsEnded(string reason, long count) =>
        _sessionsEnded.Add(count, new KeyValuePair<string, object?>(ReasonTag, reason));

    /// <summary>The purge deleted <paramref name="rows"/> rows of <paramref name="target"/> and committed.</summary>
    public void Purged(string target, long rows) => _purged.Add(rows, new KeyValuePair<string, object?>(TargetTag, target));

    /// <summary>A one-time code was issued for <paramref name="purpose"/> (the name its stored form uses).</summary>
    public void CodeIssued(string purpose) => _codesIssued.Add(1, new KeyValuePair<string, object?>(PurposeTag, purpose));

    internal static string OutcomeName(RegistrationOutcome outcome) => outcome switch
    {
        RegistrationOutcome.Created => "created",
        RegistrationOutcome.Replaced => "replaced",
        RegistrationOutcome.Taken => "taken",
        _ => throw new UnreachableException($"Unknown outcome {outcome}."),
    };

    internal static string OutcomeName(LoginOutcome outcome) => outcome switch
    {
        LoginOutcome.Opened => "opened",
        LoginOutcome.Pending => "pending",
        LoginOutcome.Invalid => "invalid",
        LoginOutcome.Throttled => "throttled",
        _ => throw new UnreachableException($"Unknown outcome {outcome}."),
    };

    internal static string OutcomeName(StepUpOutcome outcome) => outcome switch
    {
        StepUpOutcome.Completed => "completed",
        StepUpOutcome.Incorrect => "incorrect",
        StepUpOutcome.Throttled => "throttled",
        _ => throw new UnreachableException($"Unknown outcome {outcome}."),
    };

    internal static string OutcomeName(VerificationOutcome outcome) => outcome switch
    {
        VerificationOutcome.Verified => "verified",
        VerificationOutcome.Invalid => "invalid",
        VerificationOutcome.Unavailable => "unavailable",
        _ => throw new UnreachableException($"Unknown outcome {outcome}."),
    };

    /// <summary>The tag value of an outcome; each is named, none falls back (OBS-05).</summary>
    internal static string OutcomeName(BreachCheckOutcome outcome) => outcome switch
    {
        BreachCheckOutcome.Clean => "clean",
        BreachCheckOutcome.Breached => "breached",
        BreachCheckOutcome.Unavailable => "unavailable",
        _ => throw new UnreachableException($"Unknown outcome {outcome}."),
    };
}
