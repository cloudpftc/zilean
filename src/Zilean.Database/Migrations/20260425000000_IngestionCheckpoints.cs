using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zilean.Database.Migrations;

/// <inheritdoc />
public partial class IngestionCheckpoints : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "IngestionCheckpoints",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Source = table.Column<string>(type: "text", nullable: false),
                LastProcessedInfohash = table.Column<string>(type: "text", nullable: true),
                TotalProcessed = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                Status = table.Column<string>(type: "text", nullable: false, defaultValue: "active"),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IngestionCheckpoints", x => x.Id);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "IngestionCheckpoints");
    }
}
