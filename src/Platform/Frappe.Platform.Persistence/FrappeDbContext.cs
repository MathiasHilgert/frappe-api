using Microsoft.EntityFrameworkCore;

namespace Frappe.Platform.Persistence;

/// <summary>
/// The base of every module's EF Core context. Each context owns one schema: its tables, its migrations
/// and its migrations history table live there, and it maps no table of another schema. Every aggregate
/// root's <c>Revision</c> is its concurrency token.
/// </summary>
/// <param name="options">The options built by <see cref="FrappeDbContextOptionsBuilderExtensions.UseFrappeDatabase"/>.</param>
/// <param name="schema">The schema this context owns.</param>
public abstract class FrappeDbContext(DbContextOptions options, string schema) : DbContext(options)
{
    /// <summary>Puts every table of the context in its schema; an override calls this first.</summary>
    /// <param name="modelBuilder">The builder of the context's model.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.HasDefaultSchema(schema);

    /// <summary>Adds the conventions every context shares; an override calls this first.</summary>
    /// <param name="configurationBuilder">The builder of the context's conventions.</param>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Conventions.Add(static _ => new RevisionConcurrencyTokenConvention());
}
