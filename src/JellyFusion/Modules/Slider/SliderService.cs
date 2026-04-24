using JellyFusion.Configuration;
using Jellyfin.Data.Enums;
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

        try
        {
            var items = cfg.Mode switch
            {
                "Favourites"  => await GetFavouritesAsync(cfg, userId, ct),
                "Collections" => await GetCollectionsAsync(cfg, ct),
                "New"         => await GetNewReleasesAsync(cfg, ct),
                _             => await GetRandomAsync(cfg, ct)       // "Random" = default
            };

            // Apply filters
            items = ApplyFilters(items, cfg);

            // Limit
            return items.Take(cfg.MaxItems).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building slider items");
            return Array.Empty<BaseItem>();
        }
    }

    // ── Mode implementations ────────────────────────────────────

    private Task<List<BaseItem>> GetRandomAsync(SliderConfig cfg, CancellationToken ct)
    {
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
            Recursive        = true,
            IsVirtualItem    = false,
            Limit            = cfg.MaxItems * 3 // over-fetch before filtering
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
        SliderConfig cfg, Guid? userId, CancellationToken ct)
    {
        var user = userId.HasValue
            ? _userManager.GetUserById(userId.Value)
            : _userManager.Users.FirstOrDefault(
                u => u.Username.Equals(cfg.FavouritesUser, StringComparison.OrdinalIgnoreCase));

        if (user is null)
        {
            _logger.LogWarning("Favourites mode: user not found, falling back to random");
            return await GetRandomAsync(cfg, ct);
        }

        var query = new InternalItemsQuery(user)
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
            IsFavorite       = true,
            Recursive        = true
        };

        return _libraryManager.GetItemsResult(query).Items.ToList();
    }

    private Task<List<BaseItem>> GetCollectionsAsync(SliderConfig cfg, CancellationToken ct)
    {
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.BoxSet },
            Recursive        = true
        };

        var collections = _libraryManager.GetItemsResult(query).Items.ToList();
        return Task.FromResult(collections.Cast<BaseItem>().ToList());
    }

    private Task<List<BaseItem>> GetNewReleasesAsync(SliderConfig cfg, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-180); // last 6 months

        var query = new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
            Recursive        = true,
            MinPremiereDate  = cutoff,
            // Jellyfin 10.10: OrderBy is a tuple array, not separate SortBy/SortOrder.
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
