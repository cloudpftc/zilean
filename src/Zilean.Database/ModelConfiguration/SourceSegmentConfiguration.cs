namespace Zilean.Database.ModelConfiguration;

public class SourceSegmentConfiguration : IEntityTypeConfiguration<SourceSegment>
{
    public void Configure(EntityTypeBuilder<SourceSegment> builder)
    {
        builder.ToTable("source_segments");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.SourceType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.SegmentIdentifier)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasMaxLength(20);

        builder.HasIndex(x => x.SourceType);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.LastSuccessful);
        builder.HasIndex(x => x.StaleAfter);
        
        // Composite index for finding stale segments by source
        builder.HasIndex(x => new { x.SourceType, x.Status, x.StaleAfter });
    }
}
