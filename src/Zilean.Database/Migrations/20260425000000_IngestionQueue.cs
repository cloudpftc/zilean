using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zilean.Database.Migrations;

/// <inheritdoc />
public partial class IngestionQueue : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "IngestionQueue",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                InfoHash = table.Column<string>(type: "text", nullable: false),
                Source = table.Column<string>(type: "text", nullable: false),
                Status = table.Column<string>(type: "text", nullable: false, defaultValue: "pending"),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ErrorMessage = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IngestionQueue", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_IngestionQueue_InfoHash",
            table: "IngestionQueue",
            column: "InfoHash");

        migrationBuilder.CreateIndex(
            name: "IX_IngestionQueue_Status",
            table: "IngestionQueue",
            column: "Status");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "IngestionQueue");
    }
}
