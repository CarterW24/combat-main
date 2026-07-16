using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanctuary.Database.Sqlite.Migrations;

public partial class RemoveUserIsLocked : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "IsLocked",
            table: "Users");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsLocked",
            table: "Users",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);
    }
}
