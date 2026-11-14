using Frappe.Geo.Domain.Territory;

namespace Frappe.Geo.Application.Territory;

/// <summary>Outbound port: the territory Geo keeps, retired rows excluded from listings and searches.</summary>
public interface ITerritoryDirectory
{
    /// <summary>A country's subdivisions, ordered by code.</summary>
    Task<IReadOnlyList<SubdivisionView>> ListSubdivisionsAsync(string countryCode, CancellationToken cancellationToken);

    Task<SubdivisionView?> FindSubdivisionAsync(SubdivisionCode code, CancellationToken cancellationToken);

    /// <summary>Whether Geo has ever held the subdivision: a retired one counts, since its manual aliases outlive it.</summary>
    Task<bool> HasHeldSubdivisionAsync(SubdivisionCode code, CancellationToken cancellationToken);

    /// <summary>A subdivision's areas, of one level or of every level, ordered by level then name.</summary>
    Task<IReadOnlyList<AdministrativeAreaView>> ListAreasAsync(SubdivisionCode subdivision, int? level, CancellationToken cancellationToken);

    /// <summary>An area by its public id; a retired one still resolves, since another module may keep its id.</summary>
    Task<AdministrativeAreaView?> FindAreaAsync(string countryCode, string sourceId, CancellationToken cancellationToken);

    /// <summary>
    /// A page of a subdivision's localities: by name when <paramref name="search"/> is null, else the most similar names
    /// first (typos tolerated). <paramref name="after"/> is the position the previous page ended at.
    /// </summary>
    Task<LocalityPage> PageLocalitiesAsync(SubdivisionCode subdivision, string? search, int limit, LocalityCursor? after, CancellationToken cancellationToken);

    /// <summary>A locality by its public id; a retired one still resolves.</summary>
    Task<LocalityView?> FindLocalityAsync(string countryCode, string sourceId, CancellationToken cancellationToken);
}
