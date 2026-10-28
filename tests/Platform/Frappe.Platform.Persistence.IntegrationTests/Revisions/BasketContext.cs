using Microsoft.EntityFrameworkCore;

namespace Frappe.Platform.Persistence.IntegrationTests.Revisions;

// A test-only model in its own schema, so the migrations model never sees it.
internal sealed class BasketContext(DbContextOptions<BasketContext> options) : DbContext(options)
{
    public const string Schema = "revision_probe";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
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
