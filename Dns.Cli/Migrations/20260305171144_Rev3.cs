using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dns.Cli.Migrations
{
	/// <inheritdoc />
	public partial class Rev3 : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.DropColumn(
				name: "salt",
				table: "users");
		}

		/// <inheritdoc />
		protected override void Down(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.AddColumn<string>(
				name: "salt",
				table: "users",
				type: "TEXT",
				maxLength: 3,
				nullable: true);
		}
	}
}