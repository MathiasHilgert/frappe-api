namespace Frappe.Geo.Domain.Subdivisions;

/// <summary>The errors of the subdivision catalogue.</summary>
public static class SubdivisionErrors
{
    /// <summary>Gets the error for an ISO 3166-2 code the catalogue does not hold.</summary>
    public static Error NotFound { get; } = Error.NotFound("geo.subdivision.not_found");
}
