using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Frappe.Geo.Infrastructure.Currencies;

internal sealed class CurrencyRowConfiguration : IEntityTypeConfiguration<CurrencyRow>
{
    public void Configure(EntityTypeBuilder<CurrencyRow> builder)
    {
        builder.ToTable("currencies");
        builder.HasKey(c => c.Code);
        builder.Property(c => c.Code).HasColumnName("code").HasMaxLength(3).IsFixedLength();
        builder.Property(c => c.MinorUnitDigits).HasColumnName("minor_unit_digits");
        builder.Property(c => c.RetiredAt).HasColumnName("retired_at");

        builder.OwnsMany(c => c.Names, names =>
        {
            names.ToTable("currency_names");
            names.WithOwner().HasForeignKey("currency_code");
            names.Property<string>("currency_code").HasMaxLength(3).IsFixedLength();
            names.Property(n => n.Language).HasColumnName("language");
            names.Property(n => n.Name).HasColumnName("name").HasMaxLength(200);
            names.Property(n => n.Symbol).HasColumnName("symbol").HasMaxLength(20);
            names.HasKey("currency_code", nameof(CurrencyNameRow.Language));
        });
    }
}
