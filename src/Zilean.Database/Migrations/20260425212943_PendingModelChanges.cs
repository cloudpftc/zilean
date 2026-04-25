using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Zilean.Database.Migrations;

#nullable disable

/// <inheritdoc />
public partial class PendingModelChanges : Migration
{
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastRefreshedAt",
                table: "Torrents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MissCount",
                table: "Torrents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "RefreshPending",
                table: "Torrents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "FileAuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Operation = table.Column<string>(type: "text", maxLength: 50, nullable: false),
                    FilePath = table.Column<string>(type: "text", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "text", maxLength: 20, nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    DetailsJson = table.Column<string>(type: "text", nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IngestionCheckpoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Source = table.Column<string>(type: "text", maxLength: 50, nullable: false),
                    LastProcessedInfohash = table.Column<string>(type: "text", maxLength: 40, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    Status = table.Column<string>(type: "text", maxLength: 20, nullable: false, defaultValue: "active"),
                    TotalProcessed = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngestionCheckpoints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IngestionQueue",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InfoHash = table.Column<string>(type: "text", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "text", maxLength: 20, nullable: false, defaultValue: "pending"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", maxLength: 500, nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngestionQueue", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QueryAudits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Query = table.Column<string>(type: "text", maxLength: 500, nullable: false),
                    FiltersJson = table.Column<string>(type: "text", nullable: true),
                    ResultCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    DurationMs = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    SimilarityThreshold = table.Column<double>(type: "double precision", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    ClientIp = table.Column<string>(type: "text", maxLength: 45, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueryAudits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FileAuditLogs_Operation",
                table: "FileAuditLogs",
                column: "Operation");

            migrationBuilder.CreateIndex(
                name: "IX_FileAuditLogs_Timestamp",
                table: "FileAuditLogs",
                column: "Timestamp",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_IngestionCheckpoints_Source",
                table: "IngestionCheckpoints",
                column: "Source");

            migrationBuilder.CreateIndex(
                name: "IX_IngestionCheckpoints_Status",
                table: "IngestionCheckpoints",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_IngestionQueue_InfoHash",
                table: "IngestionQueue",
                column: "InfoHash");

            migrationBuilder.CreateIndex(
                name: "IX_IngestionQueue_Status",
                table: "IngestionQueue",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_QueryAudits_Query",
                table: "QueryAudits",
                column: "Query");

            migrationBuilder.CreateIndex(
                name: "IX_QueryAudits_Timestamp",
                table: "QueryAudits",
                column: "Timestamp",
                descending: new bool[0]);
        }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
                name: "FileAuditLogs");

        migrationBuilder.DropTable(
                name: "IngestionCheckpoints");

        migrationBuilder.DropTable(
                name: "IngestionQueue");

        migrationBuilder.DropTable(
                name: "QueryAudits");

        migrationBuilder.DropColumn(
                name: "LastRefreshedAt",
                table: "Torrents");

        migrationBuilder.DropColumn(
                name: "MissCount",
                table: "Torrents");

        migrationBuilder.DropColumn(
                name: "RefreshPending",
                table: "Torrents");
    }
}
