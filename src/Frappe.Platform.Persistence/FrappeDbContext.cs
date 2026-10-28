using Microsoft.EntityFrameworkCore;

namespace Frappe.Platform.Persistence;

// The one context every module shares. It exposes no DbSet properties: a module reaches only its own
// entities through Set<T>() and applies only its own configurations.
public sealed class FrappeDbContext(DbContextOptions<FrappeDbContext> options) : DbContext(options);
