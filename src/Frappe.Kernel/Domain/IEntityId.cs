namespace Frappe.Kernel.Domain;

/// <summary>
/// A strongly typed, time-ordered (UUIDv7) entity identifier. It is <see cref="IParsable{TSelf}"/> so a route or
/// query parameter binds it; implement both members with <see cref="EntityId.Parse{TId}"/> and
/// <see cref="EntityId.TryParse{TId}"/>.
/// </summary>
/// <typeparam name="TSelf">The implementing ID type.</typeparam>
public interface IEntityId<TSelf> : IEquatable<TSelf>, IParsable<TSelf>
    where TSelf : struct, IEntityId<TSelf>
{
    /// <summary>Gets the underlying UUIDv7 value.</summary>
    Guid Value { get; }

    /// <summary>Creates a new, time-ordered identifier.</summary>
    /// <returns>A new identifier.</returns>
    static abstract TSelf NewId();

    /// <summary>
    /// Builds an identifier from untrusted input, rejecting values that are not non-empty UUIDv7s.
    /// </summary>
    /// <param name="value">The raw value.</param>
    /// <returns>The identifier, or <see cref="EntityId.Invalid"/>.</returns>
    static abstract ErrorOr<TSelf> From(Guid value);
}
