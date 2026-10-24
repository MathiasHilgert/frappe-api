namespace Frappe.Identity.Application.Security;

/// <summary>
/// Reserve-first gating over several limits at once: every limit is reserved before the work, so parallel attempts
/// cannot all pass a check and record later. When any limit refuses, every slot that was taken is given back and the
/// caller is told the longest wait among the refusals.
/// </summary>
/// <remarks>
/// Giving slots back is compensation, so it runs on <see cref="CancellationToken.None"/>: a request cancelled midway
/// must not leak the reservations it already holds (the limiter bounds every call with its own timeout).
/// </remarks>
public sealed class AttemptReservation
{
    private readonly IAttemptLimiter _limiter;
    private readonly (AttemptPolicy Policy, string Key)[] _limits;

    private AttemptReservation(IAttemptLimiter limiter, (AttemptPolicy, string)[] limits, AttemptDecision decision)
    {
        _limiter = limiter;
        _limits = limits;
        Decision = decision;
    }

    /// <summary>Allowed only when every limit was reserved; otherwise carries the longest retry-after.</summary>
    public AttemptDecision Decision { get; }

    public bool IsAllowed => Decision.IsAllowed;

    public static async Task<AttemptReservation> TakeAsync(
        IAttemptLimiter limiter, IReadOnlyCollection<(AttemptPolicy Policy, string Key)> limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(limiter);
        ArgumentNullException.ThrowIfNull(limits);

        var taken = new List<(AttemptPolicy, string)>(limits.Count);
        var longestWait = TimeSpan.Zero;
        var refused = false;
        foreach (var (policy, key) in limits)
        {
            var decision = await limiter.ReserveAsync(policy, key, cancellationToken);
            if (decision.IsAllowed)
            {
                taken.Add((policy, key));
            }
            else
            {
                refused = true;
                longestWait = decision.RetryAfter > longestWait ? decision.RetryAfter : longestWait;
            }
        }

        if (!refused)
            return new AttemptReservation(limiter, [.. taken], AttemptDecision.Allow(0));

        foreach (var (policy, key) in taken)
            await limiter.ReleaseAsync(policy, key, CancellationToken.None);
        return new AttemptReservation(limiter, [], new AttemptDecision(false, 0, longestWait));
    }

    /// <summary>The attempt did not count (a failure the limits do not meter): give every slot back.</summary>
    public async Task ReleaseAsync()
    {
        foreach (var (policy, key) in _limits)
            await _limiter.ReleaseAsync(policy, key, CancellationToken.None);
    }

    /// <summary>A success that clears <paramref name="policy"/>'s key entirely, giving back the other slots.</summary>
    public async Task SucceededAsync(AttemptPolicy policy)
    {
        foreach (var (held, key) in _limits)
        {
            if (held == policy)
                await _limiter.ResetAsync(held, key, CancellationToken.None);
            else
                await _limiter.ReleaseAsync(held, key, CancellationToken.None);
        }
    }
}
