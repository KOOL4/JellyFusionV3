using JellyFusion.Configuration;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging;

namespace JellyFusion.Modules.Slider;

/// <summary>
/// Fetches trailer URLs from TMDB for the Editor's Choice slider.
/// Respects the configured trailer language and falls back to subtitled
/// version when no dubbed version is available.
/// </summary>
public class TrailerService
{
    private readonly TmdbService _tmdb;
    private readonly ILogger<TrailerService> _logger;

    public TrailerService(TmdbService tmdb, ILogger<TrailerService> logger)
    {
        _tmdb   = tmdb;
        _logger = logger;
    }

    /// <summary>
    /// Returns the best YouTube video key for <paramref name="item"/> given
    /// the slider config. Returns null if no trailer is found.
    /// v3.0.20: returns just the key (e.g. "abc123XYZ") instead of a full
    /// URL — bootstrap.js embeds the YouTube IFrame and prefers the bare
    /// key for the iframe URL.
    /// </summary>
    public async Task<string?> GetTrailerKeyAsync(
        BaseItem item, SliderConfig cfg, CancellationToken ct = default)
    {
        if (!cfg.TrailerEnabled) return null;
        if (!_tmdb.HasKey()) return null;

        // Get TMDB ID
        string? tmdbId = null;
        item.ProviderIds?.TryGetValue("Tmdb", out tmdbId);
        if (string.IsNullOrEmpty(tmdbId)) return null;

        string mediaType = item is MediaBrowser.Controller.Entities.Movies.Movie ? "movie" : "tv";

        // Determine target language
        string targetLang = string.Equals(cfg.TrailerLanguage, "Auto", StringComparison.OrdinalIgnoreCase)
            ? _tmdb.MapLanguage(Plugin.Instance?.Configuration?.Language)
            : _tmdb.MapLanguage(cfg.TrailerLanguage);

        // Try preferred language first.
        var key = await _tmdb.GetYoutubeTrailerKeyAsync(tmdbId, mediaType, targetLang, ct);

        // Fallback to English with subtitles if configured.
        if (key is null && cfg.TrailerSubtitleFallback && targetLang != "en-US")
        {
            _logger.LogDebug("No {Lang} trailer for {Item}, trying English fallback",
                targetLang, item.Name);
            key = await _tmdb.GetYoutubeTrailerKeyAsync(tmdbId, mediaType, "en-US", ct);
        }
        return key;
    }

    /// <summary>
    /// Backwards-compatible URL flavour for v3.0.19 callers (controller).
    /// Returns the YouTube watch URL or null.
    /// </summary>
    public async Task<string?> GetTrailerUrlAsync(
        BaseItem item, SliderConfig cfg, CancellationToken ct = default)
    {
        var key = await GetTrailerKeyAsync(item, cfg, ct);
        return string.IsNullOrEmpty(key) ? null : $"https://www.youtube.com/watch?v={key}";
    }
}
