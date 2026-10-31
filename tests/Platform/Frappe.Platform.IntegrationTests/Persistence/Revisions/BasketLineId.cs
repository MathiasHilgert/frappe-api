using System.Diagnostics.CodeAnalysis;
using Frappe.Kernel.Domain;

namespace Frappe.Platform.IntegrationTests.Persistence.Revisions;

internal readonly record struct BasketLineId(Guid Value) : IEntityId<BasketLineId>
{
    public static BasketLineId NewId() => new(Guid.CreateVersion7());

    public static ErrorOr<BasketLineId> From(Guid value) =>
        EntityId.Validate(value).Then(static v => new BasketLineId(v));

    public static BasketLineId Parse(string s, IFormatProvider? provider) =>
        EntityId.Parse<BasketLineId>(s);

    public static bool TryParse(
        [NotNullWhen(true)] string? s,
        IFormatProvider? provider,
        out BasketLineId result
    ) => EntityId.TryParse(s, out result);
}
