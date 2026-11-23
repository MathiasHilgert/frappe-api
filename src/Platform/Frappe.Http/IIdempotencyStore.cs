namespace Frappe.Http;

/// <summary>
/// Where every replica keeps the answers of requests sent with an <c>Idempotency-Key</c> (HTTP-26), so a retry gets the
/// same answer whichever replica receives it. A host registers one that survives replicas (Valkey); without one, each
/// replica keeps its own in memory, which only protects a retry that lands on the same replica. Keys arrive already
/// scoped to the endpoint and the caller; implementations store them as opaque text.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Claims <paramref name="key"/> for a request whose content is <paramref name="fingerprint"/>, atomically, so two
    /// concurrent requests never both run: <see cref="IdempotencyOutcome.Claimed"/> for the one that may run (the claim
    /// lasts <see cref="IdempotencyKey.InFlightLifetime"/> unless completed or released),
    /// <see cref="IdempotencyOutcome.Replay"/> with the stored answer when an earlier request with the same fingerprint
    /// finished, <see cref="IdempotencyOutcome.InFlight"/> when it is still running, and
    /// <see cref="IdempotencyOutcome.Reused"/> when the key was claimed or answered for another fingerprint.
    /// </summary>
    ValueTask<IdempotencyClaim> ClaimAsync(string key, string fingerprint, CancellationToken cancellationToken);

    /// <summary>Stores the answer of the request that claimed <paramref name="key"/> for <see cref="IdempotencyKey.Retention"/>.</summary>
    ValueTask CompleteAsync(string key, string fingerprint, StoredResponse response, CancellationToken cancellationToken);

    /// <summary>Gives <paramref name="key"/> up (the request failed, so a retry may run), without storing anything.</summary>
    ValueTask ReleaseAsync(string key, CancellationToken cancellationToken);
}
