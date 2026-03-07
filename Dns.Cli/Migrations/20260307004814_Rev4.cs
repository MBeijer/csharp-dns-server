using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dns.Cli.Migrations
{
    /// <inheritdoc />
    public partial class Rev4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "master_zone_id",
                table: "zones",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_zones_master_zone_id",
                table: "zones",
                column: "master_zone_id");

            migrationBuilder.AddForeignKey(
                name: "FK_zones_zones_master_zone_id",
                table: "zones",
                column: "master_zone_id",
                principalTable: "zones",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_zones_zones_master_zone_id",
                table: "zones");

            migrationBuilder.DropIndex(
                name: "IX_zones_master_zone_id",
                table: "zones");

            migrationBuilder.DropColumn(
                name: "master_zone_id",
                table: "zones");
        }
    }
}
