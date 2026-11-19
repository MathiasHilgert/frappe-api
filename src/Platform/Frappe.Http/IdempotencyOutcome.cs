namespace Frappe.Http;

/// <summary>What <see cref="IIdempotencyStore.ClaimAsync"/> found.</summary>
public enum IdempotencyOutcome
{
    /// <summary>Nobody used the key: this request runs.</summary>
    Claimed,

    /// <summary>The first request finished: its stored answer is the answer.</summary>
    Replay,

    /// <summary>The first request with the same content is still running.</summary>
    InFlight,

    /// <summary>The key belongs to a request with other content.</summary>
    Reused,
}
