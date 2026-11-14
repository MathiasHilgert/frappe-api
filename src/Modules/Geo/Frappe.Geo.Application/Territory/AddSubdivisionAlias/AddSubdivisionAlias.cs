using Frappe.UseCases;

namespace Frappe.Geo.Application.Territory.AddSubdivisionAlias;

/// <summary>Adds a manual alias to a subdivision (see <see cref="SubdivisionAliasText"/>); the territory sync never removes it.</summary>
public sealed record AddSubdivisionAlias(string SubdivisionCode, string? Alias) : ICommand<SubdivisionAliasView>
{
    public const int MaxLength = SubdivisionAliasText.MaxLength;
}
