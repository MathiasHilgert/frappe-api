using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Frappe.Geo.Infrastructure.Territory;

internal sealed class AdministrativeAreaRowConfiguration : IEntityTypeConfiguration<AdministrativeAreaRow>
{
    public void Configure(EntityTypeBuilder<AdministrativeAreaRow> builder)
    {
        builder.ToTable("administrative_areas");
        builder.HasKey(a => new { a.CountryCode, a.SourceId });
        builder.Property(a => a.CountryCode).HasColumnName("country_code").HasMaxLength(2).IsFixedLength();
        builder.Property(a => a.SourceId).HasColumnName("source_id").HasMaxLength(32);
        builder.Property(a => a.SubdivisionCode).HasColumnName("subdivision_code").HasMaxLength(6);
        builder.Property(a => a.Level).HasColumnName("level");
        builder.Property(a => a.Category).HasColumnName("category").HasMaxLength(100);
        builder.Property(a => a.Name).HasColumnName("name").HasMaxLength(200);
        builder.Property(a => a.IsLocalGovernment).HasColumnName("is_local_government");
        builder.Property(a => a.Latitude).HasColumnName("latitude");
        builder.Property(a => a.Longitude).HasColumnName("longitude");
        builder.Property(a => a.RetiredAt).HasColumnName("retired_at");
        builder.HasIndex(a => new { a.SubdivisionCode, a.Level }).HasDatabaseName("ix_administrative_areas_subdivision_level");
    }
}
