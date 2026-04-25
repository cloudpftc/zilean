using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
namespace Zilean.Database.ModelConfiguration;

public class QueryAuditConfiguration : IEntityTypeConfiguration<QueryAudit>
{
    public void Configure(EntityTypeBuilder<QueryAudit> builder)
    {
        builder.ToTable("QueryAudits");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasColumnType("integer")
            .ValueGeneratedOnAdd()
            .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

        builder.Property(t => t.Query)
            .IsRequired()
            .HasColumnType("text")
            .HasMaxLength(500)
            .HasAnnotation("Relational:JsonPropertyName", "query");

        builder.Property(t => t.FiltersJson)
            .HasColumnType("text")
            .HasAnnotation("Relational:JsonPropertyName", "filters_json");

        builder.Property(t => t.ResultCount)
            .HasColumnType("integer")
            .IsRequired()
            .HasDefaultValue(0)
            .HasAnnotation("Relational:JsonPropertyName", "result_count");

        builder.Property(t => t.DurationMs)
            .HasColumnType("integer")
            .IsRequired()
            .HasDefaultValue(0)
            .HasAnnotation("Relational:JsonPropertyName", "duration_ms");

        builder.Property(t => t.SimilarityThreshold)
            .HasColumnType("double precision")
            .HasAnnotation("Relational:JsonPropertyName", "similarity_threshold");

        builder.Property(t => t.Timestamp)
            .HasColumnType("timestamp with time zone")
            .IsRequired()
            .HasDefaultValueSql("now() at time zone 'utc'")
            .HasAnnotation("Relational:JsonPropertyName", "timestamp");

        builder.Property(t => t.ClientIp)
            .HasColumnType("text")
            .HasMaxLength(45)
            .HasAnnotation("Relational:JsonPropertyName", "client_ip");

        builder.HasIndex(t => t.Timestamp)
            .HasDatabaseName("IX_QueryAudits_Timestamp")
            .IsDescending();

        builder.HasIndex(t => t.Query)
            .HasDatabaseName("IX_QueryAudits_Query");
    }
}
