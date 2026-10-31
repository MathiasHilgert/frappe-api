using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Frappe.Kernel.Domain;

/// <summary>
/// The validation rule every <see cref="IEntityId{TSelf}.From"/> delegates to, and the parsing every typed id's
/// <see cref="IParsable{TSelf}"/> members delegate to.
/// </summary>
public static class EntityId
{
    private const int UuidVersion7 = 7;

    // RFC 9562 variant: the two high bits of the variant nibble are 10.
    private const int VariantMask = 0b1100;
    private const int RfcVariant = 0b1000;

    /// <summary>Gets the error returned for a value that is empty or not a UUIDv7.</summary>
    public static Error Invalid { get; } = Error.Validation("kernel.entity_id.invalid");

    /// <summary>Accepts only non-empty RFC 9562 UUIDv7 values.</summary>
    /// <param name="value">The raw value.</param>
    /// <returns>The value, or <see cref="Invalid"/>.</returns>
    public static ErrorOr<Guid> Validate(Guid value) =>
        value != Guid.Empty
        && value.Version == UuidVersion7
        && (value.Variant & VariantMask) == RfcVariant
            ? value
            : Invalid;

    /// <summary>Parses the UUID text of a typed id through its <see cref="IEntityId{TSelf}.From"/>.</summary>
    /// <typeparam name="TId">The typed id.</typeparam>
    /// <param name="s">The UUID text.</param>
    /// <returns>The typed id.</returns>
    /// <exception cref="FormatException"><paramref name="s"/> is not a non-empty RFC 9562 UUIDv7.</exception>
    public static TId Parse<TId>(string s)
        where TId : struct, IEntityId<TId>
    {
        ArgumentNullException.ThrowIfNull(s);
        return TryParse(s, out TId id)
            ? id
            : throw new FormatException($"'{s}' is not a non-empty RFC 9562 UUIDv7.");
    }

    /// <summary>Tries to parse the UUID text of a typed id through its <see cref="IEntityId{TSelf}.From"/>.</summary>
    /// <typeparam name="TId">The typed id.</typeparam>
    /// <param name="s">The UUID text.</param>
    /// <param name="result">The typed id, or <see langword="default"/>.</param>
    /// <returns>Whether <paramref name="s"/> is a non-empty RFC 9562 UUIDv7.</returns>
    public static bool TryParse<TId>([NotNullWhen(true)] string? s, out TId result)
        where TId : struct, IEntityId<TId>
    {
        if (
            Guid.TryParse(s, CultureInfo.InvariantCulture, out Guid value)
            && TId.From(value) is { IsError: false, Value: var id }
        )
        {
            result = id;
            return true;
        }

        result = default;
        return false;
    }
}
