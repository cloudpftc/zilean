using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
namespace Zilean.Database.ModelConfiguration;

public class IngestionQueueConfiguration : IEntityTypeConfiguration<IngestionQueue>
{
    public void Configure(EntityTypeBuilder<IngestionQueue> builder)
    {
        builder.ToTable("IngestionQueue");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasColumnType("integer")
            .ValueGeneratedOnAdd()
            .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

        builder.Property(t => t.InfoHash)
            .IsRequired()
            .HasColumnType("text")
            .HasMaxLength(40)
            .HasAnnotation("Relational:JsonPropertyName", "info_hash");

        builder.Property(t => t.Status)
            .IsRequired()
            .HasColumnType("text")
            .HasMaxLength(20)
            .HasDefaultValue("pending")
            .HasAnnotation("Relational:JsonPropertyName", "status");

        builder.Property(t => t.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired()
            .HasDefaultValueSql("now() at time zone 'utc'")
            .HasAnnotation("Relational:JsonPropertyName", "created_at");

        builder.Property(t => t.ProcessedAt)
            .HasColumnType("timestamp with time zone")
            .HasAnnotation("Relational:JsonPropertyName", "processed_at");

        builder.Property(t => t.ErrorMessage)
            .HasColumnType("text")
            .HasMaxLength(500)
            .HasAnnotation("Relational:JsonPropertyName", "error_message");

        builder.Property(t => t.RetryCount)
            .HasColumnType("integer")
            .HasDefaultValue(0)
            .HasAnnotation("Relational:JsonPropertyName", "retry_count");

        builder.HasIndex(t => t.InfoHash)
            .HasDatabaseName("IX_IngestionQueue_InfoHash");

        builder.HasIndex(t => t.Status)
            .HasDatabaseName("IX_IngestionQueue_Status");
    }
}
