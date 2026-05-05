using JellyFusion.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace JellyFusion;

/// <summary>
/// JellyFusion v2.0.0 — All-in-one Jellyfin enhancement plugin.
/// Combines Banner (Editor's Choice), Smart Tags (JellyTag), Studios,
/// Home rails, Themes, Navigation and Notifications into a single plugin.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    private readonly ILogger<Plugin> _logger;

    public Plugin(
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer,
        ILogger<Plugin> logger)
        : base(applicationPaths, xmlSerializer)
    {
        _logger = logger;
        Instance = this;
        _logger.LogInformation("JellyFusion v{Version} loaded", Version);

        // v3.0.8: one-shot seed of default lists (Rails, Studios, Nav,
        // BadgeOrder). See PluginConfiguration.HasBeenSeeded for why this
        // exists: XmlSerializer appends persisted items to property-
        // initializer defaults, which caused the "options multiplying"
        // bug. Now lists start empty and we seed defaults exactly once
        // per install.
        SeedDefaultsIfNeeded();
    }

    /// <summary>
    /// Populates empty default lists (Rails, Studios, Nav, BadgeOrder) and
    /// persists the config. No-op after the first successful run.
    ///
    /// Safety: only fills a list if it's still empty. If the user has
    /// already added any item, we leave the list alone - so the user's
    /// manual edits are never overwritten.
    /// </summary>
    private void SeedDefaultsIfNeeded()
    {
        try
        {
            var cfg = Configuration;
            if (cfg is null) return;
            if (cfg.HasBeenSeeded)
            {
                _logger.LogDebug("JellyFusion defaults already seeded, skipping");
                return;
            }

            // First: dedupe any already-duplicated lists from pre-v3.0.8
            // installs (see HasBeenSeeded docstring for the cause). This
            // is the ONLY time duplicates are removed automatically; after
            // the seed flag flips, user edits are respected as-is.
            if (cfg.Home?.Rails is { Count: > 0 })
            {
                var before = cfg.Home.Rails.Count;
                cfg.Home.Rails = cfg.Home.Rails
                    .Where(r => r is not null && !string.IsNullOrEmpty(r.Id))
                    .GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();
                if (cfg.Home.Rails.Count != before)
                    _logger.LogInformation("Deduped Rails: {Before} -> {After}", before, cfg.Home.Rails.Count);
            }
            if (cfg.Studios?.Items is { Count: > 0 })
            {
                var before = cfg.Studios.Items.Count;
                cfg.Studios.Items = cfg.Studios.Items
                    .Where(s => s is not null && !string.IsNullOrEmpty(s.Name))
                    .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();
                if (cfg.Studios.Items.Count != before)
                    _logger.LogInformation("Deduped Studios: {Before} -> {After}", before, cfg.Studios.Items.Count);
            }
            if (cfg.Navigation?.Items is { Count: > 0 })
            {
                var before = cfg.Navigation.Items.Count;
                cfg.Navigation.Items = cfg.Navigation.Items
                    .Where(n => n is not null && !string.IsNullOrEmpty(n.Id))
                    .GroupBy(n => n.Id, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();
                if (cfg.Navigation.Items.Count != before)
                    _logger.LogInformation("Deduped Navigation: {Before} -> {After}", before, cfg.Navigation.Items.Count);
            }
            if (cfg.Badges?.BadgeOrder is { Count: > 0 })
            {
                var before = cfg.Badges.BadgeOrder.Count;
                cfg.Badges.BadgeOrder = cfg.Badges.BadgeOrder
                    .Where(b => !string.IsNullOrEmpty(b))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (cfg.Badges.BadgeOrder.Count != before)
                    _logger.LogInformation("Deduped BadgeOrder: {Before} -> {After}", before, cfg.Badges.BadgeOrder.Count);
            }

            // Then: seed defaults for anything still empty.
            if (cfg.Home is not null && (cfg.Home.Rails is null || cfg.Home.Rails.Count == 0))
            {
                cfg.Home.Rails = HomeConfig.DefaultRails();
            }
            if (cfg.Studios is not null && (cfg.Studios.Items is null || cfg.Studios.Items.Count == 0))
            {
                cfg.Studios.Items = StudiosConfig.DefaultStudios();
            }
            if (cfg.Navigation is not null && (cfg.Navigation.Items is null || cfg.Navigation.Items.Count == 0))
            {
                cfg.Navigation.Items = NavigationConfig.DefaultItems();
            }
            if (cfg.Badges is not null && (cfg.Badges.BadgeOrder is null || cfg.Badges.BadgeOrder.Count == 0))
            {
                cfg.Badges.BadgeOrder = BadgesConfig.DefaultOrder();
            }

            // v3.0.8 defensive fix #1: persist BEFORE flipping the in-memory
            // flag. If SaveConfiguration throws (read-only filesystem,
            // permission error, plugin config dir not yet created on some
            // builds), we want HasBeenSeeded to stay false in memory so the
            // next plugin load tries again. Setting it before the save would
            // mean a transient failure is invisible to the user AND blocks
            // future seed attempts (since the in-memory true is what gets
            // checked).
            cfg.HasBeenSeeded = true;
            try
            {
                SaveConfiguration();
                _logger.LogInformation(
                    "JellyFusion first-run defaults seeded (Rails={Rails}, Studios={Studios}, Nav={Nav}, BadgeOrder={Badges})",
                    cfg.Home?.Rails?.Count ?? 0,
                    cfg.Studios?.Items?.Count ?? 0,
                    cfg.Navigation?.Items?.Count ?? 0,
                    cfg.Badges?.BadgeOrder?.Count ?? 0);
            }
            catch (Exception saveEx)
            {
                // Roll back the in-memory flag so a future plugin load
                // (after the disk problem is resolved) retries the seed.
                cfg.HasBeenSeeded = false;
                _logger.LogError(saveEx,
                    "SeedDefaultsIfNeeded: SaveConfiguration failed; rolling back HasBeenSeeded so seed retries on next load");
            }
        }
        catch (Exception ex)
        {
            // Never let seeding failure block plugin load.
            _logger.LogError(ex, "SeedDefaultsIfNeeded failed");
        }
    }

    /// <inheritdoc />
    public override string Name => "JellyFusion";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("b7c8d9e0-f1a2-3b4c-5d6e-7f8090a1b2c3");

    /// <inheritdoc />
    public override string Description =>
        "All-in-one Jellyfin plugin: Netflix-style banner with trailers, " +
        "smart badges (LAT/SUB/NUEVO/KID), configurable studios, " +
        "home rails (Top 10, Porque viste…), 7 themes, navigation shortcuts " +
        "and Discord/Telegram notifications. Multi-language UI (ES/EN/PT/FR).";

    /// <summary>Global singleton instance.</summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        // NOTE: CSS and JS are inlined into index.html so the single-page
        // delivery survives Jellyfin's plugin-page sanitizer in all setups.
        var prefix = GetType().Namespace + ".Web.";
        return new[]
        {
            new PluginPageInfo
            {
                Name                 = Name,
                EmbeddedResourcePath = prefix + "index.html",
                EnableInMainMenu     = true
            }
        };
    }
}

