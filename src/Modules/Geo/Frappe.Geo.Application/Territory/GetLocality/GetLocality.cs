using Frappe.UseCases;

namespace Frappe.Geo.Application.Territory.GetLocality;

/// <summary>One locality by its public id (<c>AR:14014010</c>).</summary>
public sealed record GetLocality(string LocalityId) : IQuery<LocalityView>;
