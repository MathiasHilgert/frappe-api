using System.Buffers.Text;
using System.Security.Cryptography;
using Frappe.Identity.Application.Sessions;

namespace Frappe.Identity.Infrastructure.Sessions;

/// <summary>
/// The session token format (S8): <c>fs_</c> and the base64url of 32 random bytes, 46 characters, so a later scheme is told apart by its
/// prefix. What is stored is the SHA-256 of the 32 bytes. A text is only accepted when it is exactly what <see cref="Issue"/> writes, so one
/// session has one spelling and nothing else is ever looked up.
/// </summary>
internal sealed class SessionTokens : ISessionTokens
{
    /// <summary>The prefix every token starts with.</summary>
    public const string Prefix = "fs_";

    /// <summary>The random bytes of a token: 256 bits.</summary>
    public const int SecretBytes = 32;

    private const int EncodedLength = 43;

    /// <summary>The characters whose two low bits are zero (values 0, 4, 8, ... 60): the only last ones of 32 bytes (4 data bits, 2 spare).</summary>
    private const string LastCharacters = "AEIMQUYcgkosw048";

    public IssuedToken Issue()
    {
        var secret = RandomNumberGenerator.GetBytes(SecretBytes);
        return new IssuedToken(Prefix + Base64Url.EncodeToString(secret), SHA256.HashData(secret));
    }

    public byte[]? HashOf(string? token)
    {
        if (token is null || token.Length != Prefix.Length + EncodedLength || !token.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        // The last character carries two unused bits: a text that sets them is another spelling of the same bytes.
        var encoded = token.AsSpan(Prefix.Length);
        if (!LastCharacters.Contains(encoded[^1]) || !Base64Url.IsValid(encoded, out var decodedLength) || decodedLength != SecretBytes)
        {
            return null;
        }

        Span<byte> secret = stackalloc byte[SecretBytes];
        return Base64Url.TryDecodeFromChars(encoded, secret, out var written) && written == SecretBytes ? SHA256.HashData(secret) : null;
    }
}
