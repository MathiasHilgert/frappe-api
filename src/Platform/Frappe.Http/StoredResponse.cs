namespace Frappe.Http;

/// <summary>An answer as stored: its status, the headers worth replaying and its body.</summary>
public sealed record StoredResponse(int Status, IReadOnlyDictionary<string, string[]> Headers, byte[] Body);
