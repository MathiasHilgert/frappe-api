using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Frappe.Caching;

namespace Frappe.Http;

/// <summary>
/// The answers of requests sent with an <c>Idempotency-Key</c>, kept in Valkey so every replica replays the same one.
/// A key is claimed with one atomic set-if-absent that holds it for <see cref="IdempotencyKey.InFlightLifetime"/>, then
/// overwritten with the answer for <see cref="IdempotencyKey.Retention"/>; the answer and a release each replace or delete
/// the claim in one atomic script, only while the stored claim is still the caller's (its fingerprint and owner token). The key is hashed, so its text never reaches
/// Valkey's keyspace.
/// </summary>
internal sealed class ValkeyIdempotencyStore(ValkeyValues values) : IIdempotencyStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>A claim (<see cref="Response"/> none, <see cref="Owner"/> the token of its request) or an answer, as stored.</summary>
    private sealed record Entry(string Fingerprint, StoredResponse? Response, string? Owner = null);

    public async ValueTask<IdempotencyClaim> ClaimAsync(string key, string fingerprint, CancellationToken cancellationToken)
    {
        var name = NameOf(key);
        var owner = Guid.NewGuid().ToString("N");
        if (await values.TryAddAsync(name, Claim(fingerprint, owner), IdempotencyKey.InFlightLifetime, cancellationToken))
        {
            return new IdempotencyClaim(IdempotencyOutcome.Claimed, Owner: owner);
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

    public async ValueTask<bool> CompleteAsync(string key, string owner, string fingerprint, StoredResponse response, CancellationToken cancellationToken) =>
        await values.ReplaceIfAsync(NameOf(key), Claim(fingerprint, owner), Serialize(new Entry(fingerprint, response)), IdempotencyKey.Retention, cancellationToken);

    /// <summary>
    /// Reads the claim to learn the exact bytes <paramref name="owner"/> stored, then deletes it in one atomic script only
    /// while those bytes are still there, so a claim or an answer written in between survives.
    /// </summary>
    public async ValueTask<bool> ReleaseAsync(string key, string owner, CancellationToken cancellationToken)
    {
        var name = NameOf(key);
        var stored = await values.GetAsync(name, cancellationToken);
        if (stored is null || JsonSerializer.Deserialize<Entry>(stored, Json) is not { Response: null, Owner: { } holder } entry
            || !string.Equals(holder, owner, StringComparison.Ordinal))
        {
            return false;
        }

        return await values.DeleteIfAsync(name, Claim(entry.Fingerprint, owner), cancellationToken);
    }

    private static string NameOf(string key) => "idempotency:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    /// <summary>The stored claim of <paramref name="owner"/>: the same bytes every time, so a later write can compare them.</summary>
    private static byte[] Claim(string fingerprint, string owner) => Serialize(new Entry(fingerprint, null, owner));

    private static byte[] Serialize(Entry entry) => JsonSerializer.SerializeToUtf8Bytes(entry, Json);
}
