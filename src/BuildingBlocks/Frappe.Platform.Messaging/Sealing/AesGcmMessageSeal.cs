using System.Security.Cryptography;
using System.Text;
using Frappe.Application.Abstractions.Messaging;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Frappe.Platform.Messaging.Sealing;

/// <summary>
/// AES-256-GCM under a key derived (HKDF-SHA256) from <see cref="MessageSealOptions.Key"/> with its own label, so
/// every instance opens what any other sealed. The sealed form is base64url of a format version byte (2), a key id
/// byte (the first byte of the derived key's SHA-256), nonce, ciphertext and tag; the header is authenticated as
/// associated data. The key id lets a rotation open seals made under <see cref="MessageSealOptions.PreviousKey"/>.
/// A seal without the header (version 1, before rotation existed) still opens under either key.
/// </summary>
public sealed class AesGcmMessageSeal : IMessageSeal
{
    private const byte Version = 2;
    private const int HeaderSize = 2;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] Label = "frappe.messaging.seal.v1"u8.ToArray();

    private readonly SealKey _current;
    private readonly SealKey[] _openers;

    public AesGcmMessageSeal(IOptions<MessageSealOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _current = SealKey.From(options.Value.Key ?? string.Empty);
        _openers = string.IsNullOrEmpty(options.Value.PreviousKey)
            ? [_current]
            : [_current, SealKey.From(options.Value.PreviousKey)];
    }

    public string Seal(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        var plaintext = Encoding.UTF8.GetBytes(secret);
        var sealedBytes = new byte[HeaderSize + NonceSize + plaintext.Length + TagSize];
        sealedBytes[0] = Version;
        sealedBytes[1] = _current.Id;
        var nonce = sealedBytes.AsSpan(HeaderSize, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_current.Bytes, TagSize);
        aes.Encrypt(nonce, plaintext, sealedBytes.AsSpan(HeaderSize + NonceSize, plaintext.Length),
            sealedBytes.AsSpan(HeaderSize + NonceSize + plaintext.Length), sealedBytes.AsSpan(0, HeaderSize));
        return WebEncoders.Base64UrlEncode(sealedBytes);
    }

    public string Open(string sealedSecret)
    {
        ArgumentNullException.ThrowIfNull(sealedSecret);

        var sealedBytes = WebEncoders.Base64UrlDecode(sealedSecret);
        if (sealedBytes.Length < NonceSize + TagSize)
            throw new CryptographicException("The sealed secret is too short.");

        if (sealedBytes.Length >= HeaderSize + NonceSize + TagSize && sealedBytes[0] == Version)
        {
            foreach (var key in _openers.Where(k => k.Id == sealedBytes[1]))
            {
                if (TryDecrypt(key, sealedBytes.AsSpan(HeaderSize), sealedBytes.AsSpan(0, HeaderSize), out var plaintext))
                    return plaintext;
            }
        }

        // Version 1: no header, nothing authenticated beside the ciphertext.
        foreach (var key in _openers)
        {
            if (TryDecrypt(key, sealedBytes, [], out var plaintext))
                return plaintext;
        }

        throw new CryptographicException("The sealed secret was tampered with or sealed by an unknown key.");
    }

    private static bool TryDecrypt(SealKey key, ReadOnlySpan<byte> body, ReadOnlySpan<byte> header, out string plaintext)
    {
        plaintext = string.Empty;
        var length = body.Length - NonceSize - TagSize;
        if (length < 0)
            return false;
        var buffer = new byte[length];
        using var aes = new AesGcm(key.Bytes, TagSize);
        try
        {
            aes.Decrypt(body[..NonceSize], body.Slice(NonceSize, length), body[(NonceSize + length)..], buffer, header);
        }
        catch (AuthenticationTagMismatchException)
        {
            return false;
        }

        plaintext = Encoding.UTF8.GetString(buffer);
        return true;
    }

    private sealed record SealKey(byte[] Bytes, byte Id)
    {
        public static SealKey From(string secret)
        {
            var bytes = HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(secret), 32, info: Label);
            return new SealKey(bytes, SHA256.HashData(bytes)[0]);
        }
    }
}
