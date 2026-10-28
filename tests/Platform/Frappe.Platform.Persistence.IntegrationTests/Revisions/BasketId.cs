using ErrorOr;
using Frappe.Kernel.Domain;

namespace Frappe.Platform.Persistence.IntegrationTests.Revisions;

internal readonly record struct BasketId(Guid Value) : IEntityId<BasketId>
{
    public static BasketId NewId() => new(Guid.CreateVersion7());

    public static ErrorOr<BasketId> From(Guid value) =>
        EntityId.Validate(value).Then(static v => new BasketId(v));
}
