namespace Frappe.Kernel.Domain;

/// <summary>
/// An object defined by its identity: two entities are equal when they are the same kind of entity
/// with the same non-default ID.
/// </summary>
/// <typeparam name="TId">The entity's strongly typed ID.</typeparam>
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : struct, IEntityId<TId>
{
    /// <summary>Initializes an entity with its identity.</summary>
    /// <param name="id">The identity; never <see langword="default"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is <see langword="default"/>.</exception>
    protected Entity(TId id)
    {
        if (id.Equals(default(TId)))
        {
            throw new ArgumentException("An entity needs a non-default ID.", nameof(id));
        }

        Id = id;
    }

    /// <summary>Initializes an entity for materialization by the persistence layer.</summary>
    protected Entity() { }

    /// <summary>Gets the identity.</summary>
    public TId Id { get; private init; }

    private bool IsTransient => Id.Equals(default(TId));

    /// <summary>Compares two entities by identity.</summary>
    /// <param name="left">The first entity.</param>
    /// <param name="right">The second entity.</param>
    /// <returns><see langword="true"/> when both are the same entity.</returns>
    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) => Equals(left, right);

    /// <summary>Compares two entities by identity.</summary>
    /// <param name="left">The first entity.</param>
    /// <param name="right">The second entity.</param>
    /// <returns><see langword="true"/> when they are different entities.</returns>
    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !Equals(left, right);

    /// <inheritdoc />
    public bool Equals(Entity<TId>? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return GetType() == other.GetType() && !IsTransient && Id.Equals(other.Id);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Entity<TId> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() =>
        IsTransient
            ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this)
            : HashCode.Combine(GetType(), Id);
}
