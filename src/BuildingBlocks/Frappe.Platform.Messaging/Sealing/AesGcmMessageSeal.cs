using System.Security.Cryptography;
using System.Text;
using Frappe.Application.Abstractions.Messaging;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Frappe.Platform.Messaging.Sealing;

/// <summary>
/// AES-256-GCM under a key derived (HKDF-SHA256) from <see cref="MessageSealOptions.Key"/> with its own label, so
/// every instance opens what any other sealed. The sealed form is base64url of nonce, ciphertext and tag.
/// </summary>
public sealed class AesGcmMessageSeal(IOptions<MessageSealOptions> options) : IMessageSeal
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] Label = "frappe.messaging.seal.v1"u8.ToArray();

    private readonly byte[] _key = HKDF.DeriveKey(
        HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(options.Value.Key ?? string.Empty), 32, info: Label);

    public string Seal(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        var plaintext = Encoding.UTF8.GetBytes(secret);
        var sealedBytes = new byte[NonceSize + plaintext.Length + TagSize];
        var nonce = sealedBytes.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintext, sealedBytes.AsSpan(NonceSize, plaintext.Length), sealedBytes.AsSpan(NonceSize + plaintext.Length));
        return WebEncoders.Base64UrlEncode(sealedBytes);
    }

    public string Open(string sealedSecret)
    {
        ArgumentNullException.ThrowIfNull(sealedSecret);

        var sealedBytes = WebEncoders.Base64UrlDecode(sealedSecret);
        if (sealedBytes.Length < NonceSize + TagSize)
            throw new CryptographicException("The sealed secret is too short.");

        var length = sealedBytes.Length - NonceSize - TagSize;
        var plaintext = new byte[length];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(sealedBytes.AsSpan(0, NonceSize), sealedBytes.AsSpan(NonceSize, length),
            sealedBytes.AsSpan(NonceSize + length), plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }
}
