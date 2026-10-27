namespace Frappe.Kernel.Domain;

/// <summary>
/// The validation rule every <see cref="IEntityId{TSelf}.From"/> delegates to.
/// </summary>
public static class EntityId
{
    private const int UuidVersion7 = 7;

    /// <summary>Gets the error returned for a value that is empty or not a UUIDv7.</summary>
    public static Error Invalid { get; } =
        Error.Validation("kernel.entity_id.invalid", "The identifier must be a non-empty UUIDv7.");

    /// <summary>Accepts only non-empty UUIDv7 values.</summary>
    /// <param name="value">The raw value.</param>
    /// <returns>The value, or <see cref="Invalid"/>.</returns>
    public static ErrorOr<Guid> Validate(Guid value) =>
        value != Guid.Empty && value.Version == UuidVersion7 ? value : Invalid;
}
