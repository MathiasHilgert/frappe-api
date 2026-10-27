namespace Frappe.Kernel.Domain;

/// <summary>
/// A business fact raised by an aggregate and handled inside its own module after commit.
/// </summary>
public interface IDomainEvent;
