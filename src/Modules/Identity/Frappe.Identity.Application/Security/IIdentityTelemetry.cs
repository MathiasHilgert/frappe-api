using Frappe.Identity.Domain.Principals;

namespace Frappe.Identity.Application.Security;

/// <summary>Security events worth a dashboard and an alert: throttling, rejected codes, purge outcomes.</summary>
public interface IIdentityTelemetry
{
    /// <summary>A sign-in was refused by the attempt limiter.</summary>
    void LoginThrottled();

    /// <summary>A wrong, expired or used code was presented for <paramref name="purpose"/>.</summary>
    void CodeRejected(CodePurpose purpose);

    /// <summary>The purge skipped a pending owner someone changed meanwhile; the next run retries it.</summary>
    void PurgeSkippedConflict();

    /// <summary>The purge deleted <paramref name="count"/> abandoned registrations.</summary>
    void PurgedOwners(int count);
}
