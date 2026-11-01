using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShapeLand.Data.Migrations.Region
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "region");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:CollationDefinition:public.case_insensitive", "und-u-ks-level2,und-u-ks-level2,icu,False");

            migrationBuilder.CreateTable(
                name: "character",
                schema: "region",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    account_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false, collation: "case_insensitive"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    shape = table.Column<int>(type: "integer", nullable: false),
                    color = table.Column<int>(type: "integer", nullable: false),
                    eye_color = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_character", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "character_owner",
                schema: "region",
                columns: table => new
                {
                    character_id = table.Column<long>(type: "bigint", nullable: false),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    released_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_character_owner", x => x.character_id);
                    table.ForeignKey(
                        name: "fk_character_owner_character_character_id",
                        column: x => x.character_id,
                        principalSchema: "region",
                        principalTable: "character",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "character_status",
                schema: "region",
                columns: table => new
                {
                    character_id = table.Column<long>(type: "bigint", nullable: false),
                    zone = table.Column<int>(type: "integer", nullable: false),
                    x = table.Column<float>(type: "real", nullable: false),
                    y = table.Column<float>(type: "real", nullable: false),
                    z = table.Column<float>(type: "real", nullable: false),
                    facing = table.Column<float>(type: "real", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_character_status", x => x.character_id);
                    table.ForeignKey(
                        name: "fk_character_status_character_character_id",
                        column: x => x.character_id,
                        principalSchema: "region",
                        principalTable: "character",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_character_account_id",
                schema: "region",
                table: "character",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_character_name",
                schema: "region",
                table: "character",
                column: "name",
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "character_owner",
                schema: "region");

            migrationBuilder.DropTable(
                name: "character_status",
                schema: "region");

            migrationBuilder.DropTable(
                name: "character",
                schema: "region");
        }
    }
}
