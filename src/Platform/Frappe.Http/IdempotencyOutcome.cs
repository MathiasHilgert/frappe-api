namespace Frappe.Http;

/// <summary>What <see cref="IIdempotencyStore.ClaimAsync"/> found.</summary>
public enum IdempotencyOutcome
{
    /// <summary>Nobody used the key: this request runs.</summary>
    Claimed,

    /// <summary>An earlier request finished: its stored answer is the answer.</summary>
    Replay,

    /// <summary>An earlier request with the same content is still running.</summary>
    InFlight,

    /// <summary>The key belongs to a request with other content.</summary>
    Reused,
}
