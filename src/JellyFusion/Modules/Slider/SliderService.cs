using JellyFusion.Configuration;
using Jellyfin.Data.Enums;
// v3.0.12: SortOrder moved to this namespace in 10.11.
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;

namespace JellyFusion.Modules.Slider;

/// <summary>
/// Selects and returns the items to display in the Editor's Choice banner slider
/// based on the configured mode (Favourites / Random / Collections / New).
/// </summary>
public class SliderService
{
    private readonly ILibraryManager      _libraryManager;
    private readonly IUserManager         _userManager;
    private readonly ILogger<SliderService> _logger;

    public SliderService(
        ILibraryManager libraryManager,
        IUserManager userManager,
        ILogger<SliderService> logger)
    {
        _libraryManager = libraryManager;
        _userManager    = userManager;
        _logger         = logger;
    }

    /// <summary>Returns the list of items for the slider given current configuration.</summary>
    public async Task<IReadOnlyList<BaseItem>> GetSliderItemsAsync(
        SliderConfig cfg,
        Guid?        userId = null,
        CancellationToken ct = default)
    {
        if (!cfg.Enabled) return Array.Empty<BaseItem>();

        // v3.0.10: resolve user UP-FRONT so EVERY mode-specific query can
        // pass it into InternalItemsQuery. The HomeService got this fix in
        // v3.0.7 (its comment: "scope by user when we have one so the
        // library manager returns actual items instead of the empty result
        // it gives for unscoped queries in 10.10"). The same fix was never
        // propagated to SliderService - that's why /slider/items returned
        // [] even though /home/* worked. Without a user, _libraryManager
        // .GetItemsResult(query) returns 0 in 10.10 even with Recursive=true,
        // because the permission filter is per-user and an absent user
        // collapses to "see nothing".
        Jellyfin.Database.Implementations.Entities.User? user = null;
        if (userId.HasValue)
        {
            try { user = _userManager.GetUserById(userId.Value); }
            catch (Exception ex) { _logger.LogDebug(ex, "GetUserById failed, continuing without user"); }
        }
        if (user is null)
        {
            try
            {
                // v3.0.12: dropped the IsAdministrator filter. In Jellyfin
                // 10.11 the User entity removed HasPermission() (was
                // moved to extension or replaced with GetPermission). We
                // were only using it to PREFER an admin user as the
                // fallback - any user works for library queries, since
                // they get the union of their library permissions.
                user = _userManager.Users.FirstOrDefault();
                if (user is not null)
                {
                    _logger.LogDebug(
                        "SliderService: no userId provided, using first-user fallback {UserId}",
                        user.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "SliderService admin user fallback failed");
            }
        }

        try
        {
            var items = cfg.Mode switch
            {
                "Favourites"  => await GetFavouritesAsync(cfg, user, ct),
                "Collections" => await GetCollectionsAsync(cfg, user, ct),
                "New"         => await GetNewReleasesAsync(cfg, user, ct),
                _             => await GetRandomAsync(cfg, user, ct)  // "Random" = default
            };

            // Apply filters
            items = ApplyFilters(items, cfg);

            // v3.0.6: if the primary query returned nothing, fall back to an
            // unfiltered broad pull so the banner is NEVER empty on a fresh
            // install. This covers cases where Collections/Favourites/New all
            // produce zero items but the library actually has movies/series.
            if (items.Count == 0)
            {
                _logger.LogInformation(
                    "Slider mode {Mode} returned 0 items, falling back to unfiltered pull",
                    cfg.Mode);
                items = await GetFallbackAnyAsync(cfg, user, ct);
            }

            // Limit
            return items.Take(cfg.MaxItems).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building slider items");
            // Last-ditch: broad pull, no filters.
            try { return (await GetFallbackAnyAsync(cfg, user, ct)).Take(cfg.MaxItems).ToList(); }
            catch { return Array.Empty<BaseItem>(); }
        }
    }

    /// <summary>
    /// v3.0.6: unfiltered broad query used as fallback when the selected Mode
    /// produces no results. Pulls up to MaxItems*3 movies+series from the
    /// entire library with no filters, shuffles, and returns them. Designed
    /// to guarantee SOMETHING in the banner on any non-empty library.
    ///
    /// v3.0.10: takes a user (admin fallback resolved by caller) so the
    /// query is user-scoped. In Jellyfin 10.10 unscoped queries return
    /// empty even with Recursive=true.
    /// </summary>
    private Task<List<BaseItem>> GetFallbackAnyAsync(SliderConfig cfg, Jellyfin.Database.Implementations.Entities.User? user, CancellationToken ct)
    {
        var query = user is not null
            ? new InternalItemsQuery(user)
              {
                  IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
                  Recursive        = true,
                  Limit            = Math.Max(cfg.MaxItems * 3, 15)
              }
            : new InternalItemsQuery
              {
                  IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
                  Recursive        = true,
                  Limit            = Math.Max(cfg.MaxItems * 3, 15)
              };
        var list = _libraryManager.GetItemsResult(query).Items.ToList();
        if (list.Count == 0) return Task.FromResult(list);

        var rng = new Random();
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return Task.FromResult(list);
    }

    // ── Mode implementations ────────────────────────────────────

    private Task<List<BaseItem>> GetRandomAsync(SliderConfig cfg, Jellyfin.Database.Implementations.Entities.User? user, CancellationToken ct)
    {
        // v3.0.6: removed IsVirtualItem=false. On 10.10 that flag can
        // incorrectly exclude real items if the 'virtual' marker isn't
        // populated on the row, producing empty results on some libraries.
        // v3.0.10: pass user when available - unscoped queries return [] in 10.10.
        var query = user is not null
            ? new InternalItemsQuery(user)
              {
                  IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
                  Recursive        = true,
                  Limit            = Math.Max(cfg.MaxItems * 3, 15)
              }
            : new InternalItemsQuery
              {
                  IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
                  Recursive        = true,
                  Limit            = Math.Max(cfg.MaxItems * 3, 15)
              };

        var result = _libraryManager.GetItemsResult(query);
        var list   = result.Items.ToList();

        // Shuffle
        var rng = new Random();
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return Task.FromResult(list);
    }

    private async Task<List<BaseItem>> GetFavouritesAsync(
        SliderConfig cfg, Jellyfin.Database.Implementations.Entities.User? user, CancellationToken ct)
    {
        // v3.0.10: caller already resolved user (may be admin fallback).
        // For Favourites mode we still prefer the configured FavouritesUser
        // when set, falling back to whatever the caller resolved.
        if (!string.IsNullOrEmpty(cfg.FavouritesUser))
        {
            var named = _userManager.Users.FirstOrDefault(
                u => u.Username.Equals(cfg.FavouritesUser, StringComparison.OrdinalIgnoreCase));
            if (named is not null) user = named;
        }

        if (user is null)
        {
            _logger.LogWarning("Favourites mode: no user resolved, falling back to random");
            return await GetRandomAsync(cfg, user, ct);
        }

        var query = new InternalItemsQuery(user)
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
            IsFavorite       = true,
            Recursive        = true
        };

        return _libraryManager.GetItemsResult(query).Items.ToList();
    }

    private Task<List<BaseItem>> GetCollectionsAsync(SliderConfig cfg, Jellyfin.Database.Implementations.Entities.User? user, CancellationToken ct)
    {
        // v3.0.10: pass user when available - unscoped queries return [] in 10.10.
        var query = user is not null
            ? new InternalItemsQuery(user)
              {
                  IncludeItemTypes = new[] { BaseItemKind.BoxSet },
                  Recursive        = true
              }
            : new InternalItemsQuery
              {
                  IncludeItemTypes = new[] { BaseItemKind.BoxSet },
                  Recursive        = true
              };

        var collections = _libraryManager.GetItemsResult(query).Items.ToList();
        return Task.FromResult(collections.Cast<BaseItem>().ToList());
    }

    private Task<List<BaseItem>> GetNewReleasesAsync(SliderConfig cfg, Jellyfin.Database.Implementations.Entities.User? user, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-180); // last 6 months

        // v3.0.10: pass user when available - unscoped queries return [] in 10.10.
        var query = user is not null
            ? new InternalItemsQuery(user)
              {
                  IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
                  Recursive        = true,
                  MinPremiereDate  = cutoff,
                  // Jellyfin 10.10: OrderBy is a tuple array, not separate SortBy/SortOrder.
                  OrderBy          = new[] { (ItemSortBy.PremiereDate, SortOrder.Descending) }
              }
            : new InternalItemsQuery
              {
                  IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
                  Recursive        = true,
                  MinPremiereDate  = cutoff,
                  OrderBy          = new[] { (ItemSortBy.PremiereDate, SortOrder.Descending) }
              };

        return Task.FromResult(_libraryManager.GetItemsResult(query).Items.ToList());
    }

    // ── Filtering ───────────────────────────────────────────────

    private static List<BaseItem> ApplyFilters(List<BaseItem> items, SliderConfig cfg)
    {
        return items.Where(item =>
        {
            // Community rating
            if (cfg.MinCommunityRating > 0 &&
                (item.CommunityRating ?? 0) < cfg.MinCommunityRating)
                return false;

            // Critic rating
            if (cfg.MinCriticRating > 0 &&
                (item.CriticRating ?? 0) < cfg.MinCriticRating)
                return false;

            // Played filter
            // (PlayedState would require user-context; skip for now)

            return true;
        }).ToList();
    }
}
