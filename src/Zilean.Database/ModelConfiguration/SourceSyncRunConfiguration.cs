namespace Zilean.Database.ModelConfiguration;

public class SourceSyncRunConfiguration : IEntityTypeConfiguration<SourceSyncRun>
{
    public void Configure(EntityTypeBuilder<SourceSyncRun> builder)
    {
        builder.ToTable("source_sync_runs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.SourceType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasMaxLength(20);

        builder.HasIndex(x => x.SourceType);
        builder.HasIndex(x => x.StartTime);
        builder.HasIndex(x => x.Status);
    }
}
