using JellyFusion.Configuration;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace JellyFusion.Modules.Slider;

/// <summary>
/// Fetches trailer URLs from TMDB for the Editor's Choice slider.
/// Respects the configured trailer language and falls back to subtitled
/// version when no dubbed version is available.
/// </summary>
public class TrailerService
{
    private readonly IHttpClientFactory _http;
    private readonly ILogger<TrailerService> _logger;

    private const string TmdbBaseUrl = "https://api.themoviedb.org/3";

    // Map plugin language codes → TMDB language codes
    private static readonly Dictionary<string, string> LangMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "es",     "es-MX" },
        { "es-419", "es-MX" },
        { "en",     "en-US" },
        { "pt",     "pt-BR" },
        { "fr",     "fr-FR" }
    };

    public TrailerService(IHttpClientFactory http, ILogger<TrailerService> logger)
    {
        _http   = http;
        _logger = logger;
    }

    /// <summary>
    /// Returns the best trailer URL for <paramref name="item"/> given the config.
    /// Returns null if none found.
    /// </summary>
    public async Task<string?> GetTrailerUrlAsync(
        BaseItem item, SliderConfig cfg, CancellationToken ct = default)
    {
        if (!cfg.TrailerEnabled) return null;
        if (string.IsNullOrEmpty(cfg.TmdbApiKey)) return null;

        // Local trailer lookup skipped to stay compatible across Jellyfin versions.
        // TMDB fallback below handles everything.

        // Determine target language
        string targetLang = cfg.TrailerLanguage == "Auto"
            ? LangMap.GetValueOrDefault(Plugin.Instance?.Configuration?.Language ?? "es", "es-MX")
            : LangMap.GetValueOrDefault(cfg.TrailerLanguage, "es-MX");

        // Get TMDB ID
        string? tmdbId = null;
        item.ProviderIds?.TryGetValue("Tmdb", out tmdbId);
        if (string.IsNullOrEmpty(tmdbId)) return null;

        string mediaType = item is MediaBrowser.Controller.Entities.Movies.Movie ? "movie" : "tv";

        // Try dubbed version first
        var url = await FetchTmdbTrailerAsync(tmdbId, mediaType, targetLang, cfg.TmdbApiKey, ct);

        // Fallback to English with subtitles if configured
        if (url is null && cfg.TrailerSubtitleFallback && targetLang != "en-US")
        {
            _logger.LogDebug("No {Lang} trailer for {Item}, trying English fallback", targetLang, item.Name);
            url = await FetchTmdbTrailerAsync(tmdbId, mediaType, "en-US", cfg.TmdbApiKey, ct);
        }

        return url;
    }

    private async Task<string?> FetchTmdbTrailerAsync(
        string tmdbId, string mediaType, string lang, string apiKey, CancellationToken ct)
    {
        try
        {
            var client   = _http.CreateClient("JellyFusion");
            var endpoint = $"{TmdbBaseUrl}/{mediaType}/{tmdbId}/videos?api_key={apiKey}&language={lang}";
            var response = await client.GetStringAsync(endpoint, ct);

            using var doc    = JsonDocument.Parse(response);
            var results = doc.RootElement.GetProperty("results");

            // Prefer official trailers, then teasers
            foreach (var type in new[] { "Trailer", "Teaser" })
            {
                foreach (var video in results.EnumerateArray())
                {
                    if (video.TryGetProperty("type", out var t) && t.GetString() == type &&
                        video.TryGetProperty("site", out var s) && s.GetString() == "YouTube" &&
                        video.TryGetProperty("key", out var k))
                    {
                        return $"https://www.youtube.com/watch?v={k.GetString()}";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TMDB trailer fetch failed for {Id}/{Lang}", tmdbId, lang);
        }

        return null;
    }
}
