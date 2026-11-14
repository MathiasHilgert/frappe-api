using Frappe.Geo.Application.Countries;
using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Geo.Application.Territory.AddSubdivisionAlias;

internal sealed class AddSubdivisionAliasHandler(ICountryCatalog countries, ITerritoryDirectory territory, ISubdivisionAliases aliases)
    : ICommandHandler<AddSubdivisionAlias, SubdivisionAliasView>
{
    public async Task<Result<SubdivisionAliasView>> HandleAsync(AddSubdivisionAlias command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Alias))
        {
            return GeoErrors.ParameterRequired("alias");
        }

        if (SubdivisionAliasText.Validate(command.Alias) is not { } alias)
        {
            return GeoErrors.SubdivisionAliasInvalid(command.Alias, SubdivisionAliasText.MaxLength);
        }

        var subdivision = await Subdivisions.ResolveAsync(command.SubdivisionCode, countries, territory, cancellationToken);
        if (!subdivision.IsSuccess)
        {
            return Result.Failure<SubdivisionAliasView>(subdivision.Errors);
        }

        var code = subdivision.Value.Value;
        var addition = await aliases.TryAddAsync(subdivision.Value, alias, cancellationToken);
        return addition.Outcome switch
        {
            AliasAdditionOutcome.Added => new SubdivisionAliasView(alias, Manual: true),
            AliasAdditionOutcome.Taken => GeoErrors.SubdivisionAliasTaken(code, alias, addition.OtherSubdivisionCode!),
            _ => GeoErrors.SubdivisionAliasDuplicate(code, alias),
        };
    }
}
