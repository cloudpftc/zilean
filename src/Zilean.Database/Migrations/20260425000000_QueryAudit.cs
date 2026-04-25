using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Zilean.Database.Migrations;

/// <inheritdoc />
public partial class QueryAudit : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "QueryAudits",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Query = table.Column<string>(type: "text", nullable: false),
                FiltersJson = table.Column<string>(type: "text", nullable: true),
                ResultCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                DurationMs = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                SimilarityThreshold = table.Column<double>(type: "double precision", nullable: true),
                Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                ClientIp = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_QueryAudits", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_QueryAudits_Timestamp",
            table: "QueryAudits",
            column: "Timestamp");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "QueryAudits");
    }
}
