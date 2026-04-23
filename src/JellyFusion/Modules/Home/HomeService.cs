using JellyFusion.Configuration;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace JellyFusion.Modules.Home;

/// <summary>
/// Resolves the items for each enabled Home rail.
/// Supports Local library queries plus external trending sources
/// (TMDB, MDBList, Trakt) when the user supplies an API key.
/// </summary>
public class HomeService
{
    private readonly ILibraryManager      _library;
    private readonly IUserManager         _userManager;
    private readonly IHttpClientFactory   _http;
    private readonly ILogger<HomeService> _logger;

    private const string TmdbBase    = "https://api.themoviedb.org/3";
    private const string MdbListBase = "https://api.mdblist.com";
    private const string TraktBase   = "https://api.trakt.tv";

    public HomeService(
        ILibraryManager library,
        IUserManager userManager,
        IHttpClientFactory http,
        ILogger<HomeService> logger)
    {
        _library     = library;
        _userManager = userManager;
        _http        = http;
        _logger      = logger;
    }

    /// <summary>Returns a lightweight payload describing every enabled rail.</summary>
    public async Task<IReadOnlyList<object>> BuildRailsAsync(Guid? userId, CancellationToken ct)
    {
        var cfg = Plugin.Instance?.Configuration?.Home;
        if (cfg is null) return Array.Empty<object>();

        var user = userId.HasValue ? _userManager.GetUserById(userId.Value) : null;
        var results = new List<object>();

        foreach (var rail in cfg.Rails.Where(r => r.Enabled))
        {
            try
            {
                var items = rail.Id switch
                {
                    "continueWatching"  => GetContinueWatching(user, rail.MaxItems),
                    "top10Movies"       => await GetTop10Async(cfg, "movie", rail.MaxItems, ct),
                    "top10Series"       => await GetTop10Async(cfg, "tv",    rail.MaxItems, ct),
                    "becauseYouWatched" => GetBecauseYouWatched(user, rail.MaxItems),
                    "newReleases"       => GetNewReleases(rail.MaxItems),
                    "categories"        => GetCategories(rail.MaxItems),
                    "recommended"       => GetRecommended(user, rail.MaxItems),
                    "upcoming"          => await GetUpcomingAsync(cfg, rail.MaxItems, ct),
                    "studios"           => GetStudiosRail(),
                    _                   => Array.Empty<object>()
                };

                results.Add(new
                {
                    id       = rail.Id,
                    title    = rail.Title,
                    source   = cfg.DataSource,
                    maxItems = rail.MaxItems,
                    items
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to build rail {Id}", rail.Id);
            }
        }

        return results;
    }

    // ── Local library rails ─────────────────────────────────────

    private object[] GetContinueWatching(Jellyfin.Data.Entities.User? user, int max)
    {
        if (user is null) return Array.Empty<object>();
        var query = new InternalItemsQuery(user)
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Episode },
            IsResumable      = true,
            Recursive        = true,
            Limit            = max,
            OrderBy          = new[] { (ItemSortBy.DatePlayed, SortOrder.Descending) }
        };
        return _library.GetItemsResult(query).Items
            .Select(ToCardDto)
            .ToArray();
    }

    private object[] GetBecauseYouWatched(Jellyfin.Data.Entities.User? user, int max)
    {
        if (user is null) return Array.Empty<object>();
        // Pick a random recently-watched item as the seed for similarity
        var played = new InternalItemsQuery(user)
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
            IsPlayed         = true,
            Recursive        = true,
            Limit            = 1,
            OrderBy          = new[] { (ItemSortBy.DatePlayed, SortOrder.Descending) }
        };
        var seed = _library.GetItemsResult(played).Items.FirstOrDefault();
        if (seed is null) return Array.Empty<object>();

        // Query items sharing genres with the seed
        var genres = seed.Genres?.Take(2).ToArray() ?? Array.Empty<string>();
        if (genres.Length == 0) return Array.Empty<object>();

        var query = new InternalItemsQuery(user)
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
            Genres           = genres,
            Recursive        = true,
            Limit            = max,
            ExcludeItemIds   = new[] { seed.Id }
        };
        return _library.GetItemsResult(query).Items
            .Select(ToCardDto)
            .Prepend(new
            {
                id    = "__seed__",
                name  = $"Porque viste {seed.Name}",
                kind  = "SeedCard",
                year  = seed.ProductionYear
            })
            .ToArray();
    }

    private object[] GetNewReleases(int max)
    {
        var cutoff = DateTime.UtcNow.AddDays(-60);
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
            MinPremiereDate  = cutoff,
            Recursive        = true,
            Limit            = max,
            OrderBy          = new[] { (ItemSortBy.PremiereDate, SortOrder.Descending) }
        };
        return _library.GetItemsResult(query).Items
            .Select(ToCardDto)
            .ToArray();
    }

    private object[] GetCategories(int max)
    {
        // Returns top genre buckets with cover images pulled from the first item in each
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
            Recursive        = true,
            Limit            = 500
        };
        var all = _library.GetItemsResult(query).Items;
        return all
            .SelectMany(i => (i.Genres ?? Array.Empty<string>()).Select(g => new { Genre = g, Item = i }))
            .GroupBy(x => x.Genre, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(max)
            .Select(g => (object)new
            {
                id    = g.Key,
                name  = g.Key,
                kind  = "Category",
                count = g.Count(),
                imageUrl = $"/Items/{g.First().Item.Id}/Images/Backdrop"
            })
            .ToArray();
    }

    private object[] GetRecommended(Jellyfin.Data.Entities.User? user, int max)
    {
        // Top-rated unseen items
        var query = user is not null
            ? new InternalItemsQuery(user)
              {
                  IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
                  IsPlayed         = false,
                  Recursive        = true,
                  Limit            = max,
                  OrderBy          = new[] { (ItemSortBy.CommunityRating, SortOrder.Descending) }
              }
            : new InternalItemsQuery
              {
                  IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
                  Recursive        = true,
                  Limit            = max,
                  OrderBy          = new[] { (ItemSortBy.CommunityRating, SortOrder.Descending) }
              };

        return _library.GetItemsResult(query).Items
            .Select(ToCardDto)
            .ToArray();
    }

    private object[] GetStudiosRail()
    {
        var studios = Plugin.Instance?.Configuration?.Studios;
        if (studios is null || !studios.Enabled) return Array.Empty<object>();

        return studios.Items
            .OrderBy(s => s.SortOrder)
            .Select(s => (object)new
            {
                id       = s.Name,
                name     = s.Name,
                kind     = "Studio",
                logoUrl  = s.LogoUrl,
                gradient = s.Gradient,
                invert   = s.Invert,
                tags     = s.Tags
            })
            .ToArray();
    }

    // ── External sources ────────────────────────────────────────

    private async Task<object[]> GetTop10Async(HomeConfig cfg, string mediaType, int max, CancellationToken ct)
    {
        try
        {
            return cfg.DataSource switch
            {
                "TMDB"    when !string.IsNullOrEmpty(cfg.TmdbApiKey)    => await TmdbTrendingAsync(cfg.TmdbApiKey!,    mediaType, max, ct),
                "MDBList" when !string.IsNullOrEmpty(cfg.MdbListApiKey) => await MdbListTopAsync (cfg.MdbListApiKey!,  mediaType, max, ct),
                "Trakt"   when !string.IsNullOrEmpty(cfg.TraktApiKey)   => await TraktTrendingAsync(cfg.TraktApiKey!,  mediaType, max, ct),
                _                                                       => LocalTop10(mediaType, max)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "External Top10 failed, falling back to local");
            return LocalTop10(mediaType, max);
        }
    }

    private object[] LocalTop10(string mediaType, int max)
    {
        var kind  = mediaType == "tv" ? BaseItemKind.Series : BaseItemKind.Movie;
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = new[] { kind },
            Recursive        = true,
            Limit            = max,
            OrderBy          = new[] { (ItemSortBy.CommunityRating, SortOrder.Descending) }
        };
        return _library.GetItemsResult(query).Items
            .Select((item, i) => (object)new
            {
                id       = item.Id,
                rank     = i + 1,
                name     = item.Name,
                year     = item.ProductionYear,
                kind     = item.GetType().Name,
                imageUrl = $"/Items/{item.Id}/Images/Primary"
            })
            .ToArray();
    }

    private async Task<object[]> TmdbTrendingAsync(string apiKey, string mediaType, int max, CancellationToken ct)
    {
        var url  = $"{TmdbBase}/trending/{mediaType}/week?api_key={apiKey}";
        var json = await _http.CreateClient("JellyFusion").GetStringAsync(url, ct);
        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("results");

        var list = new List<object>();
        int rank = 1;
        foreach (var v in items.EnumerateArray().Take(max))
        {
            list.Add(new
            {
                id       = v.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
                rank     = rank++,
                name     = v.TryGetProperty("title",    out var t1) ? t1.GetString()
                         : v.TryGetProperty("name",     out var t2) ? t2.GetString() : "",
                year     = 0,
                kind     = mediaType == "tv" ? "Series" : "Movie",
                imageUrl = v.TryGetProperty("poster_path", out var p) && p.GetString() is string ps
                           ? $"https://image.tmdb.org/t/p/w500{ps}"
                           : null
            });
        }
        return list.ToArray();
    }

    private async Task<object[]> MdbListTopAsync(string apiKey, string mediaType, int max, CancellationToken ct)
    {
        // MDBList "top100_week" style endpoint — public list IDs for TV/Movies
        var listSlug = mediaType == "tv" ? "top-100-tv-shows-by-score" : "top-100-movies-by-score";
        var url      = $"{MdbListBase}/lists/top-lists/{listSlug}/items?apikey={apiKey}";

        try
        {
            var json = await _http.CreateClient("JellyFusion").GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);

            var list = new List<object>();
            int rank = 1;
            foreach (var v in doc.RootElement.EnumerateArray().Take(max))
            {
                list.Add(new
                {
                    id       = v.TryGetProperty("id",    out var id)   ? id.GetString() : "",
                    rank     = rank++,
                    name     = v.TryGetProperty("title", out var t)    ? t.GetString()  : "",
                    year     = v.TryGetProperty("release_year", out var y) ? y.GetInt32() : 0,
                    kind     = mediaType == "tv" ? "Series" : "Movie",
                    imageUrl = v.TryGetProperty("poster", out var pp)  ? pp.GetString() : null
                });
            }
            return list.ToArray();
        }
        catch
        {
            return Array.Empty<object>();
        }
    }

    private async Task<object[]> TraktTrendingAsync(string apiKey, string mediaType, int max, CancellationToken ct)
    {
        var path   = mediaType == "tv" ? "shows/trending" : "movies/trending";
        var client = _http.CreateClient("JellyFusion");
        var req    = new HttpRequestMessage(HttpMethod.Get, $"{TraktBase}/{path}?limit={max}");
        req.Headers.Add("trakt-api-version", "2");
        req.Headers.Add("trakt-api-key", apiKey);
        var resp = await client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var list = new List<object>();
        int rank = 1;
        foreach (var v in doc.RootElement.EnumerateArray().Take(max))
        {
            var media = v.TryGetProperty(mediaType == "tv" ? "show" : "movie", out var m) ? m : v;
            list.Add(new
            {
                id       = media.TryGetProperty("ids",   out var ids) && ids.TryGetProperty("trakt", out var tid) ? tid.GetInt32() : 0,
                rank     = rank++,
                name     = media.TryGetProperty("title", out var t)   ? t.GetString() : "",
                year     = media.TryGetProperty("year",  out var y)   ? y.GetInt32()  : 0,
                kind     = mediaType == "tv" ? "Series" : "Movie",
                imageUrl = (string?)null
            });
        }
        return list.ToArray();
    }

    private async Task<object[]> GetUpcomingAsync(HomeConfig cfg, int max, CancellationToken ct)
    {
        if (cfg.DataSource == "TMDB" && !string.IsNullOrEmpty(cfg.TmdbApiKey))
        {
            try
            {
                var url  = $"{TmdbBase}/movie/upcoming?api_key={cfg.TmdbApiKey}";
                var json = await _http.CreateClient("JellyFusion").GetStringAsync(url, ct);
                using var doc = JsonDocument.Parse(json);
                var items = doc.RootElement.GetProperty("results");

                return items.EnumerateArray()
                    .Take(max)
                    .Select(v => (object)new
                    {
                        id       = v.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
                        name     = v.TryGetProperty("title", out var t) ? t.GetString() : "",
                        year     = 0,
                        kind     = "Upcoming",
                        imageUrl = v.TryGetProperty("poster_path", out var p) && p.GetString() is string ps
                                   ? $"https://image.tmdb.org/t/p/w500{ps}" : null,
                        release  = v.TryGetProperty("release_date", out var r) ? r.GetString() : null
                    })
                    .ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TMDB upcoming failed");
            }
        }
        return Array.Empty<object>();
    }

    private static object ToCardDto(BaseItem item) => new
    {
        id       = item.Id,
        name     = item.Name,
        year     = item.ProductionYear,
        kind     = item.GetType().Name,
        rating   = item.CommunityRating,
        imageUrl = $"/Items/{item.Id}/Images/Primary"
    };
}
