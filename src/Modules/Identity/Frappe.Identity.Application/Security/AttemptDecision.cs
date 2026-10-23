namespace Frappe.Identity.Application.Security;

/// <summary>The outcome; <see cref="RetryAfter"/> is zero when allowed and feeds a 429 Retry-After otherwise.</summary>
public readonly record struct AttemptDecision(bool IsAllowed, int Remaining, TimeSpan RetryAfter)
{
    public static AttemptDecision Allow(int remaining) => new(true, remaining, TimeSpan.Zero);
}
