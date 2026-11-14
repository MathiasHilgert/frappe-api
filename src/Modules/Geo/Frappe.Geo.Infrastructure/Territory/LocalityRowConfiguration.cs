using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Frappe.Geo.Infrastructure.Territory;

internal sealed class LocalityRowConfiguration : IEntityTypeConfiguration<LocalityRow>
{
    public void Configure(EntityTypeBuilder<LocalityRow> builder)
    {
        builder.ToTable("localities");
        builder.HasKey(l => new { l.CountryCode, l.SourceId });
        builder.Property(l => l.CountryCode).HasColumnName("country_code").HasMaxLength(2).IsFixedLength();
        builder.Property(l => l.SourceId).HasColumnName("source_id").HasMaxLength(32);
        builder.Property(l => l.SubdivisionCode).HasColumnName("subdivision_code").HasMaxLength(6);
        builder.Property(l => l.AreaId).HasColumnName("area_id").HasMaxLength(32);
        builder.Property(l => l.LocalGovernmentId).HasColumnName("local_government_id").HasMaxLength(32);
        builder.Property(l => l.Name).HasColumnName("name").HasMaxLength(200);
        builder.Property(l => l.SearchName).HasColumnName("search_name").HasMaxLength(200);
        builder.Property(l => l.Category).HasColumnName("category").HasMaxLength(100);
        builder.Property(l => l.Latitude).HasColumnName("latitude");
        builder.Property(l => l.Longitude).HasColumnName("longitude");
        builder.Property(l => l.RetiredAt).HasColumnName("retired_at");
        builder.HasIndex(l => new { l.SubdivisionCode, l.SearchName }).HasDatabaseName("ix_localities_subdivision_search_name");
        builder.HasIndex(l => l.SearchName).HasDatabaseName("ix_localities_search_name_trgm").HasMethod("gin").HasOperators("gin_trgm_ops");
    }
}
