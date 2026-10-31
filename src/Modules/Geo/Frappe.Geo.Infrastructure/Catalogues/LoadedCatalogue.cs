namespace Frappe.Geo.Infrastructure.Catalogues;

/// <summary>What loading one catalogue read: its row count, for the log, and one text line per row and name.</summary>
/// <param name="Rows">The catalogue rows loaded.</param>
/// <param name="Lines">Every row and name as one line, in a fixed order, for the dataset tag.</param>
internal sealed record LoadedCatalogue(int Rows, IReadOnlyList<string> Lines);
