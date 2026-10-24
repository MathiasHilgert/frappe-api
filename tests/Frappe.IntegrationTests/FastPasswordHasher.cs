using System.Security.Cryptography;
using System.Text;
using Frappe.Identity.Application.Security;

namespace Frappe.IntegrationTests;

/// <summary>
/// Salted SHA-256 stand-in for Argon2id, so the suite does not spend ~19 MiB and two passes per hash.
/// Real hashing stays covered end to end by <see cref="Identity.RealPasswordHashingTests"/>.
/// </summary>
internal sealed class FastPasswordHasher : IPasswordHasher
{
    private const string Prefix = "fast$";

    public string Hash(string password)
    {
        var salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        return $"{Prefix}{salt}${Digest(salt, password)}";
    }

    public bool Verify(string password, string passwordHash)
    {
        var parts = passwordHash.Split('$');
        if (parts.Length != 3 || parts[0] + "$" != Prefix)
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Digest(parts[1], password)), Encoding.ASCII.GetBytes(parts[2]));
    }

    private static string Digest(string salt, string password) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(salt + password)));
}
