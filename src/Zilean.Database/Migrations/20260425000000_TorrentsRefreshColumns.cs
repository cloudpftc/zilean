using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zilean.Database.Migrations;

/// <inheritdoc />
public partial class TorrentsRefreshColumns : Migration
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
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "RefreshPending",
            table: "Torrents");

        migrationBuilder.DropColumn(
            name: "MissCount",
            table: "Torrents");

        migrationBuilder.DropColumn(
            name: "LastRefreshedAt",
            table: "Torrents");
    }
}
