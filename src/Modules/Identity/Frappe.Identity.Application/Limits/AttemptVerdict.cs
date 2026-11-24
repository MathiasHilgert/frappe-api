namespace Frappe.Identity.Application.Limits;

/// <summary>The limiter's answer to one attempt: <see cref="Outcome"/> and, when refused, how long until the window closes.</summary>
public readonly record struct AttemptVerdict(AttemptOutcome Outcome, TimeSpan RetryAfter)
{
    /// <summary>The attempt is within the limit, or the flow fails open and the counter is down.</summary>
    public static AttemptVerdict Allowed { get; } = new(AttemptOutcome.Allowed, TimeSpan.Zero);

    /// <summary>The counter cannot be reached and the attempt must be refused.</summary>
    public static AttemptVerdict Unavailable { get; } = new(AttemptOutcome.Unavailable, TimeSpan.Zero);

    /// <summary>The subject must wait <paramref name="retryAfter"/> for its next window.</summary>
    public static AttemptVerdict Refused(TimeSpan retryAfter) => new(AttemptOutcome.Refused, retryAfter);
}
