namespace Zilean.Database;

public class ZileanDbContext : DbContext
{
    public ZileanDbContext()
    {
    }

    public ZileanDbContext(DbContextOptions<ZileanDbContext> options): base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=zilean;Username=postgres;Password=postgres;CommandTimeout=0;Include Error Detail=true;");
        }
        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new TorrentInfoConfiguration());
        modelBuilder.ApplyConfiguration(new ImdbFileConfiguration());
        modelBuilder.ApplyConfiguration(new ParsedPagesConfiguration());
        modelBuilder.ApplyConfiguration(new ImportMetadataConfiguration());
        modelBuilder.ApplyConfiguration(new BlacklistedItemConfiguration());
        
        // Aggressive persistence entities
        modelBuilder.ApplyConfiguration(new SourceSyncRunConfiguration());
        modelBuilder.ApplyConfiguration(new SourceSegmentConfiguration());
        modelBuilder.ApplyConfiguration(new IngestionCheckpointConfiguration());
        modelBuilder.ApplyConfiguration(new RefreshJobConfiguration());
        modelBuilder.ApplyConfiguration(new QueryAuditConfiguration());
        modelBuilder.ApplyConfiguration(new QueryMissConfiguration());
        modelBuilder.ApplyConfiguration(new TitleAliasConfiguration());
        modelBuilder.ApplyConfiguration(new SearchDocumentConfiguration());
    }

    public DbSet<TorrentInfo> Torrents => Set<TorrentInfo>();
    public DbSet<ImdbFile> ImdbFiles => Set<ImdbFile>();
    public DbSet<ParsedPages> ParsedPages => Set<ParsedPages>();
    public DbSet<ImportMetadata> ImportMetadata => Set<ImportMetadata>();
    public DbSet<BlacklistedItem> BlacklistedItems => Set<BlacklistedItem>();
    
    // Aggressive persistence DbSets
    public DbSet<SourceSyncRun> SourceSyncRuns => Set<SourceSyncRun>();
    public DbSet<SourceSegment> SourceSegments => Set<SourceSegment>();
    public DbSet<IngestionCheckpoint> IngestionCheckpoints => Set<IngestionCheckpoint>();
    public DbSet<RefreshJob> RefreshJobs => Set<RefreshJob>();
    public DbSet<QueryAudit> QueryAudits => Set<QueryAudit>();
    public DbSet<QueryMiss> QueryMisses => Set<QueryMiss>();
    public DbSet<TitleAlias> TitleAliases => Set<TitleAlias>();
    public DbSet<SearchDocument> SearchDocuments => Set<SearchDocument>();
}
