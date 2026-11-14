using Frappe.UseCases;

namespace Frappe.Geo.Application.Territory.GetSubdivision;

/// <summary>One subdivision by its ISO 3166-2 code, with its aliases.</summary>
public sealed record GetSubdivision(string SubdivisionCode) : IQuery<SubdivisionView>;
