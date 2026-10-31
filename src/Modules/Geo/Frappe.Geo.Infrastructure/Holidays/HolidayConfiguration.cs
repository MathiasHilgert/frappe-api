using Frappe.Geo.Infrastructure.Countries;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Frappe.Geo.Infrastructure.Holidays;

internal sealed class HolidayConfiguration
    : IEntityTypeConfiguration<HolidayRow>,
        IEntityTypeConfiguration<HolidayNameRow>
{
    public void Configure(EntityTypeBuilder<HolidayRow> builder)
    {
        builder.ToTable(
            "holidays",
            static table =>
            {
                table.HasCheckConstraint(
                    "ck_holidays_subdivision_code",
                    "subdivision_code ~ '^[A-Z]{2}-[A-Z0-9]{1,3}$'"
                );
                table.HasCheckConstraint("ck_holidays_holiday_key", "holiday_key ~ '^[a-z0-9-]+$'");
                table.HasCheckConstraint(
                    "ck_holidays_class",
                    "class IN ('Holiday', 'NonWorkingDay')"
                );
                table.HasCheckConstraint(
                    "ck_holidays_origin",
                    "origin IN ('Fixed', 'Movable', 'Bridge', 'Decreed')"
                );
                table.HasCheckConstraint("ck_holidays_status", "status IN ('Active', 'Retired')");
            }
        );
        builder.HasKey(static holiday => holiday.HolidayId);
        builder
            .HasIndex(static holiday => new
            {
                holiday.CountryCode,
                holiday.SubdivisionCode,
                holiday.Date,
                holiday.HolidayKey,
            })
            .IsUnique()
            .AreNullsDistinct(false);
        builder.Property(static holiday => holiday.Class).HasConversion<string>();
        builder.Property(static holiday => holiday.Origin).HasConversion<string>();
        builder.Property(static holiday => holiday.Status).HasConversion<string>();
        // TODO: add the subdivisions FK and ix_holidays_subdivision_code (once the subdivisions catalogue is seeded)
        builder
            .HasOne<CountryRow>()
            .WithMany()
            .HasForeignKey(static holiday => holiday.CountryCode)
            .OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<HolidayNameRow> builder)
    {
        builder.ToTable(
            "holiday_names",
            static table => table.HasCheckConstraint("ck_holiday_names_name", "name <> ''")
        );
        builder.HasKey(static name => new { name.HolidayId, name.Locale });
        builder
            .HasOne<HolidayRow>()
            .WithMany()
            .HasForeignKey(static name => name.HolidayId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
