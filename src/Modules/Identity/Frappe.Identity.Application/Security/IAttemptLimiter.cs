namespace Frappe.Identity.Application.Security;

/// <summary>
/// Counts attempts per policy and key in a fixed window. It only slows callers down: nothing is ever locked, and a
/// blocked key frees itself when its window ends. Keys compare case-insensitively (emails, IP addresses).
/// </summary>
public interface IAttemptLimiter
{
    /// <summary>
    /// Atomically counts one attempt unless the key is already at its limit, in which case nothing is counted and the
    /// attempt is refused. Reserve before the work, so parallel attempts cannot all pass one check; then
    /// <see cref="ReleaseAsync"/> or <see cref="ResetAsync"/> when the attempt turns out not to count.
    /// </summary>
    Task<AttemptDecision> ReserveAsync(AttemptPolicy policy, string key, CancellationToken cancellationToken);

    /// <summary>Gives back one reserved attempt that turned out not to count (never below zero).</summary>
    Task ReleaseAsync(AttemptPolicy policy, string key, CancellationToken cancellationToken);

    /// <summary>Counts one attempt; allowed while the count stays within the limit. Use to meter every attempt.</summary>
    Task<AttemptDecision> RecordAsync(AttemptPolicy policy, string key, CancellationToken cancellationToken);

    /// <summary>Forgets the key's attempts, for example after a successful sign-in.</summary>
    Task ResetAsync(AttemptPolicy policy, string key, CancellationToken cancellationToken);
}
