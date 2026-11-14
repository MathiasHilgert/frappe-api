using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Frappe.Geo.Infrastructure.Territory;

internal sealed class SubdivisionRowConfiguration : IEntityTypeConfiguration<SubdivisionRow>
{
    public void Configure(EntityTypeBuilder<SubdivisionRow> builder)
    {
        builder.ToTable("subdivisions");
        builder.HasKey(s => s.Code);
        builder.Property(s => s.Code).HasColumnName("code").HasMaxLength(6);
        builder.Property(s => s.CountryCode).HasColumnName("country_code").HasMaxLength(2).IsFixedLength();
        builder.Property(s => s.SourceId).HasColumnName("source_id").HasMaxLength(32);
        builder.Property(s => s.Name).HasColumnName("name").HasMaxLength(200);
        builder.Property(s => s.Category).HasColumnName("category").HasMaxLength(100);
        builder.Property(s => s.Latitude).HasColumnName("latitude");
        builder.Property(s => s.Longitude).HasColumnName("longitude");
        builder.Property(s => s.RetiredAt).HasColumnName("retired_at");
        builder.HasIndex(s => s.CountryCode).HasDatabaseName("ix_subdivisions_country_code");
        builder.OwnsMany(s => s.Aliases, aliases =>
        {
            aliases.ToTable("subdivision_aliases");
            aliases.WithOwner().HasForeignKey("subdivision_code");
            aliases.Property<string>("subdivision_code").HasMaxLength(6);
            aliases.Property(a => a.Alias).HasColumnName("alias").HasMaxLength(200);
            aliases.Property(a => a.SearchForm).HasColumnName("search_form").HasMaxLength(200);
            aliases.Property(a => a.Manual).HasColumnName("manual");
            aliases.HasKey("subdivision_code", nameof(SubdivisionAliasRow.Alias));
            aliases.HasIndex(a => a.SearchForm).HasDatabaseName("ix_subdivision_aliases_search_form");
            aliases.HasIndex("subdivision_code", nameof(SubdivisionAliasRow.SearchForm)).IsUnique().HasDatabaseName(SubdivisionAliasRow.OneBySearchForm);
        });
    }
}
