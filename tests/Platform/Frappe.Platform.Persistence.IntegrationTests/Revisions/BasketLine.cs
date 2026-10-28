using Frappe.Kernel.Domain;

namespace Frappe.Platform.Persistence.IntegrationTests.Revisions;

// A child entity in its own table, reached only through its basket.
internal sealed class BasketLine : Entity<BasketLineId>
{
    public BasketLine(BasketLineId id, int quantity)
        : base(id) => Quantity = quantity;

    private BasketLine() { }

    public int Quantity { get; private set; }

    public void ChangeQuantity(int quantity) => Quantity = quantity;
}
