using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zilean.Database.Migrations;

public partial class AddMissingGinIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop indexes if they exist (they may have been created by earlier migrations)
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_cleaned_parsed_title_trgm");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_seasons_gin");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_episodes_gin");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_languages_gin");

            migrationBuilder.CreateIndex(
                name: "idx_cleaned_parsed_title_trgm",
                table: "Torrents",
                columns: new[] { "CleanedParsedTitle" })
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" })
                .Annotation("Relational:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "idx_seasons_gin",
                table: "Torrents",
                columns: new[] { "Seasons" })
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Relational:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "idx_episodes_gin",
                table: "Torrents",
                columns: new[] { "Episodes" })
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Relational:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "idx_languages_gin",
                table: "Torrents",
                columns: new[] { "Languages" })
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Relational:IndexMethod", "GIN");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_cleaned_parsed_title_trgm",
                table: "Torrents");

            migrationBuilder.DropIndex(
                name: "idx_seasons_gin",
                table: "Torrents");

            migrationBuilder.DropIndex(
                name: "idx_episodes_gin",
                table: "Torrents");

            migrationBuilder.DropIndex(
                name: "idx_languages_gin",
                table: "Torrents");
        }
    }