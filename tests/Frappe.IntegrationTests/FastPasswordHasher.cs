using System.Security.Cryptography;
using System.Text;
using Frappe.Identity.Application.Security;

namespace Frappe.IntegrationTests;

/// <summary>
/// Salted SHA-256 stand-in for Argon2id, so the suite does not spend ~19 MiB and two passes per hash.
/// Real hashing stays covered end to end by <see cref="Identity.RealPasswordHashingTests"/>. Hashes made with
/// <see cref="LegacyHash"/> verify too but report <see cref="NeedsRehash"/>, standing in for an outdated cost.
/// </summary>
internal sealed class FastPasswordHasher : IPasswordHasher
{
    private const string Prefix = "fast";
    private const string LegacyPrefix = "fast-legacy";

    public static string LegacyHash(string password) => Make(LegacyPrefix, password);

    public Task<string> HashAsync(string password, CancellationToken cancellationToken) =>
        Task.FromResult(Make(Prefix, password));

    public Task<bool> VerifyAsync(string password, string passwordHash, CancellationToken cancellationToken)
    {
        var parts = passwordHash.Split('$');
        if (parts.Length != 3 || parts[0] is not (Prefix or LegacyPrefix))
            return Task.FromResult(false);

        return Task.FromResult(CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Digest(parts[1], password)), Encoding.ASCII.GetBytes(parts[2])));
    }

    public bool NeedsRehash(string passwordHash) => !passwordHash.StartsWith($"{Prefix}$", StringComparison.Ordinal);

    private static string Make(string prefix, string password)
    {
        var salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        return $"{prefix}${salt}${Digest(salt, password)}";
    }

    private static string Digest(string salt, string password) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(salt + password)));
}
