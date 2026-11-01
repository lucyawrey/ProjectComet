using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShapeLand.Data.Migrations.Account
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "account");

            migrationBuilder.CreateTable(
                name: "account",
                schema: "account",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    claimed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_active_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    recovery_code_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                    recovery_code_created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    username = table.Column<string>(type: "text", nullable: false),
                    normalized_username = table.Column<string>(type: "text", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account", x => x.id);
                    table.CheckConstraint("ck_account_kind", "kind IN (1, 2)");
                    table.CheckConstraint("ck_account_status", "status IN (1, 2)");
                });

            migrationBuilder.CreateTable(
                name: "session",
                schema: "account",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    account_id = table.Column<long>(type: "bigint", nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_session", x => x.id);
                    table.ForeignKey(
                        name: "fk_session_account_account_id",
                        column: x => x.account_id,
                        principalSchema: "account",
                        principalTable: "account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_account_normalized_username",
                schema: "account",
                table: "account",
                column: "normalized_username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_account_recovery_code_hash",
                schema: "account",
                table: "account",
                column: "recovery_code_hash",
                unique: true,
                filter: "recovery_code_hash IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_session_account_id",
                schema: "account",
                table: "session",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_session_token_hash",
                schema: "account",
                table: "session",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "session",
                schema: "account");

            migrationBuilder.DropTable(
                name: "account",
                schema: "account");
        }
    }
}
