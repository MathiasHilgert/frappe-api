using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Frappe.Migrations.Migrations.Geo
{
    /// <inheritdoc />
    public partial class InitGeo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedSql.Read("InitGeo.sql"));

            CreateCurrencies(migrationBuilder);
            CreateCountries(migrationBuilder);
            CreateNames(migrationBuilder);
            CreateCountryIndexes(migrationBuilder);

            migrationBuilder.Sql(EmbeddedSql.Read("InitGeoGrants.sql"));
        }

        private static void CreateCurrencies(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "currencies",
                schema: "geo",
                columns: table => new
                {
                    currency_code = table.Column<string>(type: "text", nullable: false),
                    numeric_code = table.Column<string>(type: "text", nullable: true),
                    minor_unit = table.Column<short>(type: "smallint", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_currencies", x => x.currency_code);
                    table.CheckConstraint(
                        "ck_currencies_currency_code",
                        "currency_code ~ '^[A-Z]{3}$'"
                    );
                    table.CheckConstraint("ck_currencies_minor_unit", "minor_unit BETWEEN 0 AND 4");
                    table.CheckConstraint(
                        "ck_currencies_numeric_code",
                        "numeric_code ~ '^[0-9]{3}$'"
                    );
                    table.CheckConstraint(
                        "ck_currencies_status",
                        "status IN ('Active', 'Retired')"
                    );
                }
            );
        }

        private static void CreateCountries(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "countries",
                schema: "geo",
                columns: table => new
                {
                    country_code = table.Column<string>(type: "text", nullable: false),
                    alpha3 = table.Column<string>(type: "text", nullable: false),
                    numeric_code = table.Column<string>(type: "text", nullable: false),
                    default_currency_code = table.Column<string>(type: "text", nullable: true),
                    calling_code = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_countries", x => x.country_code);
                    table.CheckConstraint("ck_countries_alpha3", "alpha3 ~ '^[A-Z]{3}$'");
                    table.CheckConstraint(
                        "ck_countries_calling_code",
                        "calling_code ~ '^[0-9]{1,3}$'"
                    );
                    table.CheckConstraint(
                        "ck_countries_country_code",
                        "country_code ~ '^[A-Z]{2}$'"
                    );
                    table.CheckConstraint(
                        "ck_countries_numeric_code",
                        "numeric_code ~ '^[0-9]{3}$'"
                    );
                    table.CheckConstraint("ck_countries_status", "status IN ('Active', 'Retired')");
                    table.ForeignKey(
                        name: "fk_countries_currencies_default_currency_code",
                        column: x => x.default_currency_code,
                        principalSchema: "geo",
                        principalTable: "currencies",
                        principalColumn: "currency_code",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );
        }

        private static void CreateNames(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "currency_names",
                schema: "geo",
                columns: table => new
                {
                    currency_code = table.Column<string>(type: "text", nullable: false),
                    locale = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_currency_names", x => new { x.currency_code, x.locale });
                    table.CheckConstraint("ck_currency_names_name", "name <> ''");
                    table.ForeignKey(
                        name: "fk_currency_names_currencies_currency_code",
                        column: x => x.currency_code,
                        principalSchema: "geo",
                        principalTable: "currencies",
                        principalColumn: "currency_code",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "country_names",
                schema: "geo",
                columns: table => new
                {
                    country_code = table.Column<string>(type: "text", nullable: false),
                    locale = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_country_names", x => new { x.country_code, x.locale });
                    table.CheckConstraint("ck_country_names_name", "name <> ''");
                    table.ForeignKey(
                        name: "fk_country_names_countries_country_code",
                        column: x => x.country_code,
                        principalSchema: "geo",
                        principalTable: "countries",
                        principalColumn: "country_code",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );
        }

        private static void CreateCountryIndexes(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_countries_alpha3",
                schema: "geo",
                table: "countries",
                column: "alpha3",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "ix_countries_default_currency_code",
                schema: "geo",
                table: "countries",
                column: "default_currency_code"
            );

            migrationBuilder.CreateIndex(
                name: "ix_countries_numeric_code",
                schema: "geo",
                table: "countries",
                column: "numeric_code",
                unique: true
            );
        }
    }
}
