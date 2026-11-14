using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Frappe.Geo.Infrastructure.Countries;

internal sealed class CountryRowConfiguration : IEntityTypeConfiguration<CountryRow>
{
    public void Configure(EntityTypeBuilder<CountryRow> builder)
    {
        builder.ToTable("countries");
        builder.HasKey(c => c.Code);
        builder.Property(c => c.Code).HasColumnName("code").HasMaxLength(2).IsFixedLength();
        builder.Property(c => c.Alpha3).HasColumnName("alpha3").HasMaxLength(3).IsFixedLength();
        builder.Property(c => c.Numeric).HasColumnName("numeric").HasMaxLength(3).IsFixedLength();
        builder.Property(c => c.RetiredAt).HasColumnName("retired_at");

        builder.OwnsMany(c => c.Names, names =>
        {
            names.ToTable("country_names");
            names.WithOwner().HasForeignKey("country_code");
            names.Property<string>("country_code").HasMaxLength(2).IsFixedLength();
            names.Property(n => n.Language).HasColumnName("language");
            names.Property(n => n.Text).HasColumnName("text").HasMaxLength(200);
            names.HasKey("country_code", nameof(CountryNameRow.Language));
        });

        builder.OwnsMany(c => c.Currencies, currencies =>
        {
            currencies.ToTable("country_currencies");
            currencies.WithOwner().HasForeignKey("country_code");
            currencies.Property<string>("country_code").HasMaxLength(2).IsFixedLength();
            currencies.Property(c => c.CurrencyCode).HasColumnName("currency_code").HasMaxLength(3).IsFixedLength();
            currencies.Property(c => c.Rank).HasColumnName("rank");
            currencies.HasKey("country_code", nameof(CountryCurrencyRow.CurrencyCode));
        });
    }
}
