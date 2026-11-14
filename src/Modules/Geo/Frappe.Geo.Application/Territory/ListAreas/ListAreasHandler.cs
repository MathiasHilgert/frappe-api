using System.Globalization;
using Frappe.Geo.Application.Countries;
using Frappe.Geo.Domain.Territory;
using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Geo.Application.Territory.ListAreas;

internal sealed class ListAreasHandler(ICountryCatalog countries, ITerritoryDirectory territory) : IQueryHandler<ListAreas, IReadOnlyList<AdministrativeAreaView>>
{
    public async Task<Result<IReadOnlyList<AdministrativeAreaView>>> HandleAsync(ListAreas query, CancellationToken cancellationToken)
    {
        var code = SubdivisionCode.Create(query.SubdivisionCode);
        var level = ParseLevel(query.Level);
        var input = Result.Combine(code, level);
        if (!input.IsSuccess)
        {
            return Result.Failure<IReadOnlyList<AdministrativeAreaView>>(input.Errors);
        }

        if (await territory.FindSubdivisionAsync(code.Value, cancellationToken) is null)
        {
            return await Subdivisions.MissingAsync(code.Value, countries, territory, cancellationToken);
        }

        return Result.Success(await territory.ListAreasAsync(code.Value, level.Value, cancellationToken));
    }

    /// <summary>No level means every level; a level is a whole number from 2 (the first level is the subdivision itself).</summary>
    private static Result<int?> ParseLevel(string? raw)
    {
        if (raw is null)
        {
            return Result.Success<int?>(null);
        }

        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var level))
        {
            return GeoErrors.AreaLevelInvalid(raw);
        }

        return level is >= 2 and <= 9 ? Result.Success<int?>(level) : GeoErrors.AreaLevelOutOfRange(raw);
    }
}
