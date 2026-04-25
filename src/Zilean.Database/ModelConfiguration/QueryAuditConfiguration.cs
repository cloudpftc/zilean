namespace Zilean.Database.ModelConfiguration;

public class QueryAuditConfiguration : IEntityTypeConfiguration<QueryAudit>
{
    public void Configure(EntityTypeBuilder<QueryAudit> builder)
    {
        builder.ToTable("query_audits");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.RawQuery)
            .IsRequired();

        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => x.ContentType);
        builder.HasIndex(x => x.CorrelationId);
        
        // Index for finding queries that triggered refreshes
        builder.HasIndex(x => x.TriggeredRefresh);
    }
}
