using System.Diagnostics;

namespace Zilean.Scraper.Features.Ingestion.Dmm;

/// <summary>
/// Keeps a local working copy of the DMM public hashlists repository up to date.
///
/// Previously this downloaded the repository as a zipball on every run. That repo
/// is now ~1.4 GB and is updated hourly, so a full re-download was both hugely
/// wasteful and guaranteed to hit GitHub's unauthenticated rate limit
/// (HTTP 429 Too Many Requests), which silently froze the whole DMM index.
///
/// We now keep a persistent shallow clone and fetch only the delta. Git over
/// HTTPS is not subject to the API/zipball rate limit, and an hourly fetch
/// transfers a few MB instead of 1.4 GB.
/// </summary>
public class DmmFileDownloader(ILogger<DmmFileDownloader> logger, ZileanConfiguration configuration)
{
    private static readonly IReadOnlyCollection<string> _filesToIgnore =
    [
        "index.html",
        "404.html",
        "dedupe.sh",
        "CNAME",
    ];

    public async Task<string> DownloadFileToTempPath(DmmLastImport? dmmLastImport, CancellationToken cancellationToken)
    {
        var repositoryDirectory = configuration.Dmm.RepositoryDirectory;
        var repositoryUrl = configuration.Dmm.RepositoryUrl;
        var branch = configuration.Dmm.RepositoryBranch;

        var isClone = Directory.Exists(Path.Combine(repositoryDirectory, ".git"));

        // Honour the re-download interval only when we already have a usable copy;
        // a missing clone always proceeds regardless of the interval.
        if (isClone
            && dmmLastImport is not null
            && DateTime.UtcNow - dmmLastImport.OccuredAt < TimeSpan.FromMinutes(configuration.Dmm.MinimumReDownloadIntervalMinutes))
        {
            logger.LogInformation(
                "DMM Hashlists update not required as last sync was less than the configured {Minutes} minutes re-download interval.",
                configuration.Dmm.MinimumReDownloadIntervalMinutes);
            return repositoryDirectory;
        }

        if (isClone)
        {
            logger.LogInformation("Updating DMM Hashlists repository in {RepositoryDirectory}", repositoryDirectory);
            await RunGitAsync($"-C \"{repositoryDirectory}\" fetch --depth 1 --no-tags origin {branch}", cancellationToken);
            await RunGitAsync($"-C \"{repositoryDirectory}\" reset --hard FETCH_HEAD", cancellationToken);
        }
        else
        {
            logger.LogInformation("Cloning DMM Hashlists repository to {RepositoryDirectory}", repositoryDirectory);

            if (Directory.Exists(repositoryDirectory))
            {
                Directory.Delete(repositoryDirectory, true);
            }

            var parent = Path.GetDirectoryName(repositoryDirectory);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            await RunGitAsync($"clone --depth 1 --no-tags --branch {branch} \"{repositoryUrl}\" \"{repositoryDirectory}\"", cancellationToken);
        }

        foreach (var file in _filesToIgnore)
        {
            CleanRepoExtras(repositoryDirectory, file);
        }

        logger.LogInformation("DMM Hashlists repository ready at {RepositoryDirectory}", repositoryDirectory);

        return repositoryDirectory;
    }

    private async Task RunGitAsync(string arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("git", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var output = (await stdout).Trim();
        var error = (await stderr).Trim();

        if (output.Length > 0)
        {
            logger.LogDebug("git {Arguments}: {Output}", arguments, output);
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {arguments} failed with exit code {process.ExitCode}: {error}");
        }
    }

    private static void CleanRepoExtras(string repositoryDirectory, string fileName)
    {
        var repoIndex = Path.Combine(repositoryDirectory, fileName);

        if (File.Exists(repoIndex))
        {
            File.Delete(repoIndex);
        }
    }
}
