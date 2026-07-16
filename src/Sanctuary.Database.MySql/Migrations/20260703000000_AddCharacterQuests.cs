using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanctuary.Database.MySql.Migrations
{
    public partial class AddCharacterQuests : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CharacterQuests",
                columns: table => new
                {
                    QuestId = table.Column<int>(type: "int", nullable: false),
                    CharacterGuid = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Completed = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterQuests", x => new { x.QuestId, x.CharacterGuid });
                    table.ForeignKey(
                        name: "FK_CharacterQuests_Characters_CharacterGuid",
                        column: x => x.CharacterGuid,
                        principalTable: "Characters",
                        principalColumn: "Guid",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterQuests_CharacterGuid",
                table: "CharacterQuests",
                column: "CharacterGuid");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CharacterQuests");
        }
    }
}
