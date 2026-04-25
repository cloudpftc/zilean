using Microsoft.EntityFrameworkCore;
using Zilean.Database;
using Zilean.Shared.Features.Dmm;
using Zilean.Shared.Features.Imdb;

namespace Zilean.Benchmarks.Benchmarks;

public class ImdbMatchingBenchmarks
{
    private ZileanDbContext _context = null!;
    private List<string> _torrentTitles = null!;
    private List<string> _imdbTitles = null!;
    private Random _random = null!;

    private static readonly string[] _movieTitles =
    [
        "The Shawshank Redemption", "The Godfather", "The Dark Knight", "Pulp Fiction",
        "The Lord of the Rings", "Fight Club", "Forrest Gump", "Inception",
        "The Matrix", "Goodfellas", "Se7en", "The Silence of the Lambs",
        "City of God", "The Departed", "Memento", "The Prestige",
        "Interstellar", "The Avengers", "Django Unchained", "WALL-E",
        "The Dark Knight Rises", "Gladiator", "The Lion King", "Alien",
        "Star Wars", "Jurassic Park", "Terminator 2", "Avatar",
        "Titanic", "The Avengers", "Frozen", "The Hobbit",
        "Guardians of the Galaxy", "Iron Man", "Thor", "Captain America",
        "Spider-Man", "Batman", "Superman", "Wonder Woman",
        "Aquaman", "Shazam", "Black Panther", "Doctor Strange",
        "Ant-Man", "Captain Marvel", "Black Widow", "Hawkeye",
        "Loki", "WandaVision", "The Falcon", "Winter Soldier"
    ];

    private static readonly string[] _tvShowTitles =
    [
        "Breaking Bad", "Game of Thrones", "The Wire", "The Sopranos",
        "Friends", "The Office", "Parks and Recreation", "Brooklyn Nine-Nine",
        "Stranger Things", "The Crown", "The Mandalorian", "The Witcher",
        "House of Cards", "Narcos", "Money Heist", "Dark",
        "Black Mirror", "Westworld", "The Expanse", "Foundation",
        "Severance", "Ted Lasso", "The Bear", "Succession",
        "Euphoria", "The Last of Us", "House of the Dragon", "Ring of Power"
    ];

    private static readonly string[] _releaseGroups =
    [
        "SPARKS", "MEMENTO", "TAM", "CMRG", "FLX", "METCON",
        "NOGRP", "XEM", "KOGI", "QCF", "DEEP", "NEO", "ViSUM"
    ];

    private static readonly string[] _resolutions = ["720p", "1080p", "2160p", "4K"];
    private static readonly string[] _sources = ["BluRay", "WEB-DL", "WEBRip", "HDRip", "DVDRip"];
    private static readonly string[] _codecs = ["x264", "x265", "HEVC", "AVC", "XviD"];

    [GlobalSetup]
    public void Setup()
    {
        _random = new Random(42);
        _context = new ZileanDbContext();

        // Generate realistic IMDb titles
        _imdbTitles = GenerateImdbTitles(5000);

        // Generate realistic torrent titles
        _torrentTitles = GenerateTorrentTitles(10000);
    }

    [Benchmark]
    public async Task<int> MatchTitle_1K()
    {
        var count = 0;
        var titles = _torrentTitles.Take(1000).ToList();

        foreach (var torrentTitle in titles)
        {
            var result = await _context.Database
                .SqlQueryRaw<double>(
                    "SELECT imdb_match_title({0}, {1})",
                    torrentTitle,
                    _imdbTitles[_random.Next(_imdbTitles.Count)]
                )
                .ToListAsync();

            count += result.Count;
        }

        return count;
    }

    [Benchmark]
    public async Task<int> MatchTitle_10K()
    {
        var count = 0;

        foreach (var torrentTitle in _torrentTitles)
        {
            var result = await _context.Database
                .SqlQueryRaw<double>(
                    "SELECT imdb_match_title({0}, {1})",
                    torrentTitle,
                    _imdbTitles[_random.Next(_imdbTitles.Count)]
                )
                .ToListAsync();

            count += result.Count;
        }

        return count;
    }

    [Benchmark]
    public async Task<List<dynamic>> BatchFind_1K()
    {
        var results = await _context.Database
            .SqlQueryRaw<dynamic>(
                "SELECT * FROM batch_find_imdb_matches(1000, 0.45)"
            )
            .ToListAsync();

        return results;
    }

    [Benchmark]
    public async Task<List<dynamic>> BatchFind_5K()
    {
        var results = await _context.Database
            .SqlQueryRaw<dynamic>(
                "SELECT * FROM batch_find_imdb_matches(5000, 0.45)"
            )
            .ToListAsync();

        return results;
    }

    private List<string> GenerateImdbTitles(int count)
    {
        var titles = new List<string>();
        var usedTitles = new HashSet<string>();

        for (int i = 0; i < count; i++)
        {
            string title;
            if (_random.NextDouble() > 0.3)
            {
                // Movie
                title = _movieTitles[_random.Next(_movieTitles.Length)];
                var year = 1970 + _random.Next(55);
                title = $"{title} ({year})";
            }
            else
            {
                // TV Show
                title = _tvShowTitles[_random.Next(_tvShowTitles.Length)];
                var year = 2000 + _random.Next(25);
                title = $"{title} ({year})";
            }

            // Add variant
            var variant = _random.Next(5);
            title = variant switch
            {
                0 => title,
                1 => $"{title} Season {_random.Next(1, 10)}",
                2 => $"{title} Complete Series",
                3 => $"{title} Episode {_random.Next(1, 25)}",
                _ => title
            };

            if (!usedTitles.Contains(title))
            {
                usedTitles.Add(title);
                titles.Add(title);
            }
            else
            {
                i--;
            }
        }

        return titles;
    }

    private List<string> GenerateTorrentTitles(int count)
    {
        var titles = new List<string>();

        for (int i = 0; i < count; i++)
        {
            var isMovie = _random.NextDouble() > 0.3;
            string baseTitle;

            if (isMovie)
            {
                baseTitle = _movieTitles[_random.Next(_movieTitles.Length)];
            }
            else
            {
                baseTitle = _tvShowTitles[_random.Next(_tvShowTitles.Length)];
            }

            // Clean the title (replace spaces with dots)
            var cleanTitle = baseTitle.Replace(" ", ".").Replace(":", "").Replace("'", "");

            var year = 1970 + _random.Next(55);
            var resolution = _resolutions[_random.Next(_resolutions.Length)];
            var source = _sources[_random.Next(_sources.Length)];
            var codec = _codecs[_random.Next(_codecs.Length)];
            var group = _releaseGroups[_random.Next(_releaseGroups.Length)];

            // Build torrent-style filename
            var filename = $"{cleanTitle}.{year}.{resolution}.{source}.{codec}-{group}";

            titles.Add(filename);
        }

        return titles;
    }
}