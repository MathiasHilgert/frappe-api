namespace Frappe.Access.UnitTests.Fixtures;

/// <summary>A resource every authenticated caller may see, whose actions need more.</summary>
public sealed record Folder(Guid HolderId);
