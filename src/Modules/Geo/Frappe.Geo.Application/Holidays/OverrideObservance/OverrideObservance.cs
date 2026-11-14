using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Geo.Application.Holidays.OverrideObservance;

/// <summary>
/// Adds an observance by hand to the calendar of a country (<c>AR</c>) or subdivision (<c>AR-X</c>) of the date's year,
/// or replaces the source's of the same date and name; it wins over whatever the source says later. Dates are
/// <c>yyyy-MM-dd</c>; <see cref="Kind"/> is a kind's wire name (<c>holiday</c>, ...); <see cref="Community"/> one the
/// country declares; <see cref="LegalUrl"/> an absolute <c>http(s)</c> address of the norm's text; <see cref="Scope"/>
/// picks a sector's calendar (<c>banking</c>) instead of the general one.
/// </summary>
public sealed record OverrideObservance(
    string Jurisdiction,
    string? Date,
    string? Name,
    string? Kind,
    string? MovedFrom = null,
    string? Community = null,
    string? LegalCitation = null,
    string? LegalUrl = null,
    string? Scope = null) : ICommand<Unit>;
