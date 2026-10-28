using Microsoft.EntityFrameworkCore;

namespace Frappe.Platform.Persistence;

/// <summary>
/// The one EF Core context every module shares. It exposes no <c>DbSet</c> properties: a module reaches
/// only its own entities through <see cref="DbContext.Set{TEntity}()"/> and applies only its own configurations.
/// </summary>
/// <param name="options">The options built by <see cref="FrappeDbContextOptionsBuilderExtensions.UseFrappeDatabase"/>.</param>
public sealed class FrappeDbContext(DbContextOptions<FrappeDbContext> options) : DbContext(options);
