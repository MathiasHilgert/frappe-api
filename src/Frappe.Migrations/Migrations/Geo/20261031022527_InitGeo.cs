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

            migrationBuilder.EnsureSchema(name: "geo");

            migrationBuilder.CreateTable(
                name: "timezones",
                schema: "geo",
                columns: table => new
                {
                    name = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    canonical_name = table.Column<string>(
                        type: "text",
                        nullable: true,
                        collation: "C"
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_timezones", x => x.name);
                    table.CheckConstraint(
                        "ck_timezones_canonical_name_not_self",
                        "canonical_name <> name"
                    );
                    table.CheckConstraint(
                        "ck_timezones_name_format",
                        "name ~ '^[A-Za-z0-9_+-]+(/[A-Za-z0-9_+-]+)*$'"
                    );
                    table.ForeignKey(
                        name: "fk_timezones_timezones_canonical_name",
                        column: x => x.canonical_name,
                        principalSchema: "geo",
                        principalTable: "timezones",
                        principalColumn: "name",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "ix_timezones_canonical_name",
                schema: "geo",
                table: "timezones",
                column: "canonical_name"
            );

            // A forgotten grant fails closed: the app role reads only what a migration names.
            migrationBuilder.Sql("GRANT SELECT ON geo.timezones TO frappe_app;");
        }
    }
}
