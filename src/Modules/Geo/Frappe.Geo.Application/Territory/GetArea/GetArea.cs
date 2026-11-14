using Frappe.UseCases;

namespace Frappe.Geo.Application.Territory.GetArea;

/// <summary>One administrative area by its public id (<c>AR:30015</c>).</summary>
public sealed record GetArea(string AreaId) : IQuery<AdministrativeAreaView>;
