using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Sanctuary.Database;

#nullable disable

namespace Sanctuary.Database.Sqlite.Migrations;

// Adds CharacterQuests.GoalCount (in-progress count for a Collect goal). Attributes are declared here (rather
// than a .Designer.cs) so Database.Migrate() discovers and applies it; a fresh DB gets the column from this,
// an existing one via the ALTER TABLE below applied on startup.
[DbContext(typeof(DatabaseContext))]
[Migration("20260709120000_AddQuestGoalCount")]
public partial class AddQuestGoalCount : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "GoalCount",
            table: "CharacterQuests",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "GoalCount",
            table: "CharacterQuests");
    }
}
