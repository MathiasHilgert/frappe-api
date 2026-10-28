namespace Frappe.Kernel.Domain;

/// <summary>
/// The validation rule every <see cref="IEntityId{TSelf}.From"/> delegates to.
/// </summary>
public static class EntityId
{
    private const int UuidVersion7 = 7;

    // RFC 9562 variant: the two high bits of the variant nibble are 10.
    private const int VariantMask = 0b1100;
    private const int RfcVariant = 0b1000;

    /// <summary>Gets the error returned for a value that is empty or not a UUIDv7.</summary>
    public static Error Invalid { get; } =
        Error.Validation(
            "kernel.entity_id.invalid",
            "The identifier must be a non-empty RFC 9562 UUIDv7."
        );

    /// <summary>Accepts only non-empty RFC 9562 UUIDv7 values.</summary>
    /// <param name="value">The raw value.</param>
    /// <returns>The value, or <see cref="Invalid"/>.</returns>
    public static ErrorOr<Guid> Validate(Guid value) =>
        value != Guid.Empty
        && value.Version == UuidVersion7
        && (value.Variant & VariantMask) == RfcVariant
            ? value
            : Invalid;
}
