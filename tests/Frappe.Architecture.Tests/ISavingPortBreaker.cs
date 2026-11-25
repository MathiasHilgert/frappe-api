namespace Frappe.Architecture.Tests;

/// <summary>A port that commits on its own, the shape ARCH-13 forbids.</summary>
public interface ISavingPortBreaker
{
    /// <summary>Saves, which a port must leave to the unit of work.</summary>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>The number of rows saved.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
