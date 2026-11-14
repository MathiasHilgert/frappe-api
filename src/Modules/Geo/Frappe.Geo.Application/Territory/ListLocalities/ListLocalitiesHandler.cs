using System.Globalization;
using Frappe.Geo.Application.Countries;
using Frappe.Geo.Domain.Territory;
using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Geo.Application.Territory.ListLocalities;

internal sealed class ListLocalitiesHandler(ICountryCatalog countries, ITerritoryDirectory territory) : IQueryHandler<ListLocalities, LocalityPage>
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    public async Task<Result<LocalityPage>> HandleAsync(ListLocalities query, CancellationToken cancellationToken)
    {
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        var code = SubdivisionCode.Create(query.SubdivisionCode);
        var limit = ParseLimit(query.Limit);
        var cursor = ParseCursor(query.Cursor, code.IsSuccess ? code.Value.Value : string.Empty, search);
        var input = Result.Combine(code, limit, cursor);
        if (!input.IsSuccess)
        {
            return Result.Failure<LocalityPage>(input.Errors);
        }

        if (await territory.FindSubdivisionAsync(code.Value, cancellationToken) is null)
        {
            return await Subdivisions.MissingAsync(code.Value, countries, territory, cancellationToken);
        }

        return await territory.PageLocalitiesAsync(code.Value, search, limit.Value, cursor.Value, cancellationToken);
    }

    private static Result<int> ParseLimit(string? raw)
    {
        if (raw is null)
        {
            return DefaultLimit;
        }

        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var limit))
        {
            return GeoErrors.PageLimitInvalid(raw, MaxLimit);
        }

        return limit is >= 1 and <= MaxLimit ? limit : GeoErrors.PageLimitOutOfRange(raw, MaxLimit);
    }

    /// <summary>A cursor is only valid for the subdivision and search it was issued for.</summary>
    private static Result<LocalityCursor?> ParseCursor(string? raw, string subdivision, string? search)
    {
        if (raw is null)
        {
            return Result.Success<LocalityCursor?>(null);
        }

        return LocalityCursor.Decode(raw) is { } cursor && cursor.Fingerprint == LocalityCursor.FingerprintOf(subdivision, search)
            ? cursor
            : GeoErrors.PageCursorInvalid();
    }
}
