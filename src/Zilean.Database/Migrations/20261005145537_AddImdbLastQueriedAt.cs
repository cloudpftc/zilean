using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zilean.Database.Migrations;

public partial class AddImdbLastQueriedAt : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "LastQueriedAt",
            table: "ImdbFiles",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_ImdbFiles_LastQueriedAt",
            table: "ImdbFiles",
            column: "LastQueriedAt");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ImdbFiles_LastQueriedAt",
            table: "ImdbFiles");

        migrationBuilder.DropColumn(
            name: "LastQueriedAt",
            table: "ImdbFiles");
    }
}
