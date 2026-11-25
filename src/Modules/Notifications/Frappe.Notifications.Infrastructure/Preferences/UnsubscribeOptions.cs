namespace Frappe.Notifications.Infrastructure.Preferences;

/// <summary>
/// The key unsubscribe tokens are signed with (S5, S12), only from the environment: <c>Notifications__Unsubscribe__SigningKey</c> as base64 of at
/// least <see cref="MinKeyBytes"/> bytes, named by <c>Notifications__Unsubscribe__CurrentKeyId</c>. Rotation sets a new key and a new id and moves
/// the old one to <c>Notifications__Unsubscribe__RetiredKeys__&lt;id&gt;</c>, which only verifies: tokens never expire, so links already mailed keep
/// working. The Api and the Worker need the same keys.
/// </summary>
public sealed class UnsubscribeOptions
{
    public const string Section = "Notifications:Unsubscribe";

    /// <summary>The shortest key accepted: the size of the HMAC-SHA256 output.</summary>
    public const int MinKeyBytes = 32;

    /// <summary><c>CurrentKeyId</c>: the id of <see cref="SigningKey"/>, written into every new token. Default 1. Range positive.</summary>
    public short CurrentKeyId { get; set; } = 1;

    /// <summary><c>SigningKey</c>: base64 of at least 32 bytes. No default: required wherever links are made or read. Never logged.</summary>
    public string? SigningKey { get; set; }

    /// <summary>Base64 keys by id that only verify (a rotation's earlier keys); every id is positive and differs from the current one.</summary>
    public Dictionary<short, string> RetiredKeys { get; } = [];
}
