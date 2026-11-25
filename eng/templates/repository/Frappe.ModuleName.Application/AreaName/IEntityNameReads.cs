namespace Frappe.ModuleName.Application.AreaName;

/// <summary>What queries read about AreaName, projected without loading an aggregate (CQRS-08). It never changes anything (CQRS-07).</summary>
public interface IEntityNameReads
{
    /// <summary>The EntityName <paramref name="id"/>, or <see langword="null"/> when there is none.</summary>
    Task<EntityNameView?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Up to <paramref name="limit"/> AreaName by id (UUIDv7, so oldest first), starting right after <paramref name="after"/> (keyset, never an
    /// offset).
    /// </summary>
    Task<IReadOnlyList<EntityNameView>> ListAsync(int limit, Guid? after, CancellationToken cancellationToken);
}
