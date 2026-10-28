using System.Collections.ObjectModel;
using Frappe.Kernel.Domain;

namespace Frappe.Platform.Persistence.IntegrationTests.Revisions;

// An aggregate with every part the revision rule covers: its own columns, an owned value and child
// rows in their own table. No method touches Revision; saving is what increments it.
internal sealed class Basket : AggregateRoot<BasketId>
{
    private readonly Collection<BasketLine> _lines = [];

    public Basket(BasketId id)
        : base(id) { }

    private Basket() { }

    public string Name { get; private set; } = "";

    public BasketNote Note { get; private set; } = new("");

    public IReadOnlyList<BasketLine> Lines => _lines.AsReadOnly();

    public void Rename(string name) => Name = name;

    public void Annotate(string text) => Note = new BasketNote(text);

    public void AddLine(int quantity) => _lines.Add(new BasketLine(BasketLineId.NewId(), quantity));

    public void ChangeQuantity(BasketLineId lineId, int quantity) =>
        _lines.Single(line => line.Id == lineId).ChangeQuantity(quantity);

    public void RemoveLine(BasketLineId lineId) =>
        _lines.Remove(_lines.Single(line => line.Id == lineId));
}
