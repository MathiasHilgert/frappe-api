namespace Frappe.Identity.Infrastructure.Security;

/// <summary>
/// How long one limiter call may wait on Valkey before failing open. Kept separate from the shared Valkey client
/// timeouts (which the cache also uses), so an outage adds at most this much to a sign-in.
/// </summary>
public sealed class AttemptLimiterOptions
{
    public const string Section = "Identity:AttemptLimiter";
    public const int MinimumTimeoutMilliseconds = 10;
    public const int MaximumTimeoutMilliseconds = 5_000;

    public int TimeoutMilliseconds { get; set; } = 250;

    public bool IsValid() => TimeoutMilliseconds is >= MinimumTimeoutMilliseconds and <= MaximumTimeoutMilliseconds;
}
