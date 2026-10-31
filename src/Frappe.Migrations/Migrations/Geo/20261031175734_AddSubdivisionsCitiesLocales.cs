using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Frappe.Migrations.Migrations.Geo
{
    /// <inheritdoc />
    public partial class AddSubdivisionsCitiesLocales : Migration
    {
        private static readonly string[] _countrySearch =
        [
            "country_code",
            "search_name",
            "indec_id",
        ];
        private static readonly string[] _search = ["search_name", "indec_id"];
        private static readonly string[] _subdivisionSearch =
        [
            "subdivision_code",
            "search_name",
            "indec_id",
        ];
        private static readonly string[] _trigramOperators = ["gin_trgm_ops"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedSql.Read("AddSubdivisionsCitiesLocales.sql"));

            CreateLocales(migrationBuilder);
            CreateSubdivisions(migrationBuilder);
            CreateCities(migrationBuilder);
            CreateIndexes(migrationBuilder);

            migrationBuilder.Sql(EmbeddedSql.Read("AddSubdivisionsCitiesLocalesGrants.sql"));
        }

        private static void CreateLocales(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "locales",
                schema: "geo",
                columns: table => new
                {
                    locale_tag = table.Column<string>(type: "text", nullable: false),
                    language_code = table.Column<string>(type: "text", nullable: false),
                    text_direction = table.Column<string>(type: "text", nullable: false),
                    endonym = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_locales", x => x.locale_tag);
                    table.CheckConstraint("ck_locales_status", "status IN ('Active', 'Retired')");
                    table.CheckConstraint(
                        "ck_locales_text_direction",
                        "text_direction IN ('ltr', 'rtl')"
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "locale_names",
                schema: "geo",
                columns: table => new
                {
                    locale_tag = table.Column<string>(type: "text", nullable: false),
                    locale = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_locale_names", x => new { x.locale_tag, x.locale });
                    table.CheckConstraint("ck_locale_names_name", "name <> ''");
                    table.ForeignKey(
                        name: "fk_locale_names_locales_locale_tag",
                        column: x => x.locale_tag,
                        principalSchema: "geo",
                        principalTable: "locales",
                        principalColumn: "locale_tag",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );
        }

        private static void CreateSubdivisions(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "subdivisions",
                schema: "geo",
                columns: table => new
                {
                    subdivision_code = table.Column<string>(type: "text", nullable: false),
                    country_code = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subdivisions", x => x.subdivision_code);
                    table.CheckConstraint(
                        "ck_subdivisions_status",
                        "status IN ('Active', 'Retired')"
                    );
                    table.CheckConstraint(
                        "ck_subdivisions_subdivision_code",
                        "subdivision_code ~ '^[A-Z]{2}-[A-Z0-9]{1,3}$'"
                    );
                    table.ForeignKey(
                        name: "fk_subdivisions_countries_country_code",
                        column: x => x.country_code,
                        principalSchema: "geo",
                        principalTable: "countries",
                        principalColumn: "country_code",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "subdivision_names",
                schema: "geo",
                columns: table => new
                {
                    subdivision_code = table.Column<string>(type: "text", nullable: false),
                    locale = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "pk_subdivision_names",
                        x => new { x.subdivision_code, x.locale }
                    );
                    table.CheckConstraint("ck_subdivision_names_name", "name <> ''");
                    table.ForeignKey(
                        name: "fk_subdivision_names_subdivisions_subdivision_code",
                        column: x => x.subdivision_code,
                        principalSchema: "geo",
                        principalTable: "subdivisions",
                        principalColumn: "subdivision_code",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );
        }

        private static void CreateCities(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cities",
                schema: "geo",
                columns: table => new
                {
                    city_id = table.Column<Guid>(type: "uuid", nullable: false),
                    indec_id = table.Column<string>(type: "text", nullable: false),
                    country_code = table.Column<string>(type: "text", nullable: false),
                    subdivision_code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    department_name = table.Column<string>(type: "text", nullable: true),
                    search_name = table.Column<string>(
                        type: "text",
                        nullable: false,
                        collation: "C"
                    ),
                    search_text = table.Column<string>(
                        type: "text",
                        nullable: false,
                        collation: "C"
                    ),
                    time_zone_id = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cities", x => x.city_id);
                    table.CheckConstraint("ck_cities_indec_id", "indec_id ~ '^[0-9]+$'");
                    table.CheckConstraint("ck_cities_name", "name <> ''");
                    table.CheckConstraint("ck_cities_status", "status IN ('Active', 'Retired')");
                    table.CheckConstraint("ck_cities_time_zone_id", "time_zone_id <> ''");
                    table.ForeignKey(
                        name: "fk_cities_countries_country_code",
                        column: x => x.country_code,
                        principalSchema: "geo",
                        principalTable: "countries",
                        principalColumn: "country_code",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "fk_cities_subdivisions_subdivision_code",
                        column: x => x.subdivision_code,
                        principalSchema: "geo",
                        principalTable: "subdivisions",
                        principalColumn: "subdivision_code",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );
        }

        private static void CreateIndexes(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_cities_country_search",
                schema: "geo",
                table: "cities",
                columns: _countrySearch
            );

            migrationBuilder.CreateIndex(
                name: "ix_cities_indec_id",
                schema: "geo",
                table: "cities",
                column: "indec_id",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "ix_cities_search",
                schema: "geo",
                table: "cities",
                columns: _search
            );

            migrationBuilder
                .CreateIndex(
                    name: "ix_cities_search_text_trgm",
                    schema: "geo",
                    table: "cities",
                    column: "search_text"
                )
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", _trigramOperators);

            migrationBuilder.CreateIndex(
                name: "ix_cities_subdivision_search",
                schema: "geo",
                table: "cities",
                columns: _subdivisionSearch
            );

            migrationBuilder.CreateIndex(
                name: "ix_subdivisions_country_code",
                schema: "geo",
                table: "subdivisions",
                column: "country_code"
            );
        }
    }
}
