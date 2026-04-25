using Microsoft.EntityFrameworkCore.Migrations;
using ImdbTrigramFunctions = Zilean.Database.Functions.ImdbTrigramMatching;

#nullable disable

namespace Zilean.Database.Migrations;

/// <inheritdoc />
public partial class ImdbTrigramMatching : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
        migrationBuilder.Sql(ImdbTrigramFunctions.CreateMatchTitleFunction);
        migrationBuilder.Sql(ImdbTrigramFunctions.CreateBatchFindFunction);
        migrationBuilder.Sql(ImdbTrigramFunctions.CreateBatchUpdateFunction);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(ImdbTrigramFunctions.RemoveBatchUpdateFunction);
        migrationBuilder.Sql(ImdbTrigramFunctions.RemoveBatchFindFunction);
        migrationBuilder.Sql(ImdbTrigramFunctions.RemoveMatchTitleFunction);
    }
}
