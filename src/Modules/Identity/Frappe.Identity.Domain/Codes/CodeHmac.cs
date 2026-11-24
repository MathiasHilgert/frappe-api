using System.Security.Cryptography;
using Frappe.Results;

namespace Frappe.Identity.Domain.Codes;

/// <summary>
/// The HMAC-SHA256 of a one-time code under the configured key: what the database keeps instead of the code (S5). It prints nothing of its
/// bytes (DOM-06, SEC-07) and compares in fixed time, so a guess cannot learn the stored value from how long the answer takes.
/// </summary>
public sealed record CodeHmac
{
    /// <summary>The size of an HMAC-SHA256 value, in bytes.</summary>
    public const int Length = 32;

    private readonly byte[] _bytes;

    private CodeHmac(byte[] bytes) => _bytes = bytes;

    /// <summary>A copy of the bytes, for storage.</summary>
    public byte[] ToArray() => [.. _bytes];

    /// <summary>The hash made of exactly <see cref="Length"/> bytes, or why it is not one.</summary>
    public static Result<CodeHmac> Create(ReadOnlySpan<byte> bytes) =>
        bytes.Length == Length
            ? new CodeHmac(bytes.ToArray())
            : Error.InvalidValue(IdentityErrorCodes.CodeHmacLength, ("length", Length));

    /// <summary>Whether both hashes hold the same bytes, compared in fixed time.</summary>
    public bool Equals(CodeHmac? other) => other is not null && CryptographicOperations.FixedTimeEquals(_bytes, other._bytes);

    public override int GetHashCode() => HashCode.Combine(_bytes[0], _bytes[1], _bytes[2], _bytes[3]);

    public override string ToString() => "***";
}
