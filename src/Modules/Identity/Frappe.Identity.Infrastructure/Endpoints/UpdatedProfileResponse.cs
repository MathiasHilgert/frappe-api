namespace Frappe.Identity.Infrastructure.Endpoints;

/// <summary>PATCH /me: what the update stored, e.g. the canonical locale (es_mx becomes es-MX).</summary>
internal sealed record UpdatedProfileResponse(string Locale);
