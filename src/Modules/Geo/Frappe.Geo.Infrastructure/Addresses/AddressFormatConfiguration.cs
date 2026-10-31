using Frappe.Geo.Infrastructure.Countries;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Frappe.Geo.Infrastructure.Addresses;

internal sealed class AddressFormatConfiguration : IEntityTypeConfiguration<AddressFormatRow>
{
    public void Configure(EntityTypeBuilder<AddressFormatRow> builder)
    {
        builder.ToTable(
            "address_formats",
            static table =>
            {
                table.HasCheckConstraint(
                    "ck_address_formats_postal_code_pattern",
                    "postal_code_pattern <> ''"
                );
                table.HasCheckConstraint(
                    "ck_address_formats_required_fields",
                    "required_fields <@ ARRAY['street_line', 'city', 'subdivision', 'postal_code']"
                );
                table.HasCheckConstraint(
                    "ck_address_formats_subdivision_label",
                    "subdivision_label IN ('area', 'county', 'department', 'district', 'do_si', 'emirate', "
                        + "'island', 'oblast', 'parish', 'prefecture', 'province', 'state')"
                );
            }
        );
        builder.HasKey(static format => format.CountryCode);
        builder
            .HasOne<CountryRow>()
            .WithOne()
            .HasForeignKey<AddressFormatRow>(static format => format.CountryCode)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
