using Frappe.Geo.Domain.Territory;

namespace Frappe.Geo.Application.Territory;

/// <summary>
/// The other names of a subdivision, in two layers: the source's, which each territory sync replaces, and the manual
/// ones, which only an administrator adds or removes. Both are served and searched alike.
/// </summary>
public interface ISubdivisionAliases
{
    /// <summary>Both layers, by alias.</summary>
    Task<IReadOnlyList<SubdivisionAliasView>> ListAsync(SubdivisionCode subdivision, CancellationToken cancellationToken);

    /// <summary>
    /// Stages a manual alias, unless the subdivision is already known by it (its name, its code's suffix or an alias of
    /// either layer) or another current subdivision of its country is; all compared ignoring case and accents, the way
    /// addresses map names to codes.
    /// </summary>
    Task<AliasAddition> TryAddAsync(SubdivisionCode subdivision, string alias, CancellationToken cancellationToken);

    /// <summary>Stages the removal of the manual alias equal to <paramref name="alias"/> ignoring case and accents; a source alias is never removed.</summary>
    Task<AliasRemoval> TryRemoveAsync(SubdivisionCode subdivision, string alias, CancellationToken cancellationToken);
}
