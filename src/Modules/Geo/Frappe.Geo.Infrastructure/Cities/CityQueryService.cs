using Frappe.Geo.Application;
using Frappe.Geo.Application.Cities;
using Frappe.Geo.Application.Countries;
using Frappe.Geo.Application.Subdivisions;
using Frappe.Geo.Contracts;
using Frappe.Geo.Contracts.ValueObjects;
using Frappe.Geo.Domain.Cities;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Geo.Infrastructure.Cities;

/// <summary>
/// Reads <c>geo.cities</c>, one SQL statement per call: the keyset list, the ranked autocomplete over the trigram index,
/// and the port other modules resolve a city through. Province and country names come from the in-memory catalogues.
/// </summary>
internal sealed class CityQueryService(
    GeoDbContext db,
    ISubdivisionReader subdivisions,
    ICountryReader countries
) : ICityReader, ICityCatalogue
{
    private const int SearchResults = 10;

    // Pinned in the query instead of left to the word_similarity_threshold setting.
    private const double FuzzyThreshold = 0.6;

    public async Task<Localized<CityPage>> ListAsync(
        CountryCode? country,
        SubdivisionCode? subdivision,
        CityKey? after,
        int limit,
        Locale locale,
        CancellationToken cancellationToken
    )
    {
        IQueryable<CityRow> cities = Active(country, subdivision);
        if (after is not null)
        {
            string name = after.SearchName;
            string indecId = after.IndecId;
            cities = cities.Where(city =>
                EF.Functions.GreaterThan(
                    ValueTuple.Create(city.SearchName, city.IndecId),
                    ValueTuple.Create(name, indecId)
                )
            );
        }

        List<CityMatch> page = await Project(
                cities.OrderBy(static city => city.SearchName).ThenBy(static city => city.IndecId)
            )
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        CityKey? next =
            page.Count > limit
                ? new CityKey(page[limit - 1].SearchName, page[limit - 1].IndecId)
                : null;
        Locale served = countries.List(locale).Locale;
        return new Localized<CityPage>(
            new CityPage([.. page.Take(limit).Select(match => ToDto(match, served))], next),
            served
        );
    }

    public async Task<Localized<IReadOnlyList<CityDto>>> SearchAsync(
        CitySearchTerms terms,
        CountryCode? country,
        SubdivisionCode? subdivision,
        Locale locale,
        CancellationToken cancellationToken
    )
    {
        IQueryable<CityRow> cities = Active(country, subdivision);
        List<CityMatch> matches = await (
            terms.Fuzzy ? Ranked(cities, terms) : Prefixed(cities, terms.Plain)
        ).ToListAsync(cancellationToken);
        Locale served = countries.List(locale).Locale;
        return new Localized<IReadOnlyList<CityDto>>(
            [.. matches.Select(match => ToDto(match, served))],
            served
        );
    }

    public async ValueTask<ErrorOr<GeoCity>> GetCityAsync(
        CityId city,
        CancellationToken cancellationToken
    )
    {
        Guid id = city.Value;
        var found = await db.Set<CityRow>()
            .Where(row => row.CityId == id)
            .Select(static row => new
            {
                row.Name,
                row.SubdivisionCode,
                row.TimeZoneId,
                row.Status,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (found is null)
        {
            return CityErrors.NotFound;
        }

        SubdivisionCode code = SubdivisionCode.Create(found.SubdivisionCode).Value;
        return new GeoCity(city, found.Name, code.Country, code, found.TimeZoneId, found.Status);
    }

    // Whether every word is in the search text and whether any is in the name. A word of three or more characters
    // matches anywhere, a shorter one only at a word start. Five word slots keep one SQL text for any query: a missing
    // word repeats the first, which changes neither answer.
    private static IQueryable<ScoredCity> Scored(IQueryable<CityRow> cities, CitySearchTerms terms)
    {
        string[] anywhere = new string[CitySearchTerms.MaxTokens];
        string[] wordStart = new string[CitySearchTerms.MaxTokens];
        for (int slot = 0; slot < CitySearchTerms.MaxTokens; slot++)
        {
            CitySearchToken token = terms.Tokens[slot < terms.Tokens.Count ? slot : 0];
            bool anyPlace = token.Plain.Length >= CitySearchTerms.FuzzyLength;
            anywhere[slot] = anyPlace ? $"%{token.Escaped}%" : $"{token.Escaped}%";
            wordStart[slot] = anyPlace ? anywhere[slot] : $"% {token.Escaped}%";
        }

        return cities.Select(city => new ScoredCity
        {
            City = city,
            InText =
                (
                    EF.Functions.Like(city.SearchText, anywhere[0])
                    || EF.Functions.Like(city.SearchText, wordStart[0])
                )
                && (
                    EF.Functions.Like(city.SearchText, anywhere[1])
                    || EF.Functions.Like(city.SearchText, wordStart[1])
                )
                && (
                    EF.Functions.Like(city.SearchText, anywhere[2])
                    || EF.Functions.Like(city.SearchText, wordStart[2])
                )
                && (
                    EF.Functions.Like(city.SearchText, anywhere[3])
                    || EF.Functions.Like(city.SearchText, wordStart[3])
                )
                && (
                    EF.Functions.Like(city.SearchText, anywhere[4])
                    || EF.Functions.Like(city.SearchText, wordStart[4])
                ),
            InName =
                EF.Functions.Like(city.SearchName, anywhere[0])
                || EF.Functions.Like(city.SearchName, wordStart[0])
                || EF.Functions.Like(city.SearchName, anywhere[1])
                || EF.Functions.Like(city.SearchName, wordStart[1])
                || EF.Functions.Like(city.SearchName, anywhere[2])
                || EF.Functions.Like(city.SearchName, wordStart[2])
                || EF.Functions.Like(city.SearchName, anywhere[3])
                || EF.Functions.Like(city.SearchName, wordStart[3])
                || EF.Functions.Like(city.SearchName, anywhere[4])
                || EF.Functions.Like(city.SearchName, wordStart[4]),
        });
    }

    // Rank buckets first, then how close the whole query is to the locality, province and country text, then to the
    // name alone, then name and INDEC id for a stable order. The text comes before the name so a misspelled name with
    // its province ("pilr cordoba") ranks that locality above names that only resemble the province word.
    private static IQueryable<CityRow> Ordered(IQueryable<ScoredCity> matches, string q) =>
        matches
            .OrderBy(match =>
                match.City.SearchName == q ? 0
                : match.City.SearchName.StartsWith(q) ? 1
                : match.InText && match.InName ? 2
                : match.InText ? 3
                : 4
            )
            .ThenByDescending(match =>
                EF.Functions.TrigramsWordSimilarity(q, match.City.SearchText)
            )
            .ThenByDescending(match =>
                EF.Functions.TrigramsWordSimilarity(q, match.City.SearchName)
            )
            .ThenBy(static match => match.City.SearchName)
            .ThenBy(static match => match.City.IndecId)
            .Select(static match => match.City);

    // Under three characters a query is too short for trigrams: name prefixes only, served by the byte-order btree.
    private IQueryable<CityMatch> Prefixed(IQueryable<CityRow> cities, string q) =>
        Project(
                cities
                    .Where(city => city.SearchName.StartsWith(q))
                    .OrderBy(city => city.SearchName == q ? 0 : 1)
                    .ThenBy(static city => city.SearchName)
                    .ThenBy(static city => city.IndecId)
            )
            .Take(SearchResults);

    // Every word must be in the locality, province or country text, or the whole query must be similar to it; ranked
    // exact, prefix, a word in the name itself, words only in the province or country, then similar spellings.
    private IQueryable<CityMatch> Ranked(IQueryable<CityRow> cities, CitySearchTerms terms)
    {
        string q = terms.Plain;
        return Project(
                Ordered(
                    Scored(cities, terms)
                        .Where(match =>
                            match.InText
                            || (
                                EF.Functions.TrigramsAreWordSimilar(q, match.City.SearchText)
                                && EF.Functions.TrigramsWordSimilarity(q, match.City.SearchText)
                                    >= FuzzyThreshold
                            )
                        ),
                    q
                )
            )
            .Take(SearchResults);
    }

    private IQueryable<CityRow> Active(CountryCode? country, SubdivisionCode? subdivision)
    {
        IQueryable<CityRow> cities = db.Set<CityRow>()
            .Where(static city => city.Status == ReferenceStatus.Active);
        if (country is not null)
        {
            string code = country.Value;
            cities = cities.Where(city => city.CountryCode == code);
        }

        if (subdivision is not null)
        {
            string code = subdivision.Value;
            cities = cities.Where(city => city.SubdivisionCode == code);
        }

        return cities;
    }

    // The columns a city item needs, and whether another active city of its province has the same name.
    private IQueryable<CityMatch> Project(IQueryable<CityRow> cities) =>
        cities.Select(city => new CityMatch(
            city.CityId,
            city.IndecId,
            city.Name,
            city.SearchName,
            city.SubdivisionCode,
            city.DepartmentName,
            city.TimeZoneId,
            db.Set<CityRow>()
                .Any(other =>
                    other.SubdivisionCode == city.SubdivisionCode
                    && other.SearchName == city.SearchName
                    && other.CityId != city.CityId
                    && other.Status == ReferenceStatus.Active
                )
        ));

    private CityDto ToDto(CityMatch match, Locale locale)
    {
        SubdivisionCode subdivision = SubdivisionCode.Create(match.SubdivisionCode).Value;
        string subdivisionName =
            subdivisions.Find(subdivision, locale)?.Value.Name
            ?? throw new InvalidOperationException($"No subdivision {subdivision} is loaded.");
        string countryName =
            countries.Find(subdivision.Country, locale)?.Value.Name
            ?? throw new InvalidOperationException($"No country {subdivision.Country} is loaded.");
        string name =
            match.HasNamesake && match.DepartmentName is { Length: > 0 } department
                ? $"{match.Name} ({department})"
                : match.Name;
        return new CityDto(
            CityId.From(match.CityId).Value,
            match.Name,
            subdivision.Country,
            subdivision,
            subdivisionName,
            $"{name}, {subdivisionName}, {countryName}",
            match.TimeZoneId
        );
    }

    // Member-initialized, so EF can read its members in the operators that follow.
    private sealed class ScoredCity
    {
        public CityRow City { get; init; } = null!;

        public bool InText { get; init; }

        public bool InName { get; init; }
    }

    private sealed record CityMatch(
        Guid CityId,
        string IndecId,
        string Name,
        string SearchName,
        string SubdivisionCode,
        string? DepartmentName,
        TimeZoneId TimeZoneId,
        bool HasNamesake
    );
}
