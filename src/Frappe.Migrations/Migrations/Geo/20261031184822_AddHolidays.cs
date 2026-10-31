using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Frappe.Migrations.Migrations.Geo
{
    /// <inheritdoc />
    public partial class AddHolidays : Migration
    {
        private static readonly string[] _holidayKey =
        [
            "country_code",
            "subdivision_code",
            "date",
            "holiday_key",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedSql.Read("AddHolidays.sql"));

            CreateHolidays(migrationBuilder);
            CreateHolidayIndexes(migrationBuilder);
            CreateHolidayNames(migrationBuilder);

            migrationBuilder.Sql(EmbeddedSql.Read("AddHolidaysGrants.sql"));
        }

        private static void CreateHolidays(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "holidays",
                schema: "geo",
                columns: table => new
                {
                    holiday_id = table.Column<Guid>(type: "uuid", nullable: false),
                    country_code = table.Column<string>(type: "text", nullable: false),
                    subdivision_code = table.Column<string>(type: "text", nullable: true),
                    date = table.Column<LocalDate>(type: "date", nullable: false),
                    holiday_key = table.Column<string>(type: "text", nullable: false),
                    @class = table.Column<string>(name: "class", type: "text", nullable: false),
                    origin = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holidays", x => x.holiday_id);
                    table.CheckConstraint(
                        "ck_holidays_class",
                        "class IN ('Holiday', 'NonWorkingDay')"
                    );
                    table.CheckConstraint(
                        "ck_holidays_holiday_key",
                        "holiday_key ~ '^[a-z0-9-]+$'"
                    );
                    table.CheckConstraint(
                        "ck_holidays_origin",
                        "origin IN ('Fixed', 'Movable', 'Bridge', 'Decreed')"
                    );
                    table.CheckConstraint("ck_holidays_status", "status IN ('Active', 'Retired')");
                    table.CheckConstraint(
                        "ck_holidays_subdivision_code",
                        "subdivision_code ~ '^[A-Z]{2}-[A-Z0-9]{1,3}$'"
                    );
                    table.ForeignKey(
                        name: "fk_holidays_countries_country_code",
                        column: x => x.country_code,
                        principalSchema: "geo",
                        principalTable: "countries",
                        principalColumn: "country_code",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "fk_holidays_subdivisions_subdivision_code",
                        column: x => x.subdivision_code,
                        principalSchema: "geo",
                        principalTable: "subdivisions",
                        principalColumn: "subdivision_code",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );
        }

        private static void CreateHolidayIndexes(MigrationBuilder migrationBuilder)
        {
            migrationBuilder
                .CreateIndex(
                    name: "ix_holidays_country_code_subdivision_code_date_holiday_key",
                    schema: "geo",
                    table: "holidays",
                    columns: _holidayKey,
                    unique: true
                )
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_holidays_subdivision_code",
                schema: "geo",
                table: "holidays",
                column: "subdivision_code"
            );
        }

        private static void CreateHolidayNames(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "holiday_names",
                schema: "geo",
                columns: table => new
                {
                    holiday_id = table.Column<Guid>(type: "uuid", nullable: false),
                    locale = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holiday_names", x => new { x.holiday_id, x.locale });
                    table.CheckConstraint("ck_holiday_names_name", "name <> ''");
                    table.ForeignKey(
                        name: "fk_holiday_names_holidays_holiday_id",
                        column: x => x.holiday_id,
                        principalSchema: "geo",
                        principalTable: "holidays",
                        principalColumn: "holiday_id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );
        }
    }
}
