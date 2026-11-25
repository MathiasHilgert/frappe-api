namespace Frappe.Notifications.Infrastructure.Contacts;

/// <summary>
/// The keys addresses are hashed with (S12, S13), only from the environment: <c>Notifications__AddressHash__CurrentKeyId</c> and
/// <c>Notifications__AddressHash__Keys__&lt;id&gt;</c> as base64 of at least <see cref="MinKeyBytes"/> bytes. Rotation adds a key and moves the
/// current id; the old keys stay so existing hashes still match.
/// </summary>
public sealed class AddressHashOptions
{
    public const string Section = "Notifications:AddressHash";

    /// <summary>The shortest key accepted: the size of the HMAC-SHA256 output.</summary>
    public const int MinKeyBytes = 32;

    /// <summary>The id of the key new hashes use.</summary>
    public short CurrentKeyId { get; set; }

    /// <summary>Base64 keys by id; every id is positive.</summary>
    public Dictionary<short, string> Keys { get; } = [];
}
