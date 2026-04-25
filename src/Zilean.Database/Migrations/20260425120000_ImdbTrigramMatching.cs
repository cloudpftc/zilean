using Microsoft.EntityFrameworkCore.Migrations;
using Zilean.Database.Functions;

#nullable disable

namespace Zilean.Database.Migrations;

/// <inheritdoc />
public partial class ImdbTrigramMatching : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
        migrationBuilder.Sql(ImdbTrigramMatching.CreateMatchTitleFunction);
        migrationBuilder.Sql(ImdbTrigramMatching.CreateBatchFindFunction);
        migrationBuilder.Sql(ImdbTrigramMatching.CreateBatchUpdateFunction);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(ImdbTrigramMatching.RemoveBatchUpdateFunction);
        migrationBuilder.Sql(ImdbTrigramMatching.RemoveBatchFindFunction);
        migrationBuilder.Sql(ImdbTrigramMatching.RemoveMatchTitleFunction);
    }
}
