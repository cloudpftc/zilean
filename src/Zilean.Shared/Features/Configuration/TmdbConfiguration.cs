namespace Zilean.Shared.Features.Configuration;

/// <summary>
/// TMDB (The Movie Database) settings used by the IMDb-driven Prowlarr
/// backfill to expand a TV title into per-season / per-episode searches.
/// Bound from the <c>Zilean:Tmdb</c> configuration section, so the
/// container environment variable <c>Zilean__Tmdb__AccessToken</c> feeds it
/// (environment variables are added after the JSON file, so they win).
/// </summary>
public class TmdbConfiguration
{
    /// <summary>
    /// TMDB v4 read access token (JWT), sent as
    /// <c>Authorization: Bearer &lt;token&gt;</c>. Defaults to an empty
    /// string; when it is empty the backfill skips TV season/episode
    /// expansion and searches the bare title instead, rather than issuing
    /// unauthenticated requests to TMDB.
    /// </summary>
    public string AccessToken { get; set; } = "";
}
