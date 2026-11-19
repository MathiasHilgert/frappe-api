namespace Frappe.Results;

/// <summary>
/// The caller tried too often: the operation is refused, whatever else is true, until <see cref="RetryAfterSeconds"/>
/// have passed. The wait travels as the <c>retryAfter</c> parameter (whole seconds, rounded up, at least one), so every
/// adapter tells it the same way.
/// </summary>
public sealed record RateLimitedError : Error
{
    /// <summary>The name of the parameter that carries <see cref="RetryAfterSeconds"/>.</summary>
    public const string RetryAfterParameter = "retryAfter";

    public RateLimitedError(string code, TimeSpan retryAfter, params (string Name, object? Value)[] parameters)
        : base(code, [.. parameters ?? [], (RetryAfterParameter, SecondsOf(retryAfter))])
    {
        RetryAfterSeconds = SecondsOf(retryAfter);
    }

    /// <summary>Whole seconds to wait before trying again: at least one.</summary>
    public int RetryAfterSeconds { get; }

    private static int SecondsOf(TimeSpan retryAfter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(retryAfter, TimeSpan.Zero);
        return (int)Math.Clamp(Math.Ceiling(retryAfter.TotalSeconds), 1, int.MaxValue);
    }
}
