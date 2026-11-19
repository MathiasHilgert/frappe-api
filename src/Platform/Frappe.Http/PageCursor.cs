using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Frappe.Http;

/// <summary>
/// The opaque cursor of a paged list: base64url of a JSON object holding the format's version (so an older format never
/// reads as a position), a fingerprint of the scope it was issued for, and the position the page ended at. Keyset
/// paging: the next page starts right after that position, so rows added or removed between pages never make a page
/// repeat or skip one, and the database never counts its way past the earlier pages.
/// </summary>
internal static class PageCursor
{
    /// <summary>The format's version, so a cursor of any other format is refused as invalid.</summary>
    private const string Version = "p1";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The cursor payload before it is encoded: format version, scope fingerprint and the position.</summary>
    private sealed record Envelope<T>(string? V, string? S, T? P);

    public static string Encode<TPosition>(TPosition position, string scope) =>
        Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new Envelope<TPosition>(Version, Fingerprint(scope), position), Json));

    /// <summary>The position, or <see langword="null"/> when the text is not a cursor issued for <paramref name="scope"/>.</summary>
    public static TPosition? Decode<TPosition>(string raw, string scope)
        where TPosition : class
    {
        if (string.IsNullOrEmpty(raw) || !Base64Url.IsValid(raw))
        {
            return null;
        }

        try
        {
            var envelope = JsonSerializer.Deserialize<Envelope<TPosition>>(Base64Url.DecodeFromChars(raw), Json);
            return envelope is { V: Version, P: { } position } && envelope.S == Fingerprint(scope) ? position : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Fingerprint(string scope) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(scope)))[..16];
}
