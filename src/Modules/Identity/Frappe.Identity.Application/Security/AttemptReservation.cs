namespace Frappe.Identity.Application.Security;

/// <summary>
/// Reserve-first gating over several limits at once: every limit is reserved before the work, so parallel attempts
/// cannot all pass a check and record later. A refused limit gives back the reservations already taken.
/// </summary>
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
        IAttemptLimiter limiter, CancellationToken cancellationToken, params (AttemptPolicy Policy, string Key)[] limits)
    {
        ArgumentNullException.ThrowIfNull(limiter);
        ArgumentNullException.ThrowIfNull(limits);

        var taken = new List<(AttemptPolicy, string)>(limits.Length);
        foreach (var (policy, key) in limits)
        {
            var decision = await limiter.ReserveAsync(policy, key, cancellationToken);
            if (!decision.IsAllowed)
            {
                foreach (var (heldPolicy, heldKey) in taken)
                    await limiter.ReleaseAsync(heldPolicy, heldKey, cancellationToken);
                return new AttemptReservation(limiter, [], decision);
            }
            taken.Add((policy, key));
        }

        return new AttemptReservation(limiter, [.. taken], AttemptDecision.Allow(0));
    }

    /// <summary>The attempt did not count (a success, or a failure the limits do not meter): give every slot back.</summary>
    public async Task ReleaseAsync(CancellationToken cancellationToken)
    {
        foreach (var (policy, key) in _limits)
            await _limiter.ReleaseAsync(policy, key, cancellationToken);
    }

    /// <summary>A success that clears <paramref name="policy"/>'s key entirely, giving back the other slots.</summary>
    public async Task SucceededAsync(AttemptPolicy policy, CancellationToken cancellationToken)
    {
        foreach (var (held, key) in _limits)
        {
            if (held == policy)
                await _limiter.ResetAsync(held, key, cancellationToken);
            else
                await _limiter.ReleaseAsync(held, key, cancellationToken);
        }
    }
}
