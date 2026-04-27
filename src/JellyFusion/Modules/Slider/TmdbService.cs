using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace JellyFusion.Modules.Slider;

/// <summary>
/// v3.0.20 — central wrapper for The Movie Database (TMDB) HTTP API.
/// Owns API-key resolution (bundled default → admin override) and the
/// shared <see cref="IHttpClientFactory"/> client used for every TMDB
/// call across the plugin (trailers, genre rails, trending top-10).
///
/// Why a separate service instead of inlining HTTP calls:
///   - One place to bake in the bundled default API key.
///   - One place to add retries / timeouts / rate-limit handling later.
///   - One place to add response caching so we don't hammer TMDB on
///     every home-page render.
/// </summary>
public class TmdbService
{
    private readonly IHttpClientFactory     _http;
    private readonly ILogger<TmdbService>   _logger;

    public const string TmdbBaseUrl = "https://api.themoviedb.org/3";

    /// <summary>
    /// Bundled default TMDB API key. Placeholder for now — when the
    /// project gets its own registered TMDB application, drop the
    /// public read key here so out-of-the-box installs work without
    /// the admin pasting their own key.
    ///
    /// Until populated, users still enter a personal key via the
    /// Inicio tab (Home.TmdbApiKey) and everything works the same.
    /// </summary>
    private const string DefaultApiKey = "";

    // Plugin-language → TMDB locale.
    private static readonly Dictionary<string, string> LangMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "es",     "es-MX" },
        { "es-419", "es-MX" },
        { "es-ES",  "es-ES" },
        { "en",     "en-US" },
        { "pt",     "pt-BR" },
        { "fr",     "fr-FR" }
    };

    public TmdbService(IHttpClientFactory http, ILogger<TmdbService> logger)
    {
        _http   = http;
        _logger = logger;
    }

    /// <summary>
    /// Resolves the active TMDB key with this priority:
    ///   1. Admin-supplied Home.TmdbApiKey (Inicio tab).
    ///   2. Legacy Slider.TmdbApiKey (UI removed in v3.0.17 but field
    ///      kept for backwards compat with persisted configs).
    ///   3. Bundled DefaultApiKey constant.
    ///   4. null - callers must skip the request gracefully.
    /// </summary>
    public string? ResolveApiKey()
    {
        var cfg = Plugin.Instance?.Configuration;

        var home = cfg?.Home?.TmdbApiKey;
        if (!string.IsNullOrWhiteSpace(home)) return home;

        var legacy = cfg?.Slider?.TmdbApiKey;
        if (!string.IsNullOrWhiteSpace(legacy)) return legacy;

        if (!string.IsNullOrWhiteSpace(DefaultApiKey)) return DefaultApiKey;

        return null;
    }

    /// <summary>True if any usable key is available.</summary>
    public bool HasKey() => !string.IsNullOrEmpty(ResolveApiKey());

    /// <summary>
    /// Map a plugin language code (es, en, pt, fr, es-419 …) to a TMDB
    /// locale (es-MX, en-US …). Falls back to es-MX since this plugin
    /// is Latin-American by default.
    /// </summary>
    public string MapLanguage(string? lang)
        => string.IsNullOrEmpty(lang)
            ? "es-MX"
            : (LangMap.TryGetValue(lang, out var m) ? m : "es-MX");

    /// <summary>
    /// GET /movie/{id}/videos or /tv/{id}/videos and return the first
    /// YouTube Trailer (or Teaser) key. Returns null when no video
    /// matches the language or TMDB returns nothing.
    /// </summary>
    public async Task<string?> GetYoutubeTrailerKeyAsync(
        string tmdbId,
        string mediaType,
        string lang,
        CancellationToken ct = default)
    {
        var apiKey = ResolveApiKey();
        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(tmdbId)) return null;

        try
        {
            var client = _http.CreateClient("JellyFusion");
            var url    = $"{TmdbBaseUrl}/{mediaType}/{tmdbId}/videos" +
                         $"?api_key={apiKey}&language={lang}";
            var json   = await client.GetStringAsync(url, ct);

            using var doc = JsonDocument.Parse(json);
            var results   = doc.RootElement.GetProperty("results");

            // Prefer official trailers, then teasers.
            foreach (var preferred in new[] { "Trailer", "Teaser" })
            {
                foreach (var v in results.EnumerateArray())
                {
                    if (v.TryGetProperty("type", out var t) && t.GetString() == preferred &&
                        v.TryGetProperty("site", out var s) && s.GetString() == "YouTube" &&
                        v.TryGetProperty("key",  out var k))
                    {
                        return k.GetString();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "TMDB videos fetch failed: {Id}/{Lang}", tmdbId, lang);
        }
        return null;
    }

    /// <summary>
    /// GET /discover/movie?with_genres=X (or tv) and return the TMDB
    /// IDs of the first <paramref name="max"/> matching items. Used to
    /// build genre rails by intersecting these IDs with the user's
    /// library (items whose ProviderIds["Tmdb"] is in the result set).
    /// </summary>
    public async Task<HashSet<string>> GetGenreItemIdsAsync(
        int    genreId,
        string mediaType,
        string lang,
        int    max = 60,
        CancellationToken ct = default)
    {
        var apiKey = ResolveApiKey();
        var ids    = new HashSet<string>();
        if (string.IsNullOrEmpty(apiKey) || genreId <= 0) return ids;

        try
        {
            var client = _http.CreateClient("JellyFusion");
            // We hit page 1 + page 2 (40 results) when max > 20 so the
            // intersection has enough overlap with the local library.
            int pages = max <= 20 ? 1 : (max <= 40 ? 2 : 3);
            for (int p = 1; p <= pages; p++)
            {
                var url = $"{TmdbBaseUrl}/discover/{mediaType}" +
                          $"?api_key={apiKey}&language={lang}&with_genres={genreId}" +
                          $"&sort_by=popularity.desc&page={p}";
                var json = await client.GetStringAsync(url, ct);

                using var doc = JsonDocument.Parse(json);
                var results   = doc.RootElement.GetProperty("results");
                foreach (var item in results.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var id))
                    {
                        ids.Add(id.GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }
                }
                if (results.GetArrayLength() == 0) break;  // no more pages
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "TMDB discover fetch failed: genre={Genre}", genreId);
        }
        return ids;
    }
}
