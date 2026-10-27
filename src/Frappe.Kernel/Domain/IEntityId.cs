namespace Frappe.Kernel.Domain;

/// <summary>
/// A strongly typed, time-ordered (UUIDv7) entity identifier.
/// </summary>
/// <typeparam name="TSelf">The implementing ID type.</typeparam>
public interface IEntityId<TSelf> : IEquatable<TSelf>
    where TSelf : struct, IEntityId<TSelf>
{
    /// <summary>Gets the underlying UUIDv7 value.</summary>
    public Guid Value { get; }

    /// <summary>Creates a new, time-ordered identifier.</summary>
    /// <returns>A new identifier.</returns>
    public static abstract TSelf NewId();

    /// <summary>
    /// Builds an identifier from untrusted input, rejecting values that are not non-empty UUIDv7s.
    /// </summary>
    /// <param name="value">The raw value.</param>
    /// <returns>The identifier, or <see cref="EntityId.Invalid"/>.</returns>
    public static abstract ErrorOr<TSelf> From(Guid value);
}
