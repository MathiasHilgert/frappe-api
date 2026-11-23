namespace Frappe.UseCases;

/// <summary>
/// Outbound port: the changes one command stages in a module's storage, written as a whole or not at all. The platform
/// drives it around every command (begin, then commit when the Result succeeds, or roll back), so handlers and their
/// repository ports only stage changes and never save. Each module registers its own, keyed by the module name of the
/// namespace convention <c>Frappe.&lt;Module&gt;.*</c> in lower case, over its own storage.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Starts the unit of a command. Inside a transaction somebody else owns (an inbox consumer's), the unit nests in
    /// it: committing the unit only keeps its changes in that transaction, which its owner commits.
    /// </summary>
    Task BeginAsync(CancellationToken cancellationToken);

    /// <summary>Writes what is staged so far without ending the unit; outside a unit, each call is its own transaction.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Writes everything the unit staged in one transaction and ends it.</summary>
    Task CommitAsync(CancellationToken cancellationToken);

    /// <summary>Undoes what the unit wrote and forgets what it staged, so a later save in the same scope writes none of it.</summary>
    Task RollbackAsync(CancellationToken cancellationToken);
}
