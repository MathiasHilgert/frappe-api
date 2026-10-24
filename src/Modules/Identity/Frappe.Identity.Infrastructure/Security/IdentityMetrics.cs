using System.Diagnostics.Metrics;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Domain.Principals;

namespace Frappe.Identity.Infrastructure.Security;

/// <summary>
/// Identity's meter: safeguards that degraded (failed open), so an outage shows on dashboards, and security events
/// (throttled sign-ins, rejected codes, purge outcomes), so an attack or a stuck purge does too.
/// </summary>
internal sealed class IdentityMetrics : IIdentityTelemetry
{
    public const string MeterName = "Frappe.Identity";
    public const string BreachCheckUnavailable = "frappe.identity.breach_check.unavailable";
    public const string AttemptLimiterUnavailable = "frappe.identity.attempt_limiter.unavailable";
    public const string LoginThrottledName = "frappe.identity.login.throttled";
    public const string CodeInvalid = "frappe.identity.code.invalid";
    public const string PurgeSkippedConflictName = "frappe.identity.purge.skipped_conflict";
    public const string PurgedCount = "frappe.identity.purge.purged";

    private readonly Counter<long> _breachCheckUnavailable;
    private readonly Counter<long> _attemptLimiterUnavailable;
    private readonly Counter<long> _loginThrottled;
    private readonly Counter<long> _codeInvalid;
    private readonly Counter<long> _purgeSkippedConflict;
    private readonly Counter<long> _purged;

    public IdentityMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        var meter = meterFactory.Create(MeterName);
        _breachCheckUnavailable = meter.CreateCounter<long>(BreachCheckUnavailable,
            description: "Breached-password checks skipped because the breach source failed or was slow.");
        _attemptLimiterUnavailable = meter.CreateCounter<long>(AttemptLimiterUnavailable,
            description: "Attempt-limiter operations allowed through because Valkey was unavailable.");
        _loginThrottled = meter.CreateCounter<long>(LoginThrottledName,
            description: "Sign-ins refused by the attempt limiter.");
        _codeInvalid = meter.CreateCounter<long>(CodeInvalid,
            description: "Wrong, expired or used verification codes presented, tagged by purpose.");
        _purgeSkippedConflict = meter.CreateCounter<long>(PurgeSkippedConflictName,
            description: "Pending owners the purge skipped because they changed meanwhile.");
        _purged = meter.CreateCounter<long>(PurgedCount, description: "Abandoned registrations deleted by the purge.");
    }

    public void BreachCheckSkipped() => _breachCheckUnavailable.Add(1);

    public void AttemptLimiterSkipped() => _attemptLimiterUnavailable.Add(1);

    public void LoginThrottled() => _loginThrottled.Add(1);

    public void CodeRejected(CodePurpose purpose) =>
        _codeInvalid.Add(1, new KeyValuePair<string, object?>("purpose", purpose.ToString()));

    public void PurgeSkippedConflict() => _purgeSkippedConflict.Add(1);

    public void PurgedOwners(int count) => _purged.Add(count);
}
