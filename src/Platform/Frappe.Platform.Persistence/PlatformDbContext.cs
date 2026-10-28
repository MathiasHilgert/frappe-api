using Microsoft.EntityFrameworkCore;

namespace Frappe.Platform.Persistence;

/// <summary>The context of the <c>platform</c> schema, the first one migrated.</summary>
/// <param name="options">The options built by <see cref="FrappeDbContextOptionsBuilderExtensions.UseFrappeDatabase"/>.</param>
public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options)
    : FrappeDbContext(options, Schema)
{
    /// <summary>The schema this context owns.</summary>
    public const string Schema = "platform";
}
