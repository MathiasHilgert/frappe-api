namespace Frappe.Http;

/// <summary>
/// An enumerated value on the wire (HTTP-25): its stable snake_case <c>code</c>, which clients branch on, and its
/// <c>name</c>, written in the language the client asked for, which clients show.
/// </summary>
public sealed record EnumeratedValue(string Code, string Name);
