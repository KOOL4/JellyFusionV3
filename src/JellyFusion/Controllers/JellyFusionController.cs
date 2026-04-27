using JellyFusion.Configuration;
using JellyFusion.Modules.Badges;
using JellyFusion.Modules.Home;
using JellyFusion.Modules.Notifications;
using JellyFusion.Modules.Slider;
using JellyFusion.Modules.Studios;
using JellyFusion.Modules.Themes;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace JellyFusion.Controllers;

/// <summary>
/// REST API for the JellyFusion plugin.
/// Base route: /jellyfusion
///
/// v3.0.8 AUTH — back to the working model:
///   Initially (v3.0.6) the public endpoints were [AllowAnonymous] and
///   GetUserId() returned null, which made all user-scoped rails return
///   empty and the home show "only a banner".
///
///   v3.0.7 tried to fix that by switching the public endpoints to
///   [Authorize(Policy="DefaultAuthorization")] so User.Claims would be
///   populated. That BROKE the banner: the SPA fetches don't satisfy
///   that policy reliably from /web/ context, so the endpoints returned
///   401 and the browser showed "banner ERR" with nothing below it.
///
///   v3.0.8: go back to [AllowAnonymous] on the public endpoints AND
///   keep the GetUserId() admin fallback introduced in v3.0.7. The
///   fallback (first admin via IUserManager) resolves a user without
///   needing the auth pipeline to run - so user-scoped rails get
///   non-empty results AND the fetch never 401s.
///
///   Per-endpoint auth now:
///     - config/*,
///       badges/cache/*,
///       badges/custom/*,
///       notifications/test -> RequiresElevation (admin only)
///     - bootstrap.js,
///       theme/css, i18n,
///       home/rails,
///       slider/items,
///       slider/trailer,
///       studios,
///       badges/preview     -> AllowAnonymous (user resolved via
///                             GetUserId() admin fallback)
/// </summary>
[ApiController]
[Route("jellyfusion")]
public class JellyFusionController : ControllerBase
{
    private readonly SliderService          _slider;
    private readonly TrailerService         _trailer;
    private readonly ImageCacheService      _cache;
    private readonly StudiosService         _studios;
    private readonly ThemeService           _themes;
    private readonly NotificationService    _notif;
    private readonly HomeService            _home;
    private readonly LocalizationService    _i18n;
    private readonly IUserManager           _userManager;
    private readonly ILibraryManager        _library;
    private readonly ILogger<JellyFusionController> _logger;

    public JellyFusionController(
        SliderService slider,
        TrailerService trailer,
        ImageCacheService cache,
        StudiosService studios,
        ThemeService themes,
        NotificationService notif,
        HomeService home,
        LocalizationService i18n,
        IUserManager userManager,
        ILibraryManager library,
        ILogger<JellyFusionController> logger)
    {
        _slider      = slider;
        _trailer     = trailer;
        _cache       = cache;
        _studios     = studios;
        _themes      = themes;
        _notif       = notif;
        _home        = home;
        _i18n        = i18n;
        _userManager = userManager;
        _library     = library;
        _logger      = logger;
    }

    // ── Configuration ───────────────────────────────────────────

    /// <summary>GET current plugin configuration.</summary>
    [HttpGet("config")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<PluginConfiguration> GetConfig()
        => Ok(Plugin.Instance!.Configuration);

    /// <summary>POST — save full plugin configuration.</summary>
    [HttpPost("config")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult SaveConfig([FromBody] PluginConfiguration config)
    {
        Plugin.Instance!.UpdateConfiguration(config);
        _logger.LogInformation("JellyFusion configuration saved");
        return NoContent();
    }

    // ── Home rails ──────────────────────────────────────────────

    /// <summary>GET all enabled home rails resolved against the selected data source.</summary>
    [HttpGet("home/rails")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHomeRails(CancellationToken ct)
    {
        // v3.0.6: bulletproof - this endpoint must NEVER 500, or the browser
        // badge shows "rails ERR" and the user has no idea what happened.
        try
        {
            var rails = await _home.BuildRailsAsync(GetUserId(), ct);
            return Ok(rails);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetHomeRails outer failure");
            return Ok(Array.Empty<object>());
        }
    }

    // ── Slider ──────────────────────────────────────────────────

    /// <summary>GET slider items for the current user.</summary>
    [HttpGet("slider/items")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSliderItems(CancellationToken ct)
    {
        // v3.0.6: bulletproof - never 500. Empty array on any failure.
        try
        {
            var cfg    = Plugin.Instance?.Configuration?.Slider;
            if (cfg is null)
            {
                _logger.LogWarning("GetSliderItems: Slider config null");
                return Ok(Array.Empty<object>());
            }
            var userId = GetUserId();
            var items  = await _slider.GetSliderItemsAsync(cfg, userId, ct);

            _logger.LogInformation(
                "GetSliderItems: mode={Mode} userId={UserId} returned {Count} items",
                cfg.Mode, userId, items.Count);

            var itemDtos = items.Select(item => new
            {
                id          = item.Id,
                name        = item.Name,
                overview    = item.Overview,
                year        = item.ProductionYear,
                rating      = item.CommunityRating,
                imageUrl    = $"/Items/{item.Id}/Images/Backdrop",
                logoUrl     = $"/Items/{item.Id}/Images/Logo",
                posterUrl   = $"/Items/{item.Id}/Images/Primary",
                type        = item.GetType().Name
            }).ToList();

            // v3.0.13: response is now an object that ALSO carries the
            // PlatformStyle ("netflix" | "disney" | "prime" | "apple"),
            // so bootstrap.js can pick the matching visual layout
            // (Disney+ tabs / Apple TV+ feathered fullscreen / etc.)
            // without an extra round-trip to /jellyfusion/config.
            // Bootstrap normalises both shapes (raw array OR object) to
            // stay backward-compatible with v3.0.12 clients during a
            // staged rollout.
            return Ok(new
            {
                style            = string.IsNullOrEmpty(cfg.PlatformStyle) ? "netflix" : cfg.PlatformStyle.ToLowerInvariant(),
                autoplay         = cfg.AutoplayEnabled,
                autoplayInterval = cfg.AutoplayInterval,
                heading          = cfg.BannerHeading,
                showPlayButton   = cfg.ShowPlayButton,
                playButtonText   = cfg.CustomPlayButtonText,
                showRating       = cfg.ShowCommunityRating,
                showDescription  = cfg.ShowDescription,
                // v3.0.17: surface BannerHeight (Small|Medium|Large|Fullscreen)
                // so bootstrap.js applies the matching CSS preset and the
                // admin's choice in the config tab is actually respected.
                height           = string.IsNullOrEmpty(cfg.BannerHeight) ? "Large" : cfg.BannerHeight,
                items            = itemDtos
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetSliderItems outer failure");
            return Ok(Array.Empty<object>());
        }
    }

    /// <summary>GET trailer URL for a specific item.</summary>
    [HttpGet("slider/trailer/{itemId}")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTrailer(Guid itemId, CancellationToken ct)
    {
        // v3.0.8 defensive fix #3: mirror the bulletproof outer try/catch
        // pattern used by every other public endpoint. Without it, an NRE
        // on Plugin.Instance!.Configuration.Slider during a transient
        // null-config window (or a TrailerService exception) would surface
        // as a 500, breaking the play button on the banner.
        try
        {
            var cfg = Plugin.Instance?.Configuration?.Slider;
            if (cfg is null)
            {
                _logger.LogWarning("GetTrailer: Slider config null");
                return NotFound(new { message = "No trailer config" });
            }

            var item = _library.GetItemById(itemId);
            if (item is null) return NotFound();

            // v3.0.20: respond with both the YouTube KEY and a full URL.
            // Bootstrap.js uses the key to build a YouTube IFrame embed
            // (https://www.youtube.com/embed/<key>?autoplay=1&mute=1&...)
            // for the banner trailer overlay. Older callers that read
            // `url` keep working unchanged.
            var key = await _trailer.GetTrailerKeyAsync(item, cfg, ct);
            if (string.IsNullOrEmpty(key)) return NotFound(new { message = "No trailer found" });

            // v3.0.22: switched to youtube-nocookie.com — same player,
            // way fewer "Embedding restricted" (Error 153) responses
            // because the privacy-enhanced domain has fewer geographic
            // and consent gates. Bootstrap.js will append &origin=...
            // client-side so the iframe Same-Origin policy is satisfied
            // (server has no idea what origin the request came from).
            return Ok(new
            {
                key,
                url = $"https://www.youtube.com/watch?v={key}",
                embedUrl = $"https://www.youtube-nocookie.com/embed/{key}" +
                           "?autoplay=1&mute=1&controls=0&modestbranding=1" +
                           "&playsinline=1&rel=0&loop=1&iv_load_policy=3" +
                           "&fs=0&disablekb=1&enablejsapi=1&playlist=" + key
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetTrailer outer failure for item {ItemId}", itemId);
            return NotFound(new { message = "Trailer lookup failed" });
        }
    }

    // ── Badges ──────────────────────────────────────────────────

    /// <summary>GET preview image with badges for a specific item (used by Live Preview).</summary>
    [HttpGet("badges/preview/{itemId}")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetBadgePreview(Guid itemId)
    {
        return Ok(new { itemId, message = "Preview rendered via middleware on /Items/{itemId}/Images/Primary" });
    }

    /// <summary>POST — clear the badge image cache.</summary>
    [HttpPost("badges/cache/clear")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult ClearCache()
    {
        _cache.ClearAll();
        return NoContent();
    }

    /// <summary>GET badge cache statistics.</summary>
    [HttpGet("badges/cache/stats")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetCacheStats()
    {
        var (files, bytes, oldest) = _cache.GetStats();
        return Ok(new
        {
            files,
            sizeBytes = bytes,
            sizeMb    = Math.Round(bytes / 1_048_576.0, 2),
            oldest    = oldest?.ToString("g")
        });
    }

    /// <summary>POST — upload a custom badge image.</summary>
    [HttpPost("badges/custom/{badgeKey}")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UploadCustomBadge(string badgeKey, IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest("No file provided");

        var allowed = new[] { "image/svg+xml", "image/png", "image/jpeg" };
        if (!allowed.Contains(file.ContentType))
            return BadRequest("Only SVG, PNG and JPEG are allowed");

        var dir  = Path.Combine(Plugin.Instance!.DataFolderPath, "custom-badges");
        Directory.CreateDirectory(dir);
        var ext  = Path.GetExtension(file.FileName);
        var path = Path.Combine(dir, $"{badgeKey}{ext}");

        using var stream = System.IO.File.Create(path);
        await file.CopyToAsync(stream);

        _cache.ClearAll(); // invalidate so new badges render
        return NoContent();
    }

    /// <summary>DELETE — revert a custom badge to default.</summary>
    [HttpDelete("badges/custom/{badgeKey}")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult DeleteCustomBadge(string badgeKey)
    {
        var dir = Path.Combine(Plugin.Instance!.DataFolderPath, "custom-badges");
        foreach (var ext in new[] { ".svg", ".png", ".jpg", ".jpeg" })
        {
            var path = Path.Combine(dir, $"{badgeKey}{ext}");
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
        _cache.ClearAll();
        return NoContent();
    }

    // ── Studios ─────────────────────────────────────────────────

    /// <summary>GET all configured studios with their browse URLs.</summary>
    [HttpGet("studios")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetStudios()
    {
        var cfg = Plugin.Instance!.Configuration.Studios;
        var result = cfg.Items
            .OrderBy(s => s.SortOrder)
            .Select(s => new
            {
                s.Name,
                s.LogoUrl,
                s.Gradient,
                s.Invert,
                s.Tags,
                browseUrl  = StudiosService.GetStudioBrowseUrl(s),
                itemCount  = _studios.GetItemCountForStudio(s.Name)
            });
        return Ok(result);
    }

    // ── Themes ──────────────────────────────────────────────────

    /// <summary>
    /// GET CSS for the active theme (injected by client script).
    /// v3.0.17 — accepts an optional <c>?theme=X</c> override so the
    /// admin page can request a live preview of any built-in theme
    /// (Netflix / Disney+ / Prime / Apple TV+ / Crunchyroll / Paramount+)
    /// without saving and reloading. The override only changes
    /// ActiveTheme; PrimaryColor, BackgroundColor and FontFamily still
    /// come from the persisted config so the admin keeps any custom
    /// tweaks they made on top of the preset.
    /// </summary>
    [HttpGet("theme/css")]
    [AllowAnonymous]
    [Produces("text/css")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetThemeCss([FromQuery] string? theme)
    {
        var cfg = Plugin.Instance!.Configuration.Theme;
        if (!string.IsNullOrEmpty(theme))
        {
            // Shallow clone so the override doesn't mutate the persisted
            // config in memory.
            cfg = new Configuration.ThemeConfig
            {
                ActiveTheme     = theme,
                PrimaryColor    = cfg.PrimaryColor,
                BackgroundColor = cfg.BackgroundColor,
                FontFamily      = cfg.FontFamily
            };
        }
        var css = _themes.GetThemeCss(cfg);
        // No-cache so consecutive ?theme= requests always return fresh CSS.
        Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        return Content(css, "text/css");
    }

    // ── Notifications ────────────────────────────────────────────

    /// <summary>POST — send a test notification to Discord or Telegram.</summary>
    [HttpPost("notifications/test/{channel}")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> TestNotification(string channel, CancellationToken ct)
    {
        try
        {
            await _notif.SendTestAsync(channel, ct);
            return NoContent();
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    // ── Client-side bootstrap ───────────────────────────────────

    /// <summary>
    /// GET /jellyfusion/bootstrap.js — the client-side loader injected by
    /// ClientScriptInjectionMiddleware into Jellyfin's /web/index.html.
    /// Must be anonymous because the injection target is the unauthenticated
    /// login/home shell.
    /// </summary>
    [HttpGet("bootstrap.js")]
    [AllowAnonymous]
    [Produces("application/javascript")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetBootstrapScript()
    {
        var asm          = typeof(Plugin).Assembly;
        var resourceName = typeof(Plugin).Namespace + ".Web.bootstrap.js";
        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            _logger.LogWarning("Embedded resource not found: {Resource}", resourceName);
            return NotFound();
        }
        using var reader = new StreamReader(stream);
        var js = reader.ReadToEnd();
        // Short cache so admins don't have to hard-refresh after every plugin
        // update during development, but clients still pick up new bootstraps.
        Response.Headers["Cache-Control"] = "public, max-age=300";
        return Content(js, "application/javascript");
    }

    // ── Localization ─────────────────────────────────────────────

    /// <summary>GET all localization strings for the requested (or current) language.</summary>
    [HttpGet("i18n")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetI18n([FromQuery] string? lang)
    {
        var language = lang ?? Plugin.Instance?.Configuration?.Language ?? "es";
        var strings  = _i18n.GetAllForLanguage(language);
        return Ok(new { language, strings });
    }

    // ── Navigation (top-bar replacement) ────────────────────────

    /// <summary>
    /// GET /jellyfusion/navigation — returns the JellyFusion-managed top-bar
    /// shortcuts already RESOLVED to actual Jellyfin URLs. Bootstrap.js calls
    /// this to render its own top nav (replacing the stock "Inicio / Favoritos"
    /// row). Each item carries:
    ///   - id     : stable identifier ("home", "movies", "series", ...)
    ///   - label  : display text in the user's configured language
    ///   - url    : the SPA hash to navigate to when clicked
    /// </summary>
    [HttpGet("navigation")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetNavigation()
    {
        try
        {
            var navCfg = Plugin.Instance?.Configuration?.Navigation;
            if (navCfg is null) return Ok(Array.Empty<object>());

            var rawItems = (navCfg.Items?.Count ?? 0) > 0
                ? navCfg.Items!
                : Configuration.NavigationConfig.DefaultItems();

            // Resolve library Ids on demand. We grab CollectionFolder items
            // (the user-visible library roots) and look them up by name in
            // both English and Spanish to be friendly to non-EN servers.
            var libs = _library.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
            {
                IncludeItemTypes = new[] { Jellyfin.Data.Enums.BaseItemKind.CollectionFolder },
                Recursive        = false
            });

            // v3.0.15: build URL from the library's CollectionType
            // (movies/tvshows/music/...) so the SPA opens it the same
            // way the stock "Mis medios" cards do. Falls back to the
            // generic list view if type is unknown. We also normalise
            // names with Unicode-insensitive comparison so "Películas"
            // (with NFC accent) and "Peliculas" (no accent) both match.
            static string Normalise(string s) => string.IsNullOrEmpty(s)
                ? string.Empty
                : new string(s.Normalize(System.Text.NormalizationForm.FormD)
                    .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                        != System.Globalization.UnicodeCategory.NonSpacingMark)
                    .ToArray()).ToLowerInvariant();

            string BuildLibraryUrl(MediaBrowser.Controller.Entities.BaseItem lib)
            {
                // CollectionFolder.CollectionType is "movies" | "tvshows" |
                // "music" | "boxsets" | "homevideos" | "books" | "" (mixed).
                string? colType = null;
                if (lib is MediaBrowser.Controller.Entities.CollectionFolder cf)
                    colType = cf.CollectionType?.ToString().ToLowerInvariant();

                string id  = lib.Id.ToString("N");
                string sid = (lib.GetType().GetProperty("ServerId")?.GetValue(lib) as string) ?? "";

                // Modern Jellyfin web (10.10+) routes:
                //   movies-typed     -> #/movies.html?topParentId=GUID
                //   tvshows-typed    -> #/tv.html?topParentId=GUID
                //   anything else    -> #/list.html?parentId=GUID
                string suffix = !string.IsNullOrEmpty(sid)
                    ? $"&serverId={sid}"
                    : "";
                return colType switch
                {
                    "movies"   => $"#/movies.html?topParentId={id}{suffix}",
                    "tvshows"  => $"#/tv.html?topParentId={id}{suffix}",
                    "music"    => $"#/music.html?topParentId={id}{suffix}",
                    _          => $"#/list.html?parentId={id}{suffix}"
                };
            }

            string? FindLibUrl(string idHint, params string[] candidateNames)
            {
                // (1) Try by collection type first (most reliable: works
                //     regardless of what the user named the library).
                MediaBrowser.Controller.Entities.BaseItem? hit = null;
                string targetType = idHint switch
                {
                    "movies" => "movies",
                    "series" => "tvshows",
                    "music"  => "music",
                    _        => ""
                };
                if (!string.IsNullOrEmpty(targetType))
                {
                    hit = libs.OfType<MediaBrowser.Controller.Entities.CollectionFolder>()
                              .FirstOrDefault(cf =>
                                  string.Equals(cf.CollectionType?.ToString(), targetType,
                                      StringComparison.OrdinalIgnoreCase));
                }

                // (2) Fall back to fuzzy name match (accent-insensitive).
                if (hit is null)
                {
                    var normalisedTargets = candidateNames.Select(Normalise).ToArray();
                    hit = libs.FirstOrDefault(l =>
                        normalisedTargets.Any(t => Normalise(l.Name) == t));
                }

                return hit is not null ? BuildLibraryUrl(hit) : null;
            }

            string LabelFor(Configuration.NavItem n)
            {
                // LabelKey may be a translation key like "nav.home". We try
                // localization first, fall back to a sensible Spanish default
                // matching the Disney+ tab look the user requested.
                if (!string.IsNullOrEmpty(n.LabelKey))
                {
                    try
                    {
                        var lang = Plugin.Instance?.Configuration?.Language ?? "es";
                        var dict = _i18n.GetAllForLanguage(lang);
                        if (dict.TryGetValue(n.LabelKey, out var s) && !string.IsNullOrEmpty(s))
                            return s;
                    }
                    catch { /* fall through */ }
                }
                return n.Id switch
                {
                    "home"      => "Para ti",
                    "movies"    => "Películas",
                    "series"    => "Series",
                    "studios"   => "Estudios",
                    "favorites" => "Favoritos",
                    "live"      => "TV en vivo",
                    _           => n.Id
                };
            }

            string ResolveUrl(Configuration.NavItem n)
            {
                if (!string.IsNullOrEmpty(n.Url))
                    return n.Url!;
                return n.Id switch
                {
                    "home"      => "#/home.html",
                    "favorites" => "#/home.html?tab=1",
                    "movies"    => FindLibUrl("movies", "Películas", "Peliculas", "Movies", "Pelis", "Cine") ?? "#/home.html",
                    "series"    => FindLibUrl("series", "Series", "TV Shows", "Shows") ?? "#/home.html",
                    "music"     => FindLibUrl("music",  "Música",   "Musica",   "Music") ?? "#/home.html",
                    "studios"   => "#/home.html",
                    "live"      => "#/livetv.html",
                    _           => "#/home.html"
                };
            }

            var resolved = rawItems.Select(n => new
            {
                id    = n.Id,
                label = LabelFor(n),
                icon  = n.Icon,
                url   = ResolveUrl(n)
            }).ToList();

            // v3.0.15: response now wraps the items in an object so we
            // can also surface UI flags the bootstrap needs.
            // v3.0.17: hideStockMyMedia is now driven by the existing
            // Navigation.ReplaceMyContent toggle (the one labelled
            // "Sustituir Mi Contenido" in the admin Navigación tab).
            // We OR it with Home.HideStockMyMedia so users who already
            // flipped the v3.0.15 flag don't lose their setting.
            // Bootstrap normalises raw-array AND object shapes for
            // backwards compatibility with v3.0.14 cached scripts.
            var hideStock =
                (Plugin.Instance?.Configuration?.Navigation?.ReplaceMyContent ?? false) ||
                (Plugin.Instance?.Configuration?.Home?.HideStockMyMedia ?? false);
            return Ok(new
            {
                hideStockMyMedia = hideStock,
                items            = resolved
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetNavigation failure");
            return Ok(Array.Empty<object>());
        }
    }

    // ── Helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Best-effort user id resolution.
    ///
    /// v3.0.7: the previous implementation only checked
    /// ClaimTypes.NameIdentifier and - when that returned null -
    /// cascaded silently into "all user-scoped rails return empty".
    /// That's what made the home screen show ONLY a banner in v3.0.6.
    ///
    /// Strategy:
    ///   1. Try the standard NameIdentifier claim.
    ///   2. Try Jellyfin's custom "Jellyfin-UserId" claim (some auth
    ///      handlers set this instead).
    ///   3. Try "sub" (JWT style) and "userid" (Emby-inherited).
    ///   4. Fall back to the first administrator on the server so the
    ///      rails have SOMETHING to query against even when the client
    ///      somehow didn't carry a user token (e.g. on a cold boot
    ///      after Save when the page re-fetches before login re-ack).
    /// </summary>
    private Guid? GetUserId()
    {
        string?[] candidates =
        {
            User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            User.FindFirst("Jellyfin-UserId")?.Value,
            User.FindFirst("sub")?.Value,
            User.FindFirst("userid")?.Value
        };

        foreach (var c in candidates)
        {
            if (Guid.TryParse(c, out var id) && id != Guid.Empty)
                return id;
        }

        // Fallback: first administrator. Without this, a browser that
        // reached the endpoint without a populated auth context would
        // get rails with items:[] on every per-user rail, and bootstrap
        // would skip them all - exactly the v3.0.6 regression.
        try
        {
            // v3.0.12: dropped IsAdministrator filter. User.HasPermission
            // was removed in Jellyfin 10.11; any user works for library
            // queries (they get the union of their library permissions).
            var admin = _userManager.Users.FirstOrDefault();
            if (admin is not null)
            {
                _logger.LogDebug(
                    "GetUserId: no claim found, falling back to first user {UserId}",
                    admin.Id);
                return admin.Id;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "GetUserId admin fallback failed");
        }

        return null;
    }
}
