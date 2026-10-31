using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using Frappe.Geo.Contracts.ValueObjects;

namespace Frappe.Geo.Infrastructure.Catalogues;

/// <summary>
/// The city id of a Georef locality, derived from its INDEC id so every environment and every rebuilt database gives
/// a locality the same id: a UUIDv7 with a fixed timestamp whose 74 random bits hold the INDEC id's value.
/// </summary>
internal static class CityIds
{
    // 2026-01-01T00:00:00Z in Unix milliseconds.
    private const long Timestamp = 1_767_225_600_000;

    private static readonly BigInteger _randomBits = BigInteger.One << 74;

    public static CityId From(string indecId)
    {
        var value = BigInteger.Parse(indecId, NumberStyles.None, CultureInfo.InvariantCulture);
        if (value >= _randomBits)
        {
            throw new ArgumentOutOfRangeException(
                nameof(indecId),
                indecId,
                "An INDEC id fits in 74 bits."
            );
        }

        // The 74 bits are 12 (rand_a, after the version nibble) then 62 (rand_b, after the variant bits).
        ulong randA = (ulong)(value >> 62);
        ulong randB = (ulong)(value & ((BigInteger.One << 62) - 1));
        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, ((ulong)Timestamp << 16) | 0x7000UL | randA);
        BinaryPrimitives.WriteUInt64BigEndian(bytes[8..], 0x8000_0000_0000_0000UL | randB);
        return CityId.From(new Guid(bytes, bigEndian: true)).Value;
    }
}
