namespace Zilean.Database.ModelConfiguration;

public class RefreshJobConfiguration : IEntityTypeConfiguration<RefreshJob>
{
    public void Configure(EntityTypeBuilder<RefreshJob> builder)
    {
        builder.ToTable("refresh_jobs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TriggerType)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasMaxLength(20);

        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.TriggerType);
        builder.HasIndex(x => x.ScheduledAt);
        builder.HasIndex(x => x.DedupeKey);
        
        // Index for finding pending jobs
        builder.HasIndex(x => new { x.Status, x.ScheduledAt });
    }
}
