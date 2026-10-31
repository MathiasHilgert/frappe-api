using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Frappe.Migrations.Migrations.Geo
{
    /// <inheritdoc />
    public partial class AddAddressFormats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedSql.Read("AddAddressFormats.sql"));

            migrationBuilder.CreateTable(
                name: "address_formats",
                schema: "geo",
                columns: table => new
                {
                    country_code = table.Column<string>(type: "text", nullable: false),
                    postal_code_pattern = table.Column<string>(type: "text", nullable: true),
                    postal_code_examples = table.Column<string[]>(type: "text[]", nullable: false),
                    postal_code_required = table.Column<bool>(type: "boolean", nullable: false),
                    required_fields = table.Column<string[]>(type: "text[]", nullable: false),
                    subdivision_label = table.Column<string>(type: "text", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_address_formats", x => x.country_code);
                    table.CheckConstraint(
                        "ck_address_formats_postal_code_pattern",
                        "postal_code_pattern <> ''"
                    );
                    table.CheckConstraint(
                        "ck_address_formats_required_fields",
                        "required_fields <@ ARRAY['street_line', 'city', 'subdivision', 'postal_code']"
                    );
                    table.CheckConstraint(
                        "ck_address_formats_subdivision_label",
                        "subdivision_label IN ('area', 'county', 'department', 'district', 'do_si', 'emirate', 'island', 'oblast', 'parish', 'prefecture', 'province', 'state')"
                    );
                    table.ForeignKey(
                        name: "fk_address_formats_countries_country_code",
                        column: x => x.country_code,
                        principalSchema: "geo",
                        principalTable: "countries",
                        principalColumn: "country_code",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.Sql(EmbeddedSql.Read("AddAddressFormatsGrants.sql"));
        }
    }
}
