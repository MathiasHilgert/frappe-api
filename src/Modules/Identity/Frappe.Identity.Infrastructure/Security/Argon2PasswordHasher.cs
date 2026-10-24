using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Domain;
using Frappe.SharedKernel.Validation;
using Konscious.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Frappe.Identity.Infrastructure.Security;

/// <summary>
/// Argon2id in the PHC string format (<c>$argon2id$v=19$m=…,t=…,p=…$salt$hash</c>), so a hash keeps the cost it
/// was made with and raising the options never breaks existing passwords. At most
/// <see cref="Argon2Options.MaxConcurrentHashes"/> derivations run at once; callers beyond that wait asynchronously
/// for at most <see cref="Argon2Options.SlotWaitMilliseconds"/>, then get an <see cref="UnavailableException"/> (503).
/// </summary>
internal sealed class Argon2PasswordHasher : IPasswordHasher, IDisposable
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const string Prefix = "$argon2id$v=19$";

    private readonly Argon2Options _cost;
    private readonly SemaphoreSlim _slots;

    public Argon2PasswordHasher(IOptions<Argon2Options> options)
    {
        _cost = options.Value;
        _slots = new SemaphoreSlim(_cost.MaxConcurrentHashes, _cost.MaxConcurrentHashes);
    }

    public async Task<string> HashAsync(string password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = await DeriveAsync(password, salt, _cost.MemorySizeKib, _cost.Iterations, _cost.Parallelism, HashSize, cancellationToken);

        return string.Create(CultureInfo.InvariantCulture,
            $"{Prefix}m={_cost.MemorySizeKib},t={_cost.Iterations},p={_cost.Parallelism}${Encode(salt)}${Encode(hash)}");
    }

    public async Task<bool> VerifyAsync(string password, string passwordHash, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(password);

        if (!TryParse(passwordHash, out var parsed))
        {
            return false;
        }

        var actual = await DeriveAsync(
            password, parsed.Salt, parsed.Memory, parsed.Iterations, parsed.Parallelism, parsed.Hash.Length, cancellationToken);
        return CryptographicOperations.FixedTimeEquals(actual, parsed.Hash);
    }

    public bool NeedsRehash(string passwordHash) =>
        !TryParse(passwordHash, out var parsed)
        || parsed.Memory < _cost.MemorySizeKib
        || parsed.Iterations < _cost.Iterations
        || parsed.Parallelism != _cost.Parallelism;

    public void Dispose() => _slots.Dispose();

    private async Task<byte[]> DeriveAsync(
        string password, byte[] salt, int memoryKib, int iterations, int parallelism, int size, CancellationToken cancellationToken)
    {
        if (!await _slots.WaitAsync(TimeSpan.FromMilliseconds(_cost.SlotWaitMilliseconds), cancellationToken))
            throw new UnavailableException(IdentityErrors.PasswordHashingBusy.WithRetryAfter(TimeSpan.FromSeconds(1)));
        try
        {
            return Derive(password, salt, memoryKib, iterations, parallelism, size);
        }
        finally
        {
            _slots.Release();
        }
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
