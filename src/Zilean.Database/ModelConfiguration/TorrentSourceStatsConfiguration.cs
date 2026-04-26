namespace Zilean.Database.ModelConfiguration;

public class TorrentSourceStatsConfiguration : IEntityTypeConfiguration<TorrentSourceStats>
{
    public void Configure(EntityTypeBuilder<TorrentSourceStats> builder)
    {
        builder.ToTable("TorrentSourceStats");

        builder.HasKey(t => t.Source);

        builder.Property(t => t.Source)
            .IsRequired()
            .HasColumnType("text")
            .HasAnnotation("Relational:JsonPropertyName", "source");

        builder.Property(t => t.LastSyncAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now() at time zone 'utc'")
            .HasAnnotation("Relational:JsonPropertyName", "last_sync_at");

        builder.Property(t => t.TorrentCount)
            .IsRequired()
            .HasColumnType("bigint")
            .HasDefaultValue(0L)
            .HasAnnotation("Relational:JsonPropertyName", "torrent_count");

        builder.Property(t => t.LastError)
            .HasColumnType("text")
            .HasAnnotation("Relational:JsonPropertyName", "last_error");
    }
}
