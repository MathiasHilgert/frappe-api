using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Geo.Application.Territory.RemoveSubdivisionAlias;

/// <summary>
/// Removes a manual alias of a subdivision, named as it was added or differing only by case and accents; the source's
/// aliases change only with the source.
/// </summary>
public sealed record RemoveSubdivisionAlias(string SubdivisionCode, string Alias) : ICommand<Unit>;
