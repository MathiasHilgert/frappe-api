using Frappe.Geo.Domain.Timezones;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Frappe.Geo.Infrastructure.Timezones;

internal sealed class TimezoneConfiguration : IEntityTypeConfiguration<Timezone>
{
    public void Configure(EntityTypeBuilder<Timezone> builder)
    {
        builder.ToTable(
            "timezones",
            static table =>
            {
                // IANA ids are slash-separated ASCII components: America/Argentina/Cordoba, Etc/GMT+5, UTC.
                table.HasCheckConstraint(
                    "ck_timezones_name_format",
                    "name ~ '^[A-Za-z0-9_+-]+(/[A-Za-z0-9_+-]+)*$'"
                );
                table.HasCheckConstraint(
                    "ck_timezones_canonical_name_not_self",
                    "canonical_name <> name"
                );
            }
        );
        builder.HasKey(static zone => zone.Name);
        // Byte order, so the list sorts the same whatever the database's default collation is.
        builder.Property(static zone => zone.Name).UseCollation("C");
        builder.Property(static zone => zone.CanonicalName).UseCollation("C");
        builder
            .HasOne<Timezone>()
            .WithMany()
            .HasForeignKey(static zone => zone.CanonicalName)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
