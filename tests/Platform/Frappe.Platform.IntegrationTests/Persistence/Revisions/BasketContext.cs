using Frappe.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Platform.IntegrationTests.Persistence.Revisions;

// A test-only context in its own schema, so the migrations model never sees it; Revision is a
// concurrency token only through the base context, as in every module's context.
internal sealed class BasketContext(DbContextOptions<BasketContext> options)
    : FrappeDbContext(options, Schema)
{
    public const string Schema = "revision_probe";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Basket>(basket =>
        {
            basket
                .Property(b => b.Id)
                .HasConversion(id => id.Value, value => new BasketId(value))
                .ValueGeneratedNever();
            basket.Ignore(b => b.DomainEvents);
            basket.OwnsOne(b => b.Note);
            basket.HasMany(b => b.Lines).WithOne().HasForeignKey("BasketId").IsRequired();
        });
        modelBuilder
            .Entity<BasketLine>()
            .Property(l => l.Id)
            .HasConversion(id => id.Value, value => new BasketLineId(value))
            .ValueGeneratedNever();
    }
}
