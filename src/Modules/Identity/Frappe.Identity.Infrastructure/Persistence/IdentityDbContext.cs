using Frappe.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Identity.Infrastructure.Persistence;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : ModuleDbContext(options, IdentityModule.Schema);
