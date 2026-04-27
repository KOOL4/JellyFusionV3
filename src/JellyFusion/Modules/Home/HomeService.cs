using JellyFusion.Configuration;
using Jellyfin.Data.Enums;
// v3.0.12: SortOrder and PermissionKind moved to this namespace in 10.11.
// We import both because ItemSortBy is still in Jellyfin.Data.Enums.
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;
using System.Text.Json;
// Needed for PermissionKind.IsAdministrator used in the v3.0.7 admin fallback
// when rails run without a client-supplied userId.

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
        // v3.0.6: full outer try/catch so the endpoint NEVER 500s. If anything
        // throws we log and return an empty list; the bootstrap will show
        // "rails 0" on the debug badge instead of "rails ERR".
        try
        {
            var cfg = Plugin.Instance?.Configuration?.Home;
            if (cfg is null)
            {
                _logger.LogWarning("BuildRailsAsync: Home config is null (plugin config not initialised?)");
                return Array.Empty<object>();
            }

            // v3.0.10: ALWAYS populate defaults when railList is empty,
            // regardless of HasBeenSeeded. Reason: in the wild,
            // HasBeenSeeded=true + Rails=[] turned out to be the common
            // post-upgrade state (XmlSerializer race during seed,
            // partial config load, or a manual save while rails were
            // briefly empty in the admin UI). v3.0.8's "respect user-
            // cleared list" was nice in theory but in practice it left
            // 100% of upgraders with an empty home and the badge
            // showing "rails 0" with no rails visible. Better trade-off:
            // a user who genuinely wants an empty home can disable each
            // rail individually via the admin UI (Enabled=false) instead
            // of clearing the list entirely.
            var railList = cfg.Rails ?? new List<HomeRail>();
            if (railList.Count == 0)
            {
                _logger.LogInformation(
                    "BuildRailsAsync: persisted Rails empty (HasBeenSeeded={Seeded}), applying defaults so home isn't blank",
                    Plugin.Instance?.Configuration?.HasBeenSeeded ?? false);
                railList = HomeConfig.DefaultRails();
            }

            Jellyfin.Database.Implementations.Entities.User? user = null;
            if (userId.HasValue)
            {
                try { user = _userManager.GetUserById(userId.Value); }
                catch (Exception ex) { _logger.LogDebug(ex, "GetUserById failed, continuing without user"); }
            }

            // v3.0.7: every `InternalItemsQuery` that crosses the library
            // manager needs at least a user for permission checks on 10.10.
            // Without one, `_library.GetItemsResult(...)` silently returns
            // an empty result on the user-scoped rails AND - more importantly -
            // on the "public" rails like top10Movies/newReleases/categories,
            // because Recursive=true still enforces the calling user's view
            // filter. This was the single biggest reason v3.0.6 only showed
            // a banner: every rail got items:[] and bootstrap filtered them
            // all out. We now fall back to the first administrator so every
            // rail has something to query against.
            if (user is null)
            {
                try
                {
                    // v3.0.12: dropped IsAdministrator filter. See SliderService
                    // for the same change - User.HasPermission was removed in
                    // 10.11 and any user works for library queries.
                    user = _userManager.Users.FirstOrDefault();
                    if (user is not null)
                    {
                        _logger.LogDebug(
                            "BuildRailsAsync: no userId, using first-user fallback {UserId}",
                            user.Id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Admin fallback for rails failed");
                }
            }

            var results = new List<object>();

            foreach (var rail in railList.Where(r => r != null && r.Enabled))
            {
                try
                {
                    // v3.0.7: pass `user` into the "user-independent" rails too.
                    // In Jellyfin 10.10, a library query without a user scope
                    // returns empty for library-view items. Using the admin
                    // fallback for query scope is safe because we only return
                    // metadata already served by /Items/{id}/Images/Primary,
                    // which applies the calling client's own permissions.
                    var items = rail.Id switch
                    {
                        "continueWatching"  => GetContinueWatching(user, rail.MaxItems),
                        "top10Movies"       => await GetTop10Async(cfg, user, "movie", rail.MaxItems, ct),
                        "top10Series"       => await GetTop10Async(cfg, user, "tv",    rail.MaxItems, ct),
                        "becauseYouWatched" => GetBecauseYouWatched(user, rail.MaxItems),
                        "newReleases"       => GetNewReleases(user, rail.MaxItems),
                        "categories"        => GetCategories(user, rail.MaxItems),
                        "recommended"       => GetRecommended(user, rail.MaxItems),
                        "upcoming"          => await GetUpcomingAsync(cfg, rail.MaxItems, ct),
                        "studios"           => GetStudiosRail(),
                        _                   => Array.Empty<object>()
                    };

                    results.Add(new
                    {
                        id       = rail.Id,
                        title    = rail.Title ?? DefaultTitleFor(rail.Id),
                        source   = cfg.DataSource,
                        maxItems = rail.MaxItems,
                        showRank = rail.Id == "top10Movies" || rail.Id == "top10Series",
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "BuildRailsAsync outer failure");
            return Array.Empty<object>();
        }
    }

    private static string DefaultTitleFor(string? id) => id switch
    {
        "continueWatching"  => "Seguir viendo",
        "top10Movies"       => "Top 10 Películas",
        "top10Series"       => "Top 10 Series",
        "becauseYouWatched" => "Porque viste…",
        "newReleases"       => "Novedades",
        "categories"        => "Categorías",
        "recommended"       => "Recomendado para ti",
        "upcoming"          => "Próximamente",
        "studios"           => "Estudios",
        _                   => id ?? ""
    };

    // ── Local library rails ─────────────────────────────────────

    private object[] GetContinueWatching(Jellyfin.Database.Implementations.Entities.User? user, int max)
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

    private object[] GetBecauseYouWatched(Jellyfin.Database.Implementations.Entities.User? user, int max)
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

    private object[] GetNewReleases(Jellyfin.Database.Implementations.Entities.User? user, int max)
    {
        var cutoff = DateTime.UtcNow.AddDays(-60);
        // v3.0.7: scope by user when we have one so the library manager
        // returns actual items instead of the empty result it gives for
        // unscoped queries in 10.10.
        var query = user is not null
            ? new InternalItemsQuery(user)
              {
                  IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
                  MinPremiereDate  = cutoff,
                  Recursive        = true,
                  Limit            = max,
                  OrderBy          = new[] { (ItemSortBy.PremiereDate, SortOrder.Descending) }
              }
            : new InternalItemsQuery
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

    private object[] GetCategories(Jellyfin.Database.Implementations.Entities.User? user, int max)
    {
        // Returns top genre buckets with cover images pulled from the first item in each
        var query = user is not null
            ? new InternalItemsQuery(user)
              {
                  IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
                  Recursive        = true,
                  Limit            = 500
              }
            : new InternalItemsQuery
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

    private object[] GetRecommended(Jellyfin.Database.Implementations.Entities.User? user, int max)
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

        // v3.0.21: clickUrl now resolves to Jellyfin's studio detail
        // page (which lists every item tagged with that studio). The
        // search.html route used in v3.0.16 didn't work because Jellyfin
        // search only matches item TITLES, not studio metadata, so
        // clicking Netflix returned "Sorry! No results for Netflix".
        //
        // Resolution priority per studio item:
        //   1. CustomUrl override - admin can paste any URL.
        //   2. LinkMode == "CustomUrl" requires CustomUrl to be set.
        //   3. Auto: look up the studio entity by ANY of the names in
        //      StudioItem.Tags (comma-separated). The first hit's GUID
        //      becomes #/details?id=GUID&serverId=SID, which is the
        //      same URL Jellyfin uses when you click the studio chip
        //      from a movie's metadata page.
        //   4. Fallback: name-only search.html (last resort, often
        //      empty, but better than a dead button).

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
                tags     = s.Tags,
                clickUrl = ResolveStudioClickUrl(s)
            })
            .ToArray();
    }

    /// <summary>
    /// v3.0.21 — resolves the URL the JellyFusion home rail navigates
    /// to when the user clicks a studio card. See GetStudiosRail() for
    /// the priority order. Wrapped in try/catch so a missing studio
    /// entity (library never indexed it) never blocks the rail render.
    /// </summary>
    private string ResolveStudioClickUrl(JellyFusion.Configuration.StudioItem s)
    {
        try
        {
            // (1) Explicit override always wins.
            if (!string.IsNullOrWhiteSpace(s.CustomUrl))
                return s.CustomUrl!;
            if (string.Equals(s.LinkMode, "CustomUrl", StringComparison.OrdinalIgnoreCase))
                return s.CustomUrl ?? $"#/search.html?query={Uri.EscapeDataString(s.Name)}";

            // (2) Build the candidate name list from Tags + Name itself.
            var candidates = new List<string> { s.Name };
            if (!string.IsNullOrWhiteSpace(s.Tags))
            {
                foreach (var t in s.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries
                                                    | StringSplitOptions.TrimEntries))
                {
                    if (!candidates.Contains(t, StringComparer.OrdinalIgnoreCase))
                        candidates.Add(t);
                }
            }

            // (3) Auto: look up the FIRST matching Studio entity in the
            //     Jellyfin library. We query CollectionType doesn't apply
            //     here - studios are top-level entities of their own type.
            //     Using NameContains gives us a fuzzy match so "Disney+"
            //     in our Tags can find the "Disney" studio if the user's
            //     library only has the short name.
            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                var hits = _library.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.Studio },
                    NameContains     = candidate,
                    Limit            = 1
                });
                var studio = hits?.FirstOrDefault();
                if (studio is not null && studio.Id != Guid.Empty)
                {
                    return $"#/details?id={studio.Id:N}";
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ResolveStudioClickUrl failed for {Name}", s.Name);
        }

        // (4) Last resort - the same name-only search the previous
        // version used. The user will see "no results" but at least the
        // click registers and lands on a real Jellyfin page.
        return $"#/search.html?query={Uri.EscapeDataString(s.Name)}";
    }

    // ── External sources ────────────────────────────────────────

    private async Task<object[]> GetTop10Async(HomeConfig cfg, Jellyfin.Database.Implementations.Entities.User? user, string mediaType, int max, CancellationToken ct)
    {
        try
        {
            return cfg.DataSource switch
            {
                "TMDB"    when !string.IsNullOrEmpty(cfg.TmdbApiKey)    => await TmdbTrendingAsync(cfg.TmdbApiKey!,    mediaType, max, ct),
                "MDBList" when !string.IsNullOrEmpty(cfg.MdbListApiKey) => await MdbListTopAsync (cfg.MdbListApiKey!,  mediaType, max, ct),
                "Trakt"   when !string.IsNullOrEmpty(cfg.TraktApiKey)   => await TraktTrendingAsync(cfg.TraktApiKey!,  mediaType, max, ct),
                _                                                       => LocalTop10(user, mediaType, max)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "External Top10 failed, falling back to local");
            return LocalTop10(user, mediaType, max);
        }
    }

    private object[] LocalTop10(Jellyfin.Database.Implementations.Entities.User? user, string mediaType, int max)
    {
        var kind  = mediaType == "tv" ? BaseItemKind.Series : BaseItemKind.Movie;
        var query = user is not null
            ? new InternalItemsQuery(user)
              {
                  IncludeItemTypes = new[] { kind },
                  Recursive        = true,
                  Limit            = max,
                  OrderBy          = new[] { (ItemSortBy.CommunityRating, SortOrder.Descending) }
              }
            : new InternalItemsQuery
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
