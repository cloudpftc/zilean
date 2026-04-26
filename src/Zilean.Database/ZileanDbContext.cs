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
        modelBuilder.ApplyConfiguration(new IngestionCheckpointConfiguration());
        modelBuilder.ApplyConfiguration(new TorrentSourceStatsConfiguration());
        modelBuilder.ApplyConfiguration(new IngestionQueueConfiguration());
        modelBuilder.ApplyConfiguration(new QueryAuditConfiguration());
        modelBuilder.ApplyConfiguration(new FileAuditLogConfiguration());
    }

    public DbSet<TorrentInfo> Torrents => Set<TorrentInfo>();
    public DbSet<ImdbFile> ImdbFiles => Set<ImdbFile>();
    public DbSet<ParsedPages> ParsedPages => Set<ParsedPages>();
    public DbSet<ImportMetadata> ImportMetadata => Set<ImportMetadata>();
    public DbSet<BlacklistedItem> BlacklistedItems => Set<BlacklistedItem>();
    public DbSet<IngestionCheckpoint> IngestionCheckpoints => Set<IngestionCheckpoint>();
    public DbSet<IngestionQueue> IngestionQueues => Set<IngestionQueue>();
    public DbSet<QueryAudit> QueryAudits => Set<QueryAudit>();
    public DbSet<FileAuditLog> FileAuditLogs => Set<FileAuditLog>();
    public DbSet<TorrentSourceStats> TorrentSourceStats => Set<TorrentSourceStats>();
}
