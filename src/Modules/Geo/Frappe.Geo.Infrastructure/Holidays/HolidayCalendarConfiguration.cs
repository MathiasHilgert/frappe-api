using Frappe.Geo.Domain.Holidays;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Frappe.Geo.Infrastructure.Holidays;

/// <summary>
/// The calendar aggregate as stored: <c>holiday_calendars</c>, one per jurisdiction, year and scope, and its entries in
/// <c>calendar_entries</c>.
/// </summary>
internal sealed class HolidayCalendarConfiguration : IEntityTypeConfiguration<HolidayCalendar>
{
    public void Configure(EntityTypeBuilder<HolidayCalendar> builder)
    {
        builder.ToTable("holiday_calendars");
        builder.Ignore(c => c.DomainEvents);
        builder.Ignore(c => c.Imported);
        builder.Ignore(c => c.Effective);
        builder.Property(c => c.Jurisdiction).HasColumnName("jurisdiction").HasMaxLength(6)
            .HasConversion(j => j.Value, key => Jurisdiction.FromKey(key));
        builder.Property(c => c.Year).HasColumnName("year")
            .HasConversion(y => y.Value, value => HolidayYear.Create(value).Value);
        builder.Property(c => c.Scope).HasColumnName("scope").HasMaxLength(CalendarScope.MaxLength)
            .HasConversion(s => s.Value, value => CalendarScope.Create(value).Value);
        builder.HasKey(c => new { c.Jurisdiction, c.Year, c.Scope });

        // Every change of the entries moves the revision, so saving it updates this row too, checked against the row
        // version (xmin) read with the calendar: of two writers (the sync, an administrator) that read the same calendar,
        // the second one to save finds another row version and fails instead of overwriting the first one's change.
        builder.Property(c => c.Revision).HasColumnName("revision");
        builder.Property<uint>("xmin").HasColumnName("xmin").IsRowVersion();

        // The credit owed to the calendar's source; null until a source declares it on an import.
        builder.ComplexProperty(c => c.Attribution, attribution =>
        {
            attribution.Property(a => a.Name).HasColumnName("attribution_name").HasMaxLength(Attribution.MaxNameLength);
            attribution.Property(a => a.Url).HasColumnName("attribution_url").HasMaxLength(500);
            attribution.Property(a => a.License).HasColumnName("attribution_license").HasMaxLength(Attribution.MaxLicenseLength);
        });

        builder.OwnsMany(c => c.Entries, entries =>
        {
            entries.ToTable("calendar_entries");
            entries.WithOwner().HasForeignKey("jurisdiction", "year", "scope");
            entries.Property(e => e.Layer).HasColumnName("layer").HasConversion<string>().HasMaxLength(16);
            entries.Property(e => e.Date).HasColumnName("date");
            entries.Property(e => e.Name).HasColumnName("name").HasMaxLength(Observance.MaxNameLength);
            entries.Property(e => e.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(32);
            entries.Property(e => e.MovedFrom).HasColumnName("moved_from");
            entries.Property(e => e.Community).HasColumnName("community").HasMaxLength(32);
            entries.Property(e => e.LegalCitation).HasColumnName("legal_citation").HasMaxLength(200);
            entries.Property(e => e.LegalText).HasColumnName("legal_text").HasMaxLength(500);
            entries.HasKey("jurisdiction", "year", "scope", nameof(CalendarEntry.Layer), nameof(CalendarEntry.Date), nameof(CalendarEntry.Name));
        });
        builder.Navigation(c => c.Entries).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
