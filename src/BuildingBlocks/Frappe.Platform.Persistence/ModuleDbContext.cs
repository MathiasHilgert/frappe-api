using Microsoft.EntityFrameworkCore;

namespace Frappe.Platform.Persistence;

/// <summary>Each module owns one DbContext mapped to its own PostgreSQL schema.</summary>
public abstract class ModuleDbContext(DbContextOptions options, string schema) : DbContext(options)
{
    public string Schema { get; } = schema;

    /// <summary>Applies the entity configurations found in the concrete context's assembly.</summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);
    }
}
