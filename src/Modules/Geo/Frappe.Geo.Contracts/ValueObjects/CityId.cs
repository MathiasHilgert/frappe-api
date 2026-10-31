using System.Diagnostics.CodeAnalysis;
using Frappe.Kernel.Domain;

namespace Frappe.Geo.Contracts.ValueObjects;

/// <summary>The identifier of a geo city, the Georef locality other modules store a reference to.</summary>
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

    /// <inheritdoc />
    public static CityId Parse(string s, IFormatProvider? provider) => EntityId.Parse<CityId>(s);

    /// <inheritdoc />
    public static bool TryParse(
        [NotNullWhen(true)] string? s,
        IFormatProvider? provider,
        out CityId result
    ) => EntityId.TryParse(s, out result);
}
