using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Frappe.Notifications.Application.Ports;
using Frappe.Notifications.Domain.Deliveries;
using Frappe.Persistence;
using Frappe.Results;
using Microsoft.Extensions.Options;

namespace Frappe.Notifications.Infrastructure.Unsubscribe;

/// <summary>
/// HMAC-SHA256 unsubscribe tokens (S5): <c>&lt;key id&gt;.&lt;payload&gt;.&lt;mac&gt;</c>, the payload and the MAC in base64url. The payload is the
/// category and the recipient key: signed, not hidden (anyone holding the mail can decode them; the token is no encryption). The MAC covers the
/// key id and the payload under a context label, so a MAC made for anything else (an address hash, another key) never verifies. There is no
/// timestamp and no randomness: the same pair always gives the same token, so a mail renders byte for byte alike on every attempt (S14), and a
/// link never expires. Only that one text verifies: a padded or reformatted spelling of the same bytes is refused. The key id
/// lets a rotation keep verifying old links (S12).
/// </summary>
public sealed class HmacUnsubscribeTokens : IUnsubscribeTokens
{
    /// <summary>The longest token read: the longest key and its envelope fit well inside.</summary>
    internal const int MaxTokenLength = 512;

    private const char Separator = '.';

    private const char PayloadSeparator = '\n';

    private const string Context = "frappe.notifications.unsubscribe.v1";

    private readonly short _currentKeyId;
    private readonly Dictionary<short, byte[]> _keys = [];

    public HmacUnsubscribeTokens(IOptions<UnsubscribeOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var value = options.Value;
        _currentKeyId = value.CurrentKeyId;
        _keys[value.CurrentKeyId] = Convert.FromBase64String(value.SigningKey!);
        foreach (var (id, key) in value.RetiredKeys)
        {
            _keys[id] = Convert.FromBase64String(key);
        }
    }

    public string Issue(RecipientKey recipient, NotificationCategory category)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        if (!category.CanOptOut)
        {
            throw new ArgumentException($"A {category} notification has no unsubscribe link.", nameof(category));
        }

        var payload = Encoding.UTF8.GetBytes(StoredEnum<NotificationCategory>.ToText(category) + PayloadSeparator + recipient.Value);
        var mac = Sign(_currentKeyId, _keys[_currentKeyId], payload);
        return Text(_currentKeyId, payload, mac);
    }

    private static string Text(short keyId, byte[] payload, byte[] mac) =>
        string.Create(
            CultureInfo.InvariantCulture, $"{keyId}{Separator}{Base64Url.EncodeToString(payload)}{Separator}{Base64Url.EncodeToString(mac)}");

    public Result<UnsubscribeClaim> Verify(string? token)
    {
        if (token is not { Length: > 0 and <= MaxTokenLength })
        {
            return UnsubscribeToken.Invalid();
        }

        var parts = token.Split(Separator);
        if (parts.Length != 3
            || !short.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var keyId)
            || !_keys.TryGetValue(keyId, out var key)
            || !Base64Url.IsValid(parts[1]) || !Base64Url.IsValid(parts[2]))
        {
            return UnsubscribeToken.Invalid();
        }

        var payload = Base64Url.DecodeFromChars(parts[1]);
        var mac = Base64Url.DecodeFromChars(parts[2]);
        if (!CryptographicOperations.FixedTimeEquals(Sign(keyId, key, payload), mac))
        {
            return UnsubscribeToken.Invalid();
        }

        // Only the text Issue writes is ours: a padded, zero-led or otherwise reformatted spelling of the same bytes is not, so one link has one
        // text (and one rate-limit key).
        return string.Equals(token, Text(keyId, payload, mac), StringComparison.Ordinal) ? ClaimOf(payload) : UnsubscribeToken.Invalid();
    }

    /// <summary>Reads the payload of a token whose MAC held: ours, so anything off about it is still refused, never an exception.</summary>
    private static Result<UnsubscribeClaim> ClaimOf(byte[] payload)
    {
        var text = Encoding.UTF8.GetString(payload);
        var split = text.IndexOf(PayloadSeparator, StringComparison.Ordinal);
        if (split < 0)
        {
            return UnsubscribeToken.Invalid();
        }

        var category = Enum.GetValues<NotificationCategory>()
            .Where(candidate => candidate.CanOptOut && StoredEnum<NotificationCategory>.ToText(candidate) == text[..split])
            .Select(candidate => (NotificationCategory?)candidate)
            .FirstOrDefault();
        var recipient = RecipientKey.Parse(text[(split + 1)..]);
        return category is { } found && recipient.IsSuccess ? new UnsubscribeClaim(recipient.Value, found) : UnsubscribeToken.Invalid();
    }

    private static byte[] Sign(short keyId, byte[] key, byte[] payload)
    {
        var header = Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{Context}{PayloadSeparator}{keyId}{PayloadSeparator}"));
        byte[] message = [.. header, .. payload];
        return HMACSHA256.HashData(key, message);
    }
}
