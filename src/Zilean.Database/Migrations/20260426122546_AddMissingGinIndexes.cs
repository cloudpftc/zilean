using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zilean.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddMissingGinIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
}