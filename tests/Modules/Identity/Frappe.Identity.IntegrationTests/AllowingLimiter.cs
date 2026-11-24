using Frappe.Identity.Application.Limits;

namespace Frappe.Identity.IntegrationTests;

/// <summary>A limiter that never refuses, for tests of what the database does with codes.</summary>
internal sealed class AllowingLimiter : IAttemptLimiter
{
    public Task<AttemptVerdict> HitAsync(LimitFlow flow, string subjectKey, CancellationToken cancellationToken) =>
        Task.FromResult(AttemptVerdict.Allowed);
}
