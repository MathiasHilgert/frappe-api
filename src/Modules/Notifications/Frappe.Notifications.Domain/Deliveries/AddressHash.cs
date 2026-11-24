using System.Security.Cryptography;

namespace Frappe.Notifications.Domain.Deliveries;

/// <summary>
/// The keyed hash of a normalized address and the id of the key that made it: the only form an address is stored in (S13). It never
/// prints its bytes.
/// </summary>
public sealed record AddressHash
{
    /// <summary>The length of an HMAC-SHA256 value.</summary>
    public const int Length = 32;

    private readonly byte[] _value;

    public AddressHash(ReadOnlySpan<byte> value, short keyId)
    {
        if (value.Length != Length)
        {
            throw new ArgumentException($"An address hash has {Length} bytes.", nameof(value));
        }

        _value = value.ToArray();
        KeyId = keyId;
    }

    public short KeyId { get; }

    /// <summary>A copy of the hash bytes.</summary>
    public byte[] Value => [.. _value];

    /// <summary>The hash as lower-case hexadecimal, as a recipient key spells it.</summary>
    public string Hex => Convert.ToHexStringLower(_value);

    public bool Equals(AddressHash? other) =>
        other is not null && KeyId == other.KeyId && CryptographicOperations.FixedTimeEquals(_value, other._value);

    public override int GetHashCode() => HashCode.Combine(KeyId, BitConverter.ToInt32(_value, 0));

    public override string ToString() => $"AddressHash {{ KeyId = {KeyId}, Value = *** }}";
}
