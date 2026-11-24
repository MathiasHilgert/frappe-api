using System.Buffers.Text;
using System.Security.Cryptography;
using Frappe.Identity.Domain.Codes;
using NodaTime;

namespace Frappe.Identity.Application.Codes;

/// <summary>
/// Issues and finds the single-use tokens that undo an email change (S15). A token is <c>{id}.{secret}</c>: the id finds the row (the route has
/// no principal) and the secret is 256 random bits, stored only as its HMAC, so guessing it needs no limiter. A change replaces only the token of
/// its own pair of addresses: the way back from an earlier change stays open.
/// </summary>
public sealed class EmailChangeUndoTokens(IEmailChangeUndos undos, ICodeHasher hasher, TimeProvider time)
{
    private const int SecretBytes = 32;

    private const int MaxSecretLength = 64;

    private const int IdLength = 32;

    /// <summary>
    /// A new token for the change of <paramref name="oldAddress"/> to <paramref name="newAddress"/> (both normalized) of
    /// <paramref name="principalId"/>, in plain text: it exists only here and in the event that mails it.
    /// </summary>
    public async Task<string> IssueAsync(Guid principalId, string oldAddress, string newAddress, CancellationToken cancellationToken)
    {
        var now = Instant.FromDateTimeOffset(time.GetUtcNow());
        (await undos.LiveOfPairAsync(principalId, oldAddress, newAddress, cancellationToken))?.Invalidate(now);
        var secret = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(SecretBytes));
        var undo = EmailChangeUndo.Issue(principalId, oldAddress, newAddress, hasher.Hash(secret), now);
        undos.Add(undo);
        return $"{undo.Id:N}.{secret}";
    }

    /// <summary>
    /// The live token <paramref name="token"/> names, or <see langword="null"/> when it is malformed, unknown, wrong, expired, used or replaced.
    /// The secret is hashed whatever the id finds, so the answers cost the same.
    /// </summary>
    public async Task<EmailChangeUndo?> FindLiveAsync(string? token, CancellationToken cancellationToken)
    {
        var (id, secret) = Parse(token);
        var found = id is null ? null : await undos.FindAsync(id.Value, cancellationToken);
        if (found is null)
        {
            _ = hasher.Hash(secret ?? "-");
            return null;
        }

        var now = Instant.FromDateTimeOffset(time.GetUtcNow());
        return hasher.Matches(found.TokenHmac, secret!) && found.IsLive(now) ? found : null;
    }

    // The length is bounded before the split: the input is anonymous and unbounded.
    private static (Guid? Id, string? Secret) Parse(string? token)
    {
        if (token is null or { Length: > IdLength + 1 + MaxSecretLength })
        {
            return (null, null);
        }

        var parts = token.Split('.');
        return parts is [var id, { Length: > 0 } secret] && Guid.TryParseExact(id, "N", out var guid) ? (guid, secret) : (null, null);
    }
}
