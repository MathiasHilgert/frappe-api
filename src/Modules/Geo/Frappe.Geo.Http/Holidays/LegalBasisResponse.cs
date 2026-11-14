namespace Frappe.Geo.Http.Holidays;

/// <summary>The norm that establishes an observance (<c>citation</c>), and the address of its text (<c>url</c>) when known.</summary>
public sealed record LegalBasisResponse(string Citation, Uri? Url);
