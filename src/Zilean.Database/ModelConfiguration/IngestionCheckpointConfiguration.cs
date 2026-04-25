namespace Zilean.Database.ModelConfiguration;

public class IngestionCheckpointConfiguration : IEntityTypeConfiguration<IngestionCheckpoint>
{
    public void Configure(EntityTypeBuilder<IngestionCheckpoint> builder)
    {
        builder.ToTable("ingestion_checkpoints");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.SourceType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.CheckpointKey)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.CheckpointValue)
            .IsRequired();

        builder.HasIndex(x => x.SourceType);
        builder.HasIndex(x => x.CheckpointKey);
        
        // Composite index for fast lookup by source and key
        builder.HasIndex(x => new { x.SourceType, x.CheckpointKey });
    }
}
