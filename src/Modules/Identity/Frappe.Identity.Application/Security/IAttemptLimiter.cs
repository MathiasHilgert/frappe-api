namespace Frappe.Identity.Application.Security;

/// <summary>
/// Counts attempts per policy and key in a fixed window. It only slows callers down: nothing is ever locked, and a
/// blocked key frees itself when its window ends. Keys compare case-insensitively (emails, IP addresses).
/// </summary>
public interface IAttemptLimiter
{
    /// <summary>Whether one more attempt may proceed, without counting it. Use before work that records only failures.</summary>
    Task<AttemptDecision> CheckAsync(AttemptPolicy policy, string key, CancellationToken cancellationToken);

    /// <summary>Counts one attempt; allowed while the count stays within the limit. Use to meter every attempt.</summary>
    Task<AttemptDecision> RecordAsync(AttemptPolicy policy, string key, CancellationToken cancellationToken);

    /// <summary>Forgets the key's attempts, for example after a successful sign-in.</summary>
    Task ResetAsync(AttemptPolicy policy, string key, CancellationToken cancellationToken);
}
