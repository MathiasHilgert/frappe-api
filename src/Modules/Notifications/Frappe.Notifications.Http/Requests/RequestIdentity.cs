using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Frappe.Notifications.Http.Requests;

/// <summary>
/// The identity of a send request: the same <c>Idempotency-Key</c> of the same caller always gives the same one, so a retried HTTP request
/// collapses on the dispatch's own idempotency (S2, S14). A request with no key is its own request, with a fresh identity.
/// </summary>
internal static class RequestIdentity
{
    /// <summary>The namespace of the name-based UUIDs of send requests (RFC 9562, section 5.5): fixed, random, never reused elsewhere.</summary>
    private static readonly Guid Namespace = new("5b2f2e4a-7c1d-4f43-9a1e-3d8d6b0c7a11");

    /// <summary>The identity for <paramref name="idempotencyKey"/> of the caller of <paramref name="context"/>; new when no key was sent.</summary>
    public static Guid Of(HttpContext context, string? idempotencyKey)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrEmpty(idempotencyKey))
        {
            return Guid.CreateVersion7();
        }

        var caller = context.User.FindFirst("sub")?.Value
            ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.User.Identity?.Name
            ?? string.Empty;
        return Version5(Namespace, $"{caller}\n{idempotencyKey}");
    }

    /// <summary>A name-based UUID version 5: the SHA-1 of the namespace and the name, with its version and variant bits set.</summary>
    internal static Guid Version5(Guid namespaceId, string name)
    {
        Span<byte> namespaceBytes = stackalloc byte[16];
        namespaceId.TryWriteBytes(namespaceBytes, bigEndian: true, out _);
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var input = new byte[16 + nameBytes.Length];
        namespaceBytes.CopyTo(input);
        nameBytes.CopyTo(input, 16);

        // RFC 9562 mandates SHA-1 for version 5: the hash names the request, it protects nothing.
#pragma warning disable CA5350, S4790
        var hash = SHA1.HashData(input);
#pragma warning restore CA5350, S4790
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }
}
