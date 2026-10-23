using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Frappe.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.CreateTable(
                name: "principals",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_principals", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "owner_accounts",
                schema: "identity",
                columns: table => new
                {
                    principal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    pending_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_owner_accounts", x => x.principal_id);
                    table.ForeignKey(
                        name: "fk_owner_accounts_principals_id",
                        column: x => x.principal_id,
                        principalSchema: "identity",
                        principalTable: "principals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sessions",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    surface = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    principal_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_sessions_principals_principal_id",
                        column: x => x.principal_id,
                        principalSchema: "identity",
                        principalTable: "principals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "verification_codes",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "text", nullable: false),
                    code_hash = table.Column<string>(type: "text", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    window_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resend_count = table.Column<int>(type: "integer", nullable: false),
                    principal_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_verification_codes", x => x.id);
                    table.ForeignKey(
                        name: "fk_verification_codes_principals_principal_id",
                        column: x => x.principal_id,
                        principalSchema: "identity",
                        principalTable: "principals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "owner_accounts_pending_idx",
                schema: "identity",
                table: "owner_accounts",
                column: "status",
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_principal_id",
                schema: "identity",
                table: "sessions",
                column: "principal_id");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_token_hash",
                schema: "identity",
                table: "sessions",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_verification_codes_principal_id_purpose",
                schema: "identity",
                table: "verification_codes",
                columns: new[] { "principal_id", "purpose" },
                unique: true);

            // Hand-written: EF cannot model expression indexes. One account per address, whatever its case;
            // PrincipalRepository maps a violation of this name to IdentityErrors.EmailTaken.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX owner_accounts_email_key ON identity.owner_accounts (lower(email));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX identity.owner_accounts_email_key;");

            migrationBuilder.DropTable(
                name: "owner_accounts",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "sessions",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "verification_codes",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "principals",
                schema: "identity");
        }
    }
}
