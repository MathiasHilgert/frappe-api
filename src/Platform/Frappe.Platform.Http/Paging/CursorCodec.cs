using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Frappe.Kernel;
using Frappe.Platform.Http.Problems;

namespace Frappe.Platform.Http.Paging;

/// <summary>
/// Encodes the keyset position of a page as an opaque base64url cursor bound to the list's scope (its sort and
/// filters), so a cursor never moves to another list. It is a position, not a secret: a client that edits one only
/// moves its own page, and the query still applies every filter and the tenant.
/// </summary>
public static class CursorCodec
{
    /// <summary>Encodes the keyset values of the last item of a page.</summary>
    /// <param name="keys">The keyset values, in sort order.</param>
    /// <param name="scope">The sort and filters of the list, such as <c>name:asc|country=AR</c>.</param>
    /// <returns>The base64url cursor.</returns>
    public static string Encode(IReadOnlyList<string> keys, string scope)
    {
        ArgumentNullException.ThrowIfNull(keys);
        string[] payload = [Hash(scope), .. keys];
        return Base64Url.EncodeToString(
            JsonSerializer.SerializeToUtf8Bytes(payload, FrappeJsonContext.Default.StringArray)
        );
    }

    /// <summary>Decodes a cursor issued by <see cref="Encode"/> for the same scope.</summary>
    /// <param name="cursor">The cursor the client sent.</param>
    /// <param name="scope">The sort and filters of the current request.</param>
    /// <returns>The keyset values, or <see cref="PlatformErrors.InvalidCursor"/> at <c>/cursor</c>.</returns>
    public static ErrorOr<IReadOnlyList<string>> Decode(string? cursor, string scope)
    {
        string?[]? payload = null;
        try
        {
            payload = JsonSerializer.Deserialize(
                Base64Url.DecodeFromChars(cursor),
                FrappeJsonContext.Default.StringArray
            );
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            // Falls through to the invalid cursor below.
        }

        // OfType drops a null keyset value, so a shorter array means the cursor carried one.
        ErrorOr<IReadOnlyList<string>> result =
            payload is [var hash, .. var rest]
            && string.Equals(hash, Hash(scope), StringComparison.Ordinal)
            && rest.OfType<string>().ToArray() is var keys
            && keys.Length == rest.Length
                ? keys
                : PlatformErrors.InvalidCursor;
        return result.WithPointer("/cursor");
    }

    private static string Hash(string scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope)));
    }
}
