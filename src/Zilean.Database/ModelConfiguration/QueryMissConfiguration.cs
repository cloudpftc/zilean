namespace Zilean.Database.ModelConfiguration;

public class QueryMissConfiguration : IEntityTypeConfiguration<QueryMiss>
{
    public void Configure(EntityTypeBuilder<QueryMiss> builder)
    {
        builder.ToTable("query_misses");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.NormalizedQueryFingerprint)
            .IsRequired();

        builder.HasIndex(x => x.NormalizedQueryFingerprint);
        builder.HasIndex(x => x.LastSeen);
        builder.HasIndex(x => x.RefreshTriggered);
        
        // Index for finding misses that need refresh
        builder.HasIndex(x => new { x.RefreshTriggered, x.LastSeen });
    }
}
