using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zilean.Database.Migrations;

/// <inheritdoc />
public partial class AddSourceColumn : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Source",
            table: "Torrents",
            type: "text",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "TorrentSourceStats",
            columns: table => new
            {
                Source = table.Column<string>(type: "text", nullable: false),
                LastSyncAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                TorrentCount = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                LastError = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TorrentSourceStats", x => x.Source);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "TorrentSourceStats");

        migrationBuilder.DropColumn(
            name: "Source",
            table: "Torrents");
    }
}
