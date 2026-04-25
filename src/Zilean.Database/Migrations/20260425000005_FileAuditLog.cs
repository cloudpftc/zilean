using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zilean.Database.Migrations;

/// <inheritdoc />
public partial class FileAuditLog05 : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "FileAuditLogs",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Operation = table.Column<string>(type: "text", nullable: false),
                FilePath = table.Column<string>(type: "text", nullable: true),
                Status = table.Column<string>(type: "text", nullable: false),
                Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                DetailsJson = table.Column<string>(type: "text", nullable: true),
                DurationMs = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FileAuditLogs", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_FileAuditLogs_Timestamp",
            table: "FileAuditLogs",
            column: "Timestamp");

        migrationBuilder.CreateIndex(
            name: "IX_FileAuditLogs_Operation",
            table: "FileAuditLogs",
            column: "Operation");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "FileAuditLogs");
    }
}
