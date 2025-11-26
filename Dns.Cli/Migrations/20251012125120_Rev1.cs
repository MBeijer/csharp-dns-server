#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace Dns.Cli.Migrations
{
    /// <inheritdoc />
    public partial class Rev1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "account",
                table: "users",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "zones",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    suffix = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    serial = table.Column<uint>(type: "INTEGER", nullable: false),
                    enabled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_zones", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "zone_records",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    host = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    @class = table.Column<ushort>(name: "class", type: "INTEGER", nullable: true),
                    type = table.Column<ushort>(type: "INTEGER", nullable: true),
                    data = table.Column<string>(type: "TEXT", nullable: true),
                    zone = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_zone_records", x => x.id);
                    table.ForeignKey(
                        name: "FK_zone_records_zones_zone",
                        column: x => x.zone,
                        principalTable: "zones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_zone_records_host",
                table: "zone_records",
                column: "host");

            migrationBuilder.CreateIndex(
                name: "IX_zone_records_zone",
                table: "zone_records",
                column: "zone");

            migrationBuilder.CreateIndex(
                name: "IX_zones_serial",
                table: "zones",
                column: "serial",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "zone_records");

            migrationBuilder.DropTable(
                name: "zones");

            migrationBuilder.AlterColumn<string>(
                name: "account",
                table: "users",
                type: "TEXT",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 100);
        }
    }
}
