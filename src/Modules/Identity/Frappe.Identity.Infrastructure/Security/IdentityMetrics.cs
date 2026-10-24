using System.Diagnostics.Metrics;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Domain.Principals;

namespace Frappe.Identity.Infrastructure.Security;

/// <summary>
/// Identity's meter: safeguards that degraded (failed open), so an outage shows on dashboards, and security events
/// (throttled sign-ins, rejected codes, purge outcomes), so an attack or a stuck purge does too.
/// Each instrument name is a <c>&lt;What&gt;Name</c> constant shaped <c>frappe.&lt;module&gt;.&lt;thing&gt;.&lt;event&gt;</c>;
/// docs/conventions.md (Metrics) is the pattern a new module copies.
/// </summary>
internal sealed class IdentityMetrics : IIdentityTelemetry
{
    public const string MeterName = "Frappe.Identity";
    public const string BreachCheckUnavailableName = "frappe.identity.breach_check.unavailable";
    public const string AttemptLimiterUnavailableName = "frappe.identity.attempt_limiter.unavailable";
    public const string LoginThrottledName = "frappe.identity.login.throttled";
    public const string CodeInvalidName = "frappe.identity.code.invalid";
    public const string PurgeSkippedConflictName = "frappe.identity.purge.skipped_conflict";
    public const string KnownOriginsUnavailableName = "frappe.identity.known_origins.unavailable";
    public const string PurgeFailedName = "frappe.identity.purge.failed";
    public const string PurgedName = "frappe.identity.purge.purged";

    private readonly Counter<long> _breachCheckUnavailable;
    private readonly Counter<long> _attemptLimiterUnavailable;
    private readonly Counter<long> _knownOriginsUnavailable;
    private readonly Counter<long> _loginThrottled;
    private readonly Counter<long> _codeInvalid;
    private readonly Counter<long> _purgeSkippedConflict;
    private readonly Counter<long> _purgeFailed;
    private readonly Counter<long> _purged;

    public IdentityMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        var meter = meterFactory.Create(MeterName);
        _breachCheckUnavailable = meter.CreateCounter<long>(BreachCheckUnavailableName,
            description: "Breached-password checks skipped because the breach source failed or was slow.");
        _attemptLimiterUnavailable = meter.CreateCounter<long>(AttemptLimiterUnavailableName,
            description: "Attempt-limiter operations allowed through because Valkey was unavailable.");
        _knownOriginsUnavailable = meter.CreateCounter<long>(KnownOriginsUnavailableName,
            description: "Known sign-in origin lookups or writes skipped because Valkey was unavailable.");
        _loginThrottled = meter.CreateCounter<long>(LoginThrottledName,
            description: "Sign-ins refused by the attempt limiter.");
        _codeInvalid = meter.CreateCounter<long>(CodeInvalidName,
            description: "Wrong, expired or used verification codes presented, tagged by purpose.");
        _purgeSkippedConflict = meter.CreateCounter<long>(PurgeSkippedConflictName,
            description: "Pending owners the purge skipped because they changed meanwhile.");
        _purgeFailed = meter.CreateCounter<long>(PurgeFailedName,
            description: "Pending owners the purge could not delete because of an unexpected error; retried next run.");
        _purged = meter.CreateCounter<long>(PurgedName, description: "Abandoned registrations deleted by the purge.");
    }

    public void BreachCheckSkipped() => _breachCheckUnavailable.Add(1);

    public void AttemptLimiterSkipped() => _attemptLimiterUnavailable.Add(1);

    public void KnownOriginsSkipped() => _knownOriginsUnavailable.Add(1);

    public void LoginThrottled() => _loginThrottled.Add(1);

    public void CodeRejected(CodePurpose purpose) =>
        _codeInvalid.Add(1, new KeyValuePair<string, object?>("purpose", purpose.ToString()));

    public void PurgeSkippedConflict() => _purgeSkippedConflict.Add(1);

    public void PurgeFailed() => _purgeFailed.Add(1);

    public void PurgedOwners(int count) => _purged.Add(count);
}
