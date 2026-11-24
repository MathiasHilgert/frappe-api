namespace Frappe.Identity.Application.Limits;

/// <summary>
/// Counts attempts that must outlive a failed command: a failed <c>Result</c> rolls its unit of work back, so a wrong guess recorded in the
/// database would vanish with it. The counter lives outside it, keyed by flow and subject. Each feature declares its own
/// <see cref="LimitFlow"/>s; this port knows none of them.
/// </summary>
public interface IAttemptLimiter
{
    /// <summary>
    /// Counts one attempt of <paramref name="subjectKey"/> (an id or a hash, never an address or a secret) under <paramref name="flow"/>.
    /// A flow that fails open answers <see cref="AttemptOutcome.Allowed"/> when the counter is down; one that fails closed answers
    /// <see cref="AttemptOutcome.Unavailable"/>. Only the caller's own cancellation throws.
    /// </summary>
    Task<AttemptVerdict> HitAsync(LimitFlow flow, string subjectKey, CancellationToken cancellationToken);
}
