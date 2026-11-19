namespace Frappe.Http;

/// <summary>
/// Where every replica keeps the answers of requests sent with an <c>Idempotency-Key</c> (HTTP-26), so a retry gets the
/// same answer whichever replica receives it. With Valkey registered (<c>AddFrappeCaching</c>) the answers live there; without it, each
/// replica keeps its own in memory, which only protects a retry that lands on the same replica. A host may register its own store first. Keys arrive already
/// scoped to the endpoint and the caller; implementations store them as opaque text.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Claims <paramref name="key"/> for a request whose content is <paramref name="fingerprint"/>, atomically, so two
    /// concurrent requests never both run: <see cref="IdempotencyOutcome.Claimed"/> for the one that may run, with an
    /// owner token of its own (the claim lasts <see cref="IdempotencyKey.InFlightLifetime"/> unless completed or
    /// released), <see cref="IdempotencyOutcome.Replay"/> with the stored answer when the first request with the same fingerprint
    /// finished, <see cref="IdempotencyOutcome.InFlight"/> when it is still running, and
    /// <see cref="IdempotencyOutcome.Reused"/> when the key was claimed or answered for another fingerprint.
    /// </summary>
    ValueTask<IdempotencyClaim> ClaimAsync(string key, string fingerprint, CancellationToken cancellationToken);

    /// <summary>
    /// Stores the answer of the request that claimed <paramref name="key"/> for <see cref="IdempotencyKey.Retention"/>,
    /// atomically and only while <paramref name="owner"/> (<see cref="IdempotencyClaim.Owner"/>) still holds the claim:
    /// once the claim expired, the key may belong to a request that claimed it again, and a slow request never overwrites
    /// that one's claim or answer. <see langword="true"/> when the answer was stored.
    /// </summary>
    ValueTask<bool> CompleteAsync(string key, string owner, string fingerprint, StoredResponse response, CancellationToken cancellationToken);

    /// <summary>
    /// Gives <paramref name="key"/> up (the request failed, so a retry may run), without storing anything, atomically and
    /// only while <paramref name="owner"/> still holds the claim. <see langword="true"/> when the claim was given up.
    /// </summary>
    ValueTask<bool> ReleaseAsync(string key, string owner, CancellationToken cancellationToken);
}
