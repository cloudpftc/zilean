using System.Xml.Linq;
using Zilean.Database.Extensions;
using Zilean.Shared.Features.Ingestion;

namespace Zilean.ApiService.Features.Sync;

public class ProwlarrSyncJob(
    ILogger<ProwlarrSyncJob> logger,
    ZileanDbContext dbContext,
    IHttpClientFactory httpClientFactory,
    ZileanConfiguration configuration) : IInvocable, ICancellableInvocable
{
    public CancellationToken CancellationToken { get; set; }
    private const int PageSize = 100;

    public async Task Invoke()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            logger.LogInformation("[ProwlarrSync] Starting Prowlarr sync job");

            if (!configuration.Prowlarr.Enabled)
            {
                logger.LogInformation("[ProwlarrSync] Prowlarr is disabled, skipping");
                return;
            }

            var enabledIndexers = configuration.Prowlarr.Indexers
                .Where(i => i.Enabled && !string.IsNullOrWhiteSpace(i.SourceName))
                .ToList();

            if (enabledIndexers.Count == 0)
            {
                logger.LogInformation("[ProwlarrSync] No enabled Prowlarr indexers found, skipping");
                return;
            }

            var totalTorrents = 0;
            var indexerCount = 0;

            foreach (var indexer in enabledIndexers)
            {
                try
                {
                    var count = await SyncIndexerAsync(indexer);
                    totalTorrents += count;
                    indexerCount++;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[ProwlarrSync] Failed to sync indexer '{SourceName}'", indexer.SourceName);
                    await SaveIndexerErrorAsync(indexer.SourceName, ex.Message);
                }
            }

            sw.Stop();
            logger.LogInformation(
                "[ProwlarrSync] Complete: {Total} torrents from {IndexerCount} indexers in {ElapsedMs}ms",
                totalTorrents, indexerCount, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "[ProwlarrSync] Prowlarr sync job failed");
            throw;
        }
    }

    public async Task<int> SyncSingleIndexerAsync(string sourceName)
    {
        var indexer = configuration.Prowlarr.Indexers
            .FirstOrDefault(i => i.SourceName.Equals(sourceName, StringComparison.OrdinalIgnoreCase));

        if (indexer == null)
        {
            throw new InvalidOperationException($"Indexer '{sourceName}' not found in configuration.");
        }

        logger.LogInformation("[ProwlarrSync] Manually triggering sync for indexer '{SourceName}'", sourceName);
        return await SyncIndexerAsync(indexer);
    }

    private async Task<int> SyncIndexerAsync(ProwlarrIndexer indexer)
    {
        var stats = await GetOrCreateStatsAsync(indexer.SourceName);
        var lastSyncAt = stats.LastSyncAt;

        var httpClient = httpClientFactory.CreateClient("Prowlarr");
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Zilean/2.0");

        var offset = 0;
        var totalProcessed = 0;
        var page = 0;
        DateTime? maxPubDate = null;

        while (!CancellationToken.IsCancellationRequested)
        {
            page++;
            var url = $"{configuration.Prowlarr.BaseUrl.TrimEnd('/')}/{indexer.IndexerId}/api"
                + $"?t=search&apikey={configuration.Prowlarr.ApiKey}"
                + $"&cat={indexer.Categories}&extended=1"
                + $"&offset={offset}&limit={PageSize}";

            var response = await httpClient.GetAsync(url, CancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(CancellationToken);
            var torrents = ParseRssFeed(content, indexer.SourceName, lastSyncAt);

            if (torrents.Count == 0)
            {
                logger.LogInformation("[ProwlarrSync] {SourceName} page {Page}: 0 torrents (end of feed)", indexer.SourceName, page);
                break;
            }

            var allOlderOrEqual = torrents.All(t => t.IngestedAt <= lastSyncAt);
            if (allOlderOrEqual)
            {
                break;
            }

            var newItems = torrents.Where(t => t.IngestedAt > lastSyncAt).ToList();
            if (newItems.Count > 0)
            {
                await dbContext.UpsertTorrentsAsync(newItems, indexer.SourceName, CancellationToken);
                totalProcessed += newItems.Count;

                var pageMaxDate = newItems.Max(t => t.IngestedAt);
                if (maxPubDate == null || pageMaxDate > maxPubDate)
                {
                    maxPubDate = pageMaxDate;
                }

                logger.LogInformation("[ProwlarrSync] {SourceName} page {Page}: {Count} torrents", indexer.SourceName, page, newItems.Count);
            }
            else
            {
                logger.LogInformation("[ProwlarrSync] {SourceName} page {Page}: 0 new torrents (all at or before checkpoint)", indexer.SourceName, page);
            }

            offset += PageSize;
        }

        stats.LastSyncAt = maxPubDate ?? stats.LastSyncAt;
        stats.TorrentCount += totalProcessed;
        stats.LastError = null;
        await dbContext.SaveChangesAsync(CancellationToken);

        return totalProcessed;
    }

    private List<TorrentInfo> ParseRssFeed(string xmlContent, string sourceName, DateTime lastSyncAt)
    {
        var torrents = new List<TorrentInfo>();

        try
        {
            var doc = XDocument.Parse(xmlContent);
            var items = doc.Descendants("item").ToList();

            foreach (var item in items)
            {
                var infoHash = GetTorznabAttr(item, "infohash");
                if (string.IsNullOrWhiteSpace(infoHash))
                {
                    continue;
                }

                var title = item.Element("title")?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                var size = item.Element("size")?.Value;
                var pubDateStr = item.Element("pubDate")?.Value;

                DateTime ingestedAt;
                if (!string.IsNullOrWhiteSpace(pubDateStr) && DateTime.TryParse(pubDateStr, out var parsedDate))
                {
                    ingestedAt = parsedDate.Kind == DateTimeKind.Unspecified
                        ? DateTime.SpecifyKind(parsedDate, DateTimeKind.Utc)
                        : parsedDate.ToUniversalTime();
                }
                else
                {
                    ingestedAt = DateTime.UtcNow;
                }

                var torrent = new TorrentInfo
                {
                    InfoHash = infoHash.ToLowerInvariant(),
                    RawTitle = title,
                    ParsedTitle = title,
                    NormalizedTitle = title.ToLowerInvariant(),
                    Size = size,
                    IngestedAt = ingestedAt,
                    Source = sourceName,
                    Torrent = true,
                };

                torrents.Add(torrent);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ProwlarrSync] Failed to parse RSS feed for {SourceName}", sourceName);
        }

        return torrents;
    }

    private static string? GetTorznabAttr(XElement item, string attrName)
    {
        return item.Elements()
            .Where(e => e.Name.LocalName == "attr" && e.Name.NamespaceName.Contains("torznab"))
            .FirstOrDefault(e => e.Attribute("name")?.Value == attrName)
            ?.Attribute("value")
            ?.Value;
    }

    private async Task<TorrentSourceStats> GetOrCreateStatsAsync(string sourceName)
    {
        var stats = await dbContext.TorrentSourceStats
            .FirstOrDefaultAsync(s => s.Source == sourceName, CancellationToken);

        if (stats == null)
        {
            stats = new TorrentSourceStats
            {
                Source = sourceName,
                LastSyncAt = DateTime.MinValue,
                TorrentCount = 0,
            };
            dbContext.TorrentSourceStats.Add(stats);
            await dbContext.SaveChangesAsync(CancellationToken);
        }

        return stats;
    }

    private async Task SaveIndexerErrorAsync(string sourceName, string errorMessage)
    {
        try
        {
            var stats = await GetOrCreateStatsAsync(sourceName);
            stats.LastError = errorMessage;
            await dbContext.SaveChangesAsync(CancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ProwlarrSync] Failed to save error stats for {SourceName}", sourceName);
        }
    }
}
