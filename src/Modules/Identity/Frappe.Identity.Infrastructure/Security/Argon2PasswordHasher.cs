using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Frappe.Identity.Application.Security;
using Konscious.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Frappe.Identity.Infrastructure.Security;

/// <summary>
/// Argon2id in the PHC string format (<c>$argon2id$v=19$m=…,t=…,p=…$salt$hash</c>), so a hash keeps the cost it
/// was made with and raising the options never breaks existing passwords.
/// </summary>
internal sealed class Argon2PasswordHasher(IOptions<Argon2Options> options) : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const string Prefix = "$argon2id$v=19$";

    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var cost = options.Value;
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Derive(password, salt, cost.MemorySizeKib, cost.Iterations, cost.Parallelism, HashSize);

        return string.Create(CultureInfo.InvariantCulture,
            $"{Prefix}m={cost.MemorySizeKib},t={cost.Iterations},p={cost.Parallelism}${Encode(salt)}${Encode(hash)}");
    }

    public bool Verify(string password, string passwordHash)
    {
        ArgumentNullException.ThrowIfNull(password);

        if (!TryParse(passwordHash, out var parsed))
        {
            return false;
        }

        var actual = Derive(password, parsed.Salt, parsed.Memory, parsed.Iterations, parsed.Parallelism, parsed.Hash.Length);
        return CryptographicOperations.FixedTimeEquals(actual, parsed.Hash);
    }

    private static byte[] Derive(string password, byte[] salt, int memoryKib, int iterations, int parallelism, int size)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password));
        argon2.Salt = salt;
        argon2.MemorySize = memoryKib;
        argon2.Iterations = iterations;
        argon2.DegreeOfParallelism = parallelism;
        return argon2.GetBytes(size);
    }

    private static bool TryParse(string? phc, out ParsedHash parsed)
    {
        parsed = default;
        if (phc is null || !phc.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = phc[Prefix.Length..].Split('$');
        if (parts.Length != 3)
        {
            return false;
        }

        if (!TryParseCost(parts[0], out var memory, out var iterations, out var parallelism)
            || !TryDecode(parts[1], out var salt)
            || !TryDecode(parts[2], out var hash)
            || hash.Length == 0)
        {
            return false;
        }

        parsed = new ParsedHash(memory, iterations, parallelism, salt, hash);
        return true;
    }

    private static bool TryParseCost(string text, out int memory, out int iterations, out int parallelism)
    {
        var cost = text.Split(',');
        memory = iterations = parallelism = 0;
        return cost.Length == 3
            && TryReadCost(cost[0], "m=", out memory)
            && TryReadCost(cost[1], "t=", out iterations)
            && TryReadCost(cost[2], "p=", out parallelism);
    }

    private static bool TryReadCost(string part, string key, out int value)
    {
        value = 0;
        return part.StartsWith(key, StringComparison.Ordinal)
            && int.TryParse(part.AsSpan(key.Length), NumberStyles.None, CultureInfo.InvariantCulture, out value)
            && value > 0;
    }

    // PHC strings use unpadded standard base64.
    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=');

    private static bool TryDecode(string value, out byte[] bytes)
    {
        var padded = value.PadRight(value.Length + ((4 - (value.Length % 4)) % 4), '=');
        bytes = new byte[padded.Length];
        if (!Convert.TryFromBase64String(padded, bytes, out var written))
        {
            return false;
        }

        bytes = bytes[..written];
        return true;
    }

    private readonly record struct ParsedHash(int Memory, int Iterations, int Parallelism, byte[] Salt, byte[] Hash);
}
