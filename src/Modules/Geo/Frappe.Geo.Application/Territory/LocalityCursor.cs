using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace Frappe.Geo.Application.Territory;

/// <summary>
/// Where a page of localities ended: the offset into the ordering of one listing or search. Opaque to clients
/// (base64url); it is only valid with the same subdivision and search, which a client cannot change mid-way since the
/// cursor carries a fingerprint of both.
/// </summary>
public sealed record LocalityCursor(int Offset, string Fingerprint)
{
    public static string FingerprintOf(string subdivision, string? search) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(subdivision + "\n" + (search ?? string.Empty))))[..16];

    public string Encode() => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(Offset.ToString(CultureInfo.InvariantCulture) + "." + Fingerprint));

    public static LocalityCursor? Decode(string? raw)
    {
        if (string.IsNullOrEmpty(raw) || !Base64Url.IsValid(raw))
        {
            return null;
        }

        var parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(raw)).Split('.');
        return parts.Length == 2 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var offset) && offset > 0
            ? new LocalityCursor(offset, parts[1])
            : null;
    }
}
