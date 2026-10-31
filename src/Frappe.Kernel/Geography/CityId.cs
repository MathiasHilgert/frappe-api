using Frappe.Kernel.Domain;

namespace Frappe.Kernel.Geography;

/// <summary>The identifier of a geo city; in the Kernel because <see cref="Address"/> embeds it.</summary>
public readonly record struct CityId : IEntityId<CityId>
{
    private CityId(Guid value) => Value = value;

    /// <inheritdoc />
    public Guid Value { get; }

    /// <inheritdoc />
    public static CityId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public static ErrorOr<CityId> From(Guid value) =>
        EntityId.Validate(value).Then(static v => new CityId(v));
}
