#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace Dns.Cli.Migrations
{
    /// <inheritdoc />
    public partial class Rev2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_zones_serial",
                table: "zones");

            migrationBuilder.CreateIndex(
                name: "IX_zones_suffix",
                table: "zones",
                column: "suffix",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_zones_suffix",
                table: "zones");

            migrationBuilder.CreateIndex(
                name: "IX_zones_serial",
                table: "zones",
                column: "serial",
                unique: true);
        }
    }
}
