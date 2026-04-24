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

