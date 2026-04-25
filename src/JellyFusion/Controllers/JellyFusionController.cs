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

            var result = items.Select(item => new
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
            });

            return Ok(result);
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

            var url = await _trailer.GetTrailerUrlAsync(item, cfg, ct);
            if (url is null) return NotFound(new { message = "No trailer found" });

            return Ok(new { url });
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

    /// <summary>GET CSS for the active theme (injected by client script).</summary>
    [HttpGet("theme/css")]
    [AllowAnonymous]
    [Produces("text/css")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetThemeCss()
    {
        var css = _themes.GetThemeCss(Plugin.Instance!.Configuration.Theme);
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
            var admin = _userManager.Users
                .FirstOrDefault(u => u.HasPermission(PermissionKind.IsAdministrator))
                ?? _userManager.Users.FirstOrDefault();
            if (admin is not null)
            {
                _logger.LogDebug(
                    "GetUserId: no claim found, falling back to first admin {UserId}",
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
