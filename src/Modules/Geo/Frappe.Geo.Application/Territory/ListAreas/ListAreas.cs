using Frappe.UseCases;

namespace Frappe.Geo.Application.Territory.ListAreas;

/// <summary>A subdivision's administrative areas, of one <paramref name="Level"/> (2 to 9, as the client wrote it) or of all.</summary>
public sealed record ListAreas(string SubdivisionCode, string? Level) : IQuery<IReadOnlyList<AdministrativeAreaView>>;
