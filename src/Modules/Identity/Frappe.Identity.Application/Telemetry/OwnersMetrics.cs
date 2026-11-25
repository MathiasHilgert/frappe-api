using System.Diagnostics;
using System.Diagnostics.Metrics;
using Frappe.Identity.Application.Credentials;

namespace Frappe.Identity.Application.Telemetry;

/// <summary>
/// The Owners feature's instruments on meter <c>Frappe.Identity</c> (OBS-01, OBS-02), created once through <see cref="IMeterFactory"/>. Tags are
/// bounded: the outcomes are <see cref="RegistrationOutcome"/> and <see cref="VerificationOutcome"/>.
/// </summary>
public sealed class OwnersMetrics
{
    /// <summary>Counter: registrations, by outcome (<c>created</c>, <c>replaced</c> or <c>taken</c>).</summary>
    public const string RegistrationsMetric = "frappe.identity.registrations";

    /// <summary>Counter: email verifications, by outcome (<c>verified</c>, <c>invalid</c> or <c>unavailable</c>).</summary>
    public const string VerificationsMetric = "frappe.identity.verifications";

    private readonly Counter<long> _registrations;
    private readonly Counter<long> _verifications;

    public OwnersMetrics(IMeterFactory meters)
    {
        ArgumentNullException.ThrowIfNull(meters);
        var meter = meters.Create(IdentityTelemetry.MeterName);
        _registrations = meter.CreateCounter<long>(
            RegistrationsMetric, unit: "{registration}", description: "Owner registrations, by outcome.");
        _verifications = meter.CreateCounter<long>(
            VerificationsMetric, unit: "{verification}", description: "Owner email verifications, by outcome.");
    }

    /// <summary>A registration committed with <paramref name="outcome"/>.</summary>
    public void Registered(RegistrationOutcome outcome) =>
        _registrations.Add(1, new KeyValuePair<string, object?>(IdentityTelemetry.OutcomeTag, OutcomeName(outcome)));

    /// <summary>An email verification ended with <paramref name="outcome"/>.</summary>
    public void Verified(VerificationOutcome outcome) =>
        _verifications.Add(1, new KeyValuePair<string, object?>(IdentityTelemetry.OutcomeTag, OutcomeName(outcome)));

    /// <summary>The tag value of an outcome; each is named, none falls back (OBS-05).</summary>
    internal static string OutcomeName(RegistrationOutcome outcome) => outcome switch
    {
        RegistrationOutcome.Created => "created",
        RegistrationOutcome.Replaced => "replaced",
        RegistrationOutcome.Taken => "taken",
        _ => throw new UnreachableException($"Unknown outcome {outcome}."),
    };

    /// <summary>The tag value of an outcome; each is named, none falls back (OBS-05).</summary>
    internal static string OutcomeName(VerificationOutcome outcome) => outcome switch
    {
        VerificationOutcome.Verified => "verified",
        VerificationOutcome.Invalid => "invalid",
        VerificationOutcome.Unavailable => "unavailable",
        _ => throw new UnreachableException($"Unknown outcome {outcome}."),
    };
}
