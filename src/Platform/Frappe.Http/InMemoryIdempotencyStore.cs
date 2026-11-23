using System.Collections.Concurrent;

namespace Frappe.Http;

/// <summary>
/// The <see cref="IIdempotencyStore"/> of a process that has no shared one: correct for one replica, and for a retry
/// that lands on the same replica. Entries expire on their own and are dropped when met, and again on every claim, so
/// the store never outgrows its retention.
/// </summary>
internal sealed class InMemoryIdempotencyStore(TimeProvider time) : IIdempotencyStore
{
    /// <summary>One stored key: the request fingerprint it was claimed with, the answer once there is one, and when the key stops counting.</summary>
    private sealed record Entry(string Fingerprint, StoredResponse? Response, DateTimeOffset ExpiresAt);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public ValueTask<IdempotencyClaim> ClaimAsync(string key, string fingerprint, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var now = time.GetUtcNow();
            foreach (var expired in _entries.Where(e => e.Value.ExpiresAt <= now).Select(e => e.Key).ToArray())
            {
                _entries.TryRemove(expired, out _);
            }

            if (!_entries.TryGetValue(key, out var existing))
            {
                _entries[key] = new Entry(fingerprint, null, now + IdempotencyKey.InFlightLifetime);
                return ValueTask.FromResult(new IdempotencyClaim(IdempotencyOutcome.Claimed));
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

    public ValueTask CompleteAsync(string key, string fingerprint, StoredResponse response, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _entries[key] = new Entry(fingerprint, response, time.GetUtcNow() + IdempotencyKey.Retention);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask ReleaseAsync(string key, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _entries.TryRemove(key, out _);
        }

        return ValueTask.CompletedTask;
    }
}
