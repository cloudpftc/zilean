namespace Zilean.Database.ModelConfiguration;

public class SearchDocumentConfiguration : IEntityTypeConfiguration<SearchDocument>
{
    public void Configure(EntityTypeBuilder<SearchDocument> builder)
    {
        builder.ToTable("search_documents");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.SourceId)
            .IsRequired();

        builder.Property(x => x.SourceType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.RawTitle)
            .IsRequired();

        builder.Property(x => x.CanonicalTitle)
            .IsRequired();

        builder.HasIndex(x => x.SourceType);
        builder.HasIndex(x => x.ContentType);
        builder.HasIndex(x => x.CanonicalTitle);
        builder.HasIndex(x => x.NormalizedTitle);
        builder.HasIndex(x => x.ImdbId);
        builder.HasIndex(x => x.IsStale);
        builder.HasIndex(x => x.LastRefreshedAt);
        
        // Composite index for active search documents by type
        builder.HasIndex(x => new { x.ContentType, x.IsStale, x.QualityScore });
    }
}
