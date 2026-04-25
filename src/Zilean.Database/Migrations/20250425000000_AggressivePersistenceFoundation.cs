using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zilean.Database.Migrations;

/// <inheritdoc />
/// <summary>
/// Adds tables for aggressive Postgres-backed persistence:
/// - source_sync_runs: Track synchronization runs
/// - source_segments: Track individual pages/segments for incremental ingestion
/// - ingestion_checkpoints: Store checkpoints for resumable operations
/// - refresh_jobs: Durable queue for background hydration jobs
/// - query_audits: Search query telemetry
/// - query_misses: Track queries with no/low-confidence results
/// - title_aliases: Alternate title mappings for improved recall
/// - search_documents: Denormalized search-ready documents
/// </summary>
public partial class AggressivePersistenceFoundation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Source Sync Runs table
        migrationBuilder.CreateTable(
            name: "source_sync_runs",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                source_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                start_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                end_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                pages_processed = table.Column<int>(type: "integer", nullable: false),
                entries_processed = table.Column<int>(type: "integer", nullable: false),
                entries_added = table.Column<int>(type: "integer", nullable: false),
                entries_updated = table.Column<int>(type: "integer", nullable: false),
                errors = table.Column<int>(type: "integer", nullable: false),
                error_summary = table.Column<string>(type: "text", nullable: true),
                retry_count = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_source_sync_runs", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_source_sync_runs_source_type",
            table: "source_sync_runs",
            column: "source_type");

        migrationBuilder.CreateIndex(
            name: "IX_source_sync_runs_start_time",
            table: "source_sync_runs",
            column: "start_time");

        migrationBuilder.CreateIndex(
            name: "IX_source_sync_runs_status",
            table: "source_sync_runs",
            column: "status");

        // Source Segments table
        migrationBuilder.CreateTable(
            name: "source_segments",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                source_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                segment_identifier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                last_attempted = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                last_successful = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                retry_count = table.Column<int>(type: "integer", nullable: false),
                error_summary = table.Column<string>(type: "text", nullable: true),
                checksum_or_version = table.Column<string>(type: "text", nullable: true),
                stale_after = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                entry_count = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_source_segments", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_source_segments_source_type",
            table: "source_segments",
            column: "source_type");

        migrationBuilder.CreateIndex(
            name: "IX_source_segments_status",
            table: "source_segments",
            column: "status");

        migrationBuilder.CreateIndex(
            name: "IX_source_segments_last_successful",
            table: "source_segments",
            column: "last_successful");

        migrationBuilder.CreateIndex(
            name: "IX_source_segments_stale_after",
            table: "source_segments",
            column: "stale_after");

        migrationBuilder.CreateIndex(
            name: "IX_source_segments_source_type_status_stale_after",
            table: "source_segments",
            columns: new[] { "source_type", "status", "stale_after" });

        // Ingestion Checkpoints table
        migrationBuilder.CreateTable(
            name: "ingestion_checkpoints",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                source_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                checkpoint_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                checkpoint_value = table.Column<string>(type: "text", nullable: false),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                metadata = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ingestion_checkpoints", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ingestion_checkpoints_source_type",
            table: "ingestion_checkpoints",
            column: "source_type");

        migrationBuilder.CreateIndex(
            name: "IX_ingestion_checkpoints_checkpoint_key",
            table: "ingestion_checkpoints",
            column: "checkpoint_key");

        migrationBuilder.CreateIndex(
            name: "IX_ingestion_checkpoints_source_type_checkpoint_key",
            table: "ingestion_checkpoints",
            columns: new[] { "source_type", "checkpoint_key" });

        // Refresh Jobs table
        migrationBuilder.CreateTable(
            name: "refresh_jobs",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                trigger_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                query_fingerprint = table.Column<string>(type: "text", nullable: true),
                target_scope = table.Column<string>(type: "text", nullable: true),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                dedupe_key = table.Column<string>(type: "text", nullable: true),
                scheduled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                retry_count = table.Column<int>(type: "integer", nullable: false),
                error_summary = table.Column<string>(type: "text", nullable: true),
                entries_added = table.Column<int>(type: "integer", nullable: false),
                entries_updated = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_refresh_jobs", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_refresh_jobs_status",
            table: "refresh_jobs",
            column: "status");

        migrationBuilder.CreateIndex(
            name: "IX_refresh_jobs_trigger_type",
            table: "refresh_jobs",
            column: "trigger_type");

        migrationBuilder.CreateIndex(
            name: "IX_refresh_jobs_scheduled_at",
            table: "refresh_jobs",
            column: "scheduled_at");

        migrationBuilder.CreateIndex(
            name: "IX_refresh_jobs_dedupe_key",
            table: "refresh_jobs",
            column: "dedupe_key");

        migrationBuilder.CreateIndex(
            name: "IX_refresh_jobs_status_scheduled_at",
            table: "refresh_jobs",
            columns: new[] { "status", "scheduled_at" });

        // Query Audits table
        migrationBuilder.CreateTable(
            name: "query_audits",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                raw_query = table.Column<string>(type: "text", nullable: false),
                normalized_query = table.Column<string>(type: "text", nullable: true),
                content_type = table.Column<string>(type: "text", nullable: true),
                season = table.Column<int>(type: "integer", nullable: true),
                episode = table.Column<int>(type: "integer", nullable: true),
                absolute_episode = table.Column<int>(type: "integer", nullable: true),
                candidate_count = table.Column<int>(type: "integer", nullable: false),
                returned_count = table.Column<int>(type: "integer", nullable: false),
                top_confidence = table.Column<double>(type: "double precision", nullable: true),
                result_summary = table.Column<string>(type: "text", nullable: true),
                triggered_refresh = table.Column<bool>(type: "boolean", nullable: false),
                correlation_id = table.Column<string>(type: "text", nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                elapsed_ms = table.Column<TimeSpan>(type: "interval", nullable: true),
                search_strategy = table.Column<string>(type: "text", nullable: true),
                staleness_info = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_query_audits", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_query_audits_created_at",
            table: "query_audits",
            column: "created_at");

        migrationBuilder.CreateIndex(
            name: "IX_query_audits_content_type",
            table: "query_audits",
            column: "content_type");

        migrationBuilder.CreateIndex(
            name: "IX_query_audits_correlation_id",
            table: "query_audits",
            column: "correlation_id");

        migrationBuilder.CreateIndex(
            name: "IX_query_audits_triggered_refresh",
            table: "query_audits",
            column: "triggered_refresh");

        // Query Misses table
        migrationBuilder.CreateTable(
            name: "query_misses",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                normalized_query_fingerprint = table.Column<string>(type: "text", nullable: false),
                raw_query = table.Column<string>(type: "text", nullable: false),
                content_hints = table.Column<string>(type: "text", nullable: true),
                miss_count = table.Column<int>(type: "integer", nullable: false),
                first_seen = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                last_seen = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                refresh_triggered = table.Column<bool>(type: "boolean", nullable: false),
                triggered_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                refresh_completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                resolution_notes = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_query_misses", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_query_misses_normalized_query_fingerprint",
            table: "query_misses",
            column: "normalized_query_fingerprint");

        migrationBuilder.CreateIndex(
            name: "IX_query_misses_last_seen",
            table: "query_misses",
            column: "last_seen");

        migrationBuilder.CreateIndex(
            name: "IX_query_misses_refresh_triggered",
            table: "query_misses",
            column: "refresh_triggered");

        migrationBuilder.CreateIndex(
            name: "IX_query_misses_refresh_triggered_last_seen",
            table: "query_misses",
            columns: new[] { "refresh_triggered", "last_seen" });

        // Title Aliases table
        migrationBuilder.CreateTable(
            name: "title_aliases",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                raw_title = table.Column<string>(type: "text", nullable: false),
                canonical_title = table.Column<string>(type: "text", nullable: false),
                alias_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                language_or_script = table.Column<string>(type: "text", nullable: true),
                normalized_tokens = table.Column<string>(type: "text", nullable: true),
                confidence = table.Column<double>(type: "double precision", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                last_used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_title_aliases", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_title_aliases_canonical_title",
            table: "title_aliases",
            column: "canonical_title");

        migrationBuilder.CreateIndex(
            name: "IX_title_aliases_raw_title",
            table: "title_aliases",
            column: "raw_title");

        migrationBuilder.CreateIndex(
            name: "IX_title_aliases_alias_type",
            table: "title_aliases",
            column: "alias_type");

        migrationBuilder.CreateIndex(
            name: "IX_title_aliases_raw_title_alias_type",
            table: "title_aliases",
            columns: new[] { "raw_title", "alias_type" });

        // Search Documents table
        migrationBuilder.CreateTable(
            name: "search_documents",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                source_id = table.Column<string>(type: "text", nullable: false),
                source_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                raw_title = table.Column<string>(type: "text", nullable: false),
                canonical_title = table.Column<string>(type: "text", nullable: false),
                normalized_title = table.Column<string>(type: "text", nullable: true),
                cleaned_title = table.Column<string>(type: "text", nullable: true),
                alias_titles = table.Column<string[]>(type: "text[]", nullable: true),
                search_tokens = table.Column<string[]>(type: "text[]", nullable: true),
                content_type = table.Column<string>(type: "text", nullable: false),
                year = table.Column<int>(type: "integer", nullable: true),
                season = table.Column<int>(type: "integer", nullable: true),
                episode = table.Column<int>(type: "integer", nullable: true),
                absolute_episode = table.Column<int>(type: "integer", nullable: true),
                release_group = table.Column<string>(type: "text", nullable: true),
                resolution = table.Column<string>(type: "text", nullable: true),
                source = table.Column<string>(type: "text", nullable: true),
                codec = table.Column<string>(type: "text", nullable: true),
                imdb_id = table.Column<string>(type: "text", nullable: true),
                tmdb_id = table.Column<string>(type: "text", nullable: true),
                ingested_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                last_refreshed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                is_stale = table.Column<bool>(type: "boolean", nullable: false),
                quality_score = table.Column<double>(type: "double precision", nullable: false),
                match_count = table.Column<int>(type: "integer", nullable: false),
                last_matched_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_search_documents", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_search_documents_source_type",
            table: "search_documents",
            column: "source_type");

        migrationBuilder.CreateIndex(
            name: "IX_search_documents_content_type",
            table: "search_documents",
            column: "content_type");

        migrationBuilder.CreateIndex(
            name: "IX_search_documents_canonical_title",
            table: "search_documents",
            column: "canonical_title");

        migrationBuilder.CreateIndex(
            name: "IX_search_documents_normalized_title",
            table: "search_documents",
            column: "normalized_title");

        migrationBuilder.CreateIndex(
            name: "IX_search_documents_imdb_id",
            table: "search_documents",
            column: "imdb_id");

        migrationBuilder.CreateIndex(
            name: "IX_search_documents_is_stale",
            table: "search_documents",
            column: "is_stale");

        migrationBuilder.CreateIndex(
            name: "IX_search_documents_last_refreshed_at",
            table: "search_documents",
            column: "last_refreshed_at");

        migrationBuilder.CreateIndex(
            name: "IX_search_documents_content_type_is_stale_quality_score",
            table: "search_documents",
            columns: new[] { "content_type", "is_stale", "quality_score" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "search_documents");
        migrationBuilder.DropTable(name: "title_aliases");
        migrationBuilder.DropTable(name: "query_misses");
        migrationBuilder.DropTable(name: "query_audits");
        migrationBuilder.DropTable(name: "refresh_jobs");
        migrationBuilder.DropTable(name: "ingestion_checkpoints");
        migrationBuilder.DropTable(name: "source_segments");
        migrationBuilder.DropTable(name: "source_sync_runs");
    }
}
