using Frappe.Geo.Application.Countries;
using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Geo.Application.Territory.RemoveSubdivisionAlias;

internal sealed class RemoveSubdivisionAliasHandler(ICountryCatalog countries, ITerritoryDirectory territory, ISubdivisionAliases aliases)
    : ICommandHandler<RemoveSubdivisionAlias, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RemoveSubdivisionAlias command, CancellationToken cancellationToken)
    {
        // The rule that admits an alias also admits its removal: one that breaks it could never have been added.
        if (SubdivisionAliasText.Validate(command.Alias) is not { } alias)
        {
            return GeoErrors.SubdivisionAliasInvalid(command.Alias, SubdivisionAliasText.MaxLength);
        }

        // A retired subdivision's manual alias still blocks the country's other subdivisions (one unique index holds
        // them all), so it can be removed even though the subdivision is no longer served.
        var subdivision = await Subdivisions.ResolveAsync(command.SubdivisionCode, countries, territory, cancellationToken, retiredToo: true);
        if (!subdivision.IsSuccess)
        {
            return Result.Failure<Unit>(subdivision.Errors);
        }

        var code = subdivision.Value.Value;
        return await aliases.TryRemoveAsync(subdivision.Value, alias, cancellationToken) switch
        {
            AliasRemoval.Removed => Unit.Value,
            AliasRemoval.Imported => GeoErrors.SubdivisionAliasImported(code, alias),
            _ => GeoErrors.SubdivisionAliasNotFound(code, alias),
        };
    }
}
