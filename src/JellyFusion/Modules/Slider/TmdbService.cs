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
    /// EMBEDDABLE YouTube Trailer (or Teaser) key.
    ///
    /// v3.0.24: was returning the first key blindly, but ~30% of TMDB
    /// trailers have embedding disabled by the YouTube owner — those
    /// surfaced as "Error 153" iframes on the user's banner. Now we
    /// gather every YouTube Trailer/Teaser, then call YouTube's oEmbed
    /// endpoint to verify each one is actually embeddable (200 OK =
    /// playable in iframe; 401 = embed disabled; 404 = video gone).
    /// First green check wins.
    /// </summary>
    public async Task<string?> GetYoutubeTrailerKeyAsync(
        string tmdbId,
        string mediaType,
        string lang,
        CancellationToken ct = default)
    {
        var apiKey = ResolveApiKey();
        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(tmdbId)) return null;

        var candidates = new List<string>();
        try
        {
            var client = _http.CreateClient("JellyFusion");
            var url    = $"{TmdbBaseUrl}/{mediaType}/{tmdbId}/videos" +
                         $"?api_key={apiKey}&language={lang}";
            var json   = await client.GetStringAsync(url, ct);

            using var doc = JsonDocument.Parse(json);
            var results   = doc.RootElement.GetProperty("results");

            // Prefer official trailers, then teasers — but collect ALL
            // matching keys so we can fall through to the next when an
            // earlier one isn't embeddable.
            foreach (var preferred in new[] { "Trailer", "Teaser" })
            {
                foreach (var v in results.EnumerateArray())
                {
                    if (v.TryGetProperty("type", out var t) && t.GetString() == preferred &&
                        v.TryGetProperty("site", out var s) && s.GetString() == "YouTube" &&
                        v.TryGetProperty("key",  out var k))
                    {
                        var key = k.GetString();
                        if (!string.IsNullOrEmpty(key) && !candidates.Contains(key))
                            candidates.Add(key);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "TMDB videos fetch failed: {Id}/{Lang}", tmdbId, lang);
            return null;
        }

        if (candidates.Count == 0) return null;

        // Probe each candidate via YouTube oEmbed. 200 means embeddable.
        // We cap probes at 5 to keep the request snappy on items with
        // big video catalogues.
        var probeClient = _http.CreateClient("JellyFusion");
        foreach (var key in candidates.Take(5))
        {
            if (await IsYoutubeEmbeddableAsync(probeClient, key, ct))
            {
                _logger.LogDebug("Trailer probe OK: {Key} ({Tmdb}/{Lang})", key, tmdbId, lang);
                return key;
            }
        }

        _logger.LogDebug("All {Count} TMDB trailer candidates were non-embeddable for {Tmdb}",
            candidates.Count, tmdbId);
        return null;
    }

    /// <summary>
    /// v3.0.24 — pre-flight check: hit YouTube's oEmbed endpoint to
    /// verify a video can be played in an iframe. Returns false on
    /// 401 (embed disabled by owner), 404 (video deleted), 5xx, or
    /// any network error. Cheap (~150ms) and saves the banner from
    /// painting an Error 153 page.
    /// </summary>
    private async Task<bool> IsYoutubeEmbeddableAsync(
        HttpClient client, string key, CancellationToken ct)
    {
        try
        {
            var probeUrl = $"https://www.youtube.com/oembed" +
                           $"?url=https%3A%2F%2Fwww.youtube.com%2Fwatch%3Fv%3D{key}" +
                           $"&format=json";
            using var req = new HttpRequestMessage(HttpMethod.Get, probeUrl);
            using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "oEmbed probe threw for {Key}", key);
            // On network failure assume it MIGHT work - the client-side
            // postMessage error detector is the second line of defense.
            return true;
        }
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
