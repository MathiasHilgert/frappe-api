using System.Diagnostics.CodeAnalysis;
using Frappe.Kernel.Domain;

namespace Frappe.Platform.IntegrationTests.Persistence.Revisions;

internal readonly record struct BasketId(Guid Value) : IEntityId<BasketId>
{
    public static BasketId NewId() => new(Guid.CreateVersion7());

    public static ErrorOr<BasketId> From(Guid value) =>
        EntityId.Validate(value).Then(static v => new BasketId(v));

    public static BasketId Parse(string s, IFormatProvider? provider) =>
        EntityId.Parse<BasketId>(s);

    public static bool TryParse(
        [NotNullWhen(true)] string? s,
        IFormatProvider? provider,
        out BasketId result
    ) => EntityId.TryParse(s, out result);
}
