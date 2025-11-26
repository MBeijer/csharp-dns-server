#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace Dns.Cli.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    account = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    password = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    salt = table.Column<string>(type: "TEXT", maxLength: 3, nullable: true),
                    activated = table.Column<bool>(type: "INTEGER", nullable: false),
                    adminlevel = table.Column<byte>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_users_account",
                table: "users",
                column: "account",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "users");
        }
    }
}