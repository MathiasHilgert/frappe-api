namespace Frappe.Geo.Http.Holidays;

/// <summary>
/// The norm of an observance set by hand: <c>citation</c> (required when a basis is given, up to 200 characters) and
/// <c>url</c>, the absolute <c>http</c> or <c>https</c> address of its text (up to 500 characters). Strings, so a value
/// that is not an address is answered with a Geo error instead of a binding failure.
/// </summary>
public sealed record LegalBasisRequest(string? Citation, string? Url = null);
