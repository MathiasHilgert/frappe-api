using System.Security.Cryptography;
using System.Text;
using Frappe.Identity.Application.Credentials;
using Frappe.Identity.Domain.Credentials;
using Geralt;
using Microsoft.Extensions.Options;

namespace Frappe.Identity.Infrastructure.Credentials;

/// <summary>
/// Argon2id through libsodium (Geralt) under the parameters of <see cref="PasswordOptions"/>. The stored text is the standard encoded hash
/// (<c>$argon2id$v=19$m=…,t=…,p=1$salt$hash</c>), so it carries its own parameters and salt and keeps verifying after the options change.
/// </summary>
public sealed class Argon2idPasswordHasher(IOptions<PasswordOptions> options, IdentityMetrics metrics, TimeProvider time) : IPasswordHasher
{
    private const int BytesPerMebibyte = 1024 * 1024;

    private readonly int _iterations = options.Value.Iterations;
    private readonly int _memoryBytes = options.Value.MemoryMebibytes * BytesPerMebibyte;

    public PasswordHash Hash(Password password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var started = time.GetTimestamp();
        var secret = Encoding.UTF8.GetBytes(password.Value);
        var hash = new char[Argon2id.HashSize];
        try
        {
            Argon2id.ComputeHash(hash, secret, _iterations, _memoryBytes);
            return new PasswordHash(EncodedText(hash));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
            metrics.PasswordHashed(time.GetElapsedTime(started));
        }
    }

    public bool Verify(PasswordHash hash, string candidate)
    {
        ArgumentNullException.ThrowIfNull(hash);
        ArgumentNullException.ThrowIfNull(candidate);
        var started = time.GetTimestamp();
        var secret = Encoding.UTF8.GetBytes(candidate);
        try
        {
            return Encoded(hash) is { } encoded && Argon2id.VerifyHash(encoded, secret);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            // A stored value that is not an Argon2id hash never matches; it is not a reason to fail the log-in.
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
            metrics.PasswordHashed(time.GetElapsedTime(started));
        }
    }

    public bool NeedsRehash(PasswordHash hash)
    {
        ArgumentNullException.ThrowIfNull(hash);
        try
        {
            return Encoded(hash) is { } encoded && Argon2id.NeedsRehash(encoded, _iterations, _memoryBytes);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            return false;
        }
    }

    /// <summary>The hash as the library reads it: its text zero padded to <see cref="Argon2id.HashSize"/>, or null if too long.</summary>
    private static char[]? Encoded(PasswordHash hash)
    {
        if (hash.Value.Length >= Argon2id.HashSize)
        {
            return null;
        }

        var padded = new char[Argon2id.HashSize];
        hash.Value.CopyTo(padded);
        return padded;
    }

    private static string EncodedText(char[] hash)
    {
        var end = Array.IndexOf(hash, '\0');
        return new string(hash, 0, end < 0 ? hash.Length : end);
    }
}
