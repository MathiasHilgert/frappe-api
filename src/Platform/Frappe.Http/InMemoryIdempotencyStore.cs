namespace Frappe.Http;

/// <summary>
/// The <see cref="IIdempotencyStore"/> of a process that has no shared one: correct for one replica, and for a retry
/// that lands on the same replica. Entries expire on their own and are dropped when met, and again on every claim, so
/// the store never outgrows its retention.
/// </summary>
internal sealed class InMemoryIdempotencyStore(TimeProvider time) : IIdempotencyStore
{
    /// <summary>
    /// One stored key: the request fingerprint it was claimed with, the owner token of the claim, the answer once there is
    /// one, and when the key stops counting.
    /// </summary>
    private sealed record Entry(string Fingerprint, string Owner, StoredResponse? Response, DateTimeOffset ExpiresAt);

    private readonly Dictionary<string, Entry> _entries = [with(StringComparer.Ordinal)];
    private readonly Lock _gate = new();

    public ValueTask<IdempotencyClaim> ClaimAsync(string key, string fingerprint, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var now = time.GetUtcNow();
            foreach (var expired in _entries.Where(e => e.Value.ExpiresAt <= now).Select(e => e.Key).ToArray())
            {
                _entries.Remove(expired);
            }

            if (!_entries.TryGetValue(key, out var existing))
            {
                var owner = Guid.NewGuid().ToString("N");
                _entries[key] = new Entry(fingerprint, owner, null, now + IdempotencyKey.InFlightLifetime);
                return ValueTask.FromResult(new IdempotencyClaim(IdempotencyOutcome.Claimed, Owner: owner));
            }

            if (!string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                return ValueTask.FromResult(new IdempotencyClaim(IdempotencyOutcome.Reused));
            }

            return ValueTask.FromResult(existing.Response is { } response
                ? new IdempotencyClaim(IdempotencyOutcome.Replay, response)
                : new IdempotencyClaim(IdempotencyOutcome.InFlight));
        }
    }

    public ValueTask<bool> CompleteAsync(string key, string owner, string fingerprint, StoredResponse response, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var now = time.GetUtcNow();
            if (!Holds(key, owner, now))
            {
                return ValueTask.FromResult(false);
            }

            _entries[key] = new Entry(fingerprint, owner, response, now + IdempotencyKey.Retention);
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<bool> ReleaseAsync(string key, string owner, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(Holds(key, owner, time.GetUtcNow()) && _entries.Remove(key));
        }
    }

    /// <summary>
    /// Whether <paramref name="owner"/> still holds the claim on <paramref name="key"/>: a claim not yet answered, taken by
    /// that owner, and not expired (an expired claim does not count, even while it waits for a later claim to drop it).
    /// </summary>
    private bool Holds(string key, string owner, DateTimeOffset now) =>
        _entries.TryGetValue(key, out var entry)
        && entry.Response is null
        && entry.ExpiresAt > now
        && string.Equals(entry.Owner, owner, StringComparison.Ordinal);
}
