namespace Frappe.Access.UnitTests.Fixtures;

/// <summary>A resource only its holder may see.</summary>
public sealed record Note(Guid HolderId);
