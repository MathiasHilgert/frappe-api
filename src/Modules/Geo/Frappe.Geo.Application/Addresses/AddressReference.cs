using System.Buffers.Text;
using System.Text;

namespace Frappe.Geo.Application.Addresses;

/// <summary>
/// The id clients get for a suggestion and for a resolved address: the provider's place id, tagged with the provider and
/// a format version, base64url-encoded. Opaque to clients, so the provider can change without changing the API; a
/// reference of another provider simply resolves to nothing. It is not signed: a client that crafts one only reaches a
/// place the provider would also show it.
/// </summary>
public sealed record AddressReference(string Provider, string ProviderId)
{
    private const string Version = "a1";
    private const int MaxProviderIdLength = 512;

    public string Encode() => Base64Url.EncodeToString(Encoding.UTF8.GetBytes($"{Version}:{Provider}:{ProviderId}"));

    /// <summary>The reference <paramref name="raw"/> encodes, or <see langword="null"/> when it is not one Geo issued.</summary>
    public static AddressReference? Decode(string? raw)
    {
        if (string.IsNullOrEmpty(raw) || raw.Length > 2 * MaxProviderIdLength || !Base64Url.IsValid(raw))
        {
            return null;
        }

        var parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(raw)).Split(':');
        return parts is [Version, var provider, var id] && IsProvider(provider) && IsProviderId(id)
            ? new AddressReference(provider, id)
            : null;
    }

    private static bool IsProvider(string provider) => provider.Length is >= 1 and <= 16 && provider.All(char.IsAsciiLetterLower);

    /// <summary>Provider ids are URL-safe tokens (place ids are base64url-like): anything else is not one.</summary>
    private static bool IsProviderId(string id) =>
        id.Length is >= 1 and <= MaxProviderIdLength && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
