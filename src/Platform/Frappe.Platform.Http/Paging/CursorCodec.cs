using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Frappe.Kernel;
using Frappe.Platform.Http.Problems;
using Microsoft.AspNetCore.DataProtection;

namespace Frappe.Platform.Http.Paging;

/// <summary>
/// Encodes the keyset position of a page as an opaque, tamper-proof cursor bound to the list's scope (its sort and
/// filters), so a cursor never moves to another list.
/// </summary>
public sealed class CursorCodec
{
    private const string Purpose = "paging.cursor";

    private readonly IDataProtector _protector;

    /// <summary>Initializes the codec with the app's data protection.</summary>
    /// <param name="provider">The data protection provider.</param>
    public CursorCodec(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(Purpose);
    }

    /// <summary>Encodes the keyset values of the last item of a page.</summary>
    /// <param name="keys">The keyset values, in sort order.</param>
    /// <param name="scope">The sort and filters of the list, such as <c>name:asc|country=AR</c>.</param>
    /// <returns>The base64url cursor.</returns>
    public string Encode(IReadOnlyList<string> keys, string scope)
    {
        ArgumentNullException.ThrowIfNull(keys);
        string[] payload = [Hash(scope), .. keys];
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(
            payload,
            FrappeJsonContext.Default.StringArray
        );
        return Base64Url.EncodeToString(_protector.Protect(json));
    }

    /// <summary>Decodes a cursor issued by <see cref="Encode"/> for the same scope.</summary>
    /// <param name="cursor">The cursor the client sent.</param>
    /// <param name="scope">The sort and filters of the current request.</param>
    /// <returns>The keyset values, or <see cref="PlatformErrors.InvalidCursor"/> at <c>/cursor</c>.</returns>
    public ErrorOr<IReadOnlyList<string>> Decode(string? cursor, string scope)
    {
        string[]? payload = null;
        try
        {
            byte[] json = _protector.Unprotect(Base64Url.DecodeFromChars(cursor));
            payload = JsonSerializer.Deserialize(json, FrappeJsonContext.Default.StringArray);
        }
        catch (Exception exception)
            when (exception is FormatException or CryptographicException or JsonException)
        {
            // Falls through to the invalid cursor below.
        }

        ErrorOr<IReadOnlyList<string>> result =
            payload is [var hash, .. var keys]
            && string.Equals(hash, Hash(scope), StringComparison.Ordinal)
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
