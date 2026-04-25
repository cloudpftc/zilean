namespace Zilean.Database.ModelConfiguration;

public class IngestionCheckpointConfiguration : IEntityTypeConfiguration<IngestionCheckpoint>
{
    public void Configure(EntityTypeBuilder<IngestionCheckpoint> builder)
    {
        builder.ToTable("IngestionCheckpoints");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasColumnType("integer")
            .ValueGeneratedOnAdd()
            .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

        builder.Property(t => t.Source)
            .IsRequired()
            .HasColumnType("text")
            .HasMaxLength(50)
            .HasAnnotation("Relational:JsonPropertyName", "source");

        builder.Property(t => t.LastProcessed)
            .HasColumnName("LastProcessedInfohash")
            .HasColumnType("text")
            .HasMaxLength(40)
            .HasAnnotation("Relational:JsonPropertyName", "last_processed");

        builder.Property(t => t.ItemsProcessed)
            .HasColumnName("TotalProcessed")
            .HasColumnType("integer")
            .IsRequired()
            .HasDefaultValue(0)
            .HasAnnotation("Relational:JsonPropertyName", "items_processed");

        builder.Property(t => t.Status)
            .IsRequired()
            .HasColumnType("text")
            .HasMaxLength(20)
            .HasDefaultValue("active")
            .HasAnnotation("Relational:JsonPropertyName", "status");

        builder.Property(t => t.Timestamp)
            .HasColumnName("CreatedAt")
            .HasColumnType("timestamp with time zone")
            .IsRequired()
            .HasDefaultValueSql("now() at time zone 'utc'")
            .HasAnnotation("Relational:JsonPropertyName", "timestamp");

        builder.HasIndex(t => t.Source)
            .HasDatabaseName("IX_IngestionCheckpoints_Source");

        builder.HasIndex(t => t.Status)
            .HasDatabaseName("IX_IngestionCheckpoints_Status");
    }
}
