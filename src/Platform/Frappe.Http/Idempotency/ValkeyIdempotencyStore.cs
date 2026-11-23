using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Frappe.Caching;

namespace Frappe.Http.Idempotency;

/// <summary>
/// The answers of requests sent with an <c>Idempotency-Key</c>, kept in Valkey so every Api replica replays the same one.
/// A key is claimed with one atomic set-if-absent that holds it for <see cref="IdempotencyKey.InFlightLifetime"/>, then
/// overwritten with the answer for <see cref="IdempotencyKey.Retention"/>. The key is hashed, so its text never reaches
/// Valkey's keyspace.
/// </summary>
internal sealed class ValkeyIdempotencyStore(ValkeyValues values) : IIdempotencyStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>A claim (<see cref="Response"/> none) or an answer, as stored.</summary>
    private sealed record Entry(string Fingerprint, StoredResponse? Response);

    public async ValueTask<IdempotencyClaim> ClaimAsync(string key, string fingerprint, CancellationToken cancellationToken)
    {
        var name = NameOf(key);
        var claim = Serialize(new Entry(fingerprint, null));
        if (await values.TryAddAsync(name, claim, IdempotencyKey.InFlightLifetime, cancellationToken))
        {
            return new IdempotencyClaim(IdempotencyOutcome.Claimed);
        }

        // The holder may release or finish between the two calls; a key that vanished is still being settled.
        var stored = await values.GetAsync(name, cancellationToken);
        if (stored is null)
        {
            return new IdempotencyClaim(IdempotencyOutcome.InFlight);
        }

        var entry = JsonSerializer.Deserialize<Entry>(stored, Json)!;
        if (!string.Equals(entry.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            return new IdempotencyClaim(IdempotencyOutcome.Reused);
        }

        return entry.Response is { } response
            ? new IdempotencyClaim(IdempotencyOutcome.Replay, response)
            : new IdempotencyClaim(IdempotencyOutcome.InFlight);
    }

    public async ValueTask CompleteAsync(string key, string fingerprint, StoredResponse response, CancellationToken cancellationToken) =>
        await values.SetAsync(NameOf(key), Serialize(new Entry(fingerprint, response)), IdempotencyKey.Retention, cancellationToken);

    public async ValueTask ReleaseAsync(string key, CancellationToken cancellationToken) =>
        await values.DeleteAsync(NameOf(key), cancellationToken);

    private static string NameOf(string key) => "idempotency:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    private static byte[] Serialize(Entry entry) => JsonSerializer.SerializeToUtf8Bytes(entry, Json);
}
