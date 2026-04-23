using JellyFusion.Configuration;
using JellyFusion.Modules.Badges;
using JellyFusion.Modules.Home;
using JellyFusion.Modules.Notifications;
using JellyFusion.Modules.Slider;
using JellyFusion.Modules.Studios;
using JellyFusion.Modules.Themes;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace JellyFusion.Controllers;

/// <summary>
/// REST API for the JellyFusion plugin.
/// Base route: /jellyfusion
/// </summary>
[ApiController]
[Route("jellyfusion")]
[Authorize(Policy = "RequiresElevation")]
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
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<PluginConfiguration> GetConfig()
        => Ok(Plugin.Instance!.Configuration);

    /// <summary>POST — save full plugin configuration.</summary>
    [HttpPost("config")]
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
        var rails = await _home.BuildRailsAsync(GetUserId(), ct);
        return Ok(rails);
    }

    // ── Slider ──────────────────────────────────────────────────

    /// <summary>GET slider items for the current user.</summary>
    [HttpGet("slider/items")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSliderItems(CancellationToken ct)
    {
        var cfg    = Plugin.Instance!.Configuration.Slider;
        var userId = GetUserId();
        var items  = await _slider.GetSliderItemsAsync(cfg, userId, ct);

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

    /// <summary>GET trailer URL for a specific item.</summary>
    [HttpGet("slider/trailer/{itemId}")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTrailer(Guid itemId, CancellationToken ct)
    {
        var cfg  = Plugin.Instance!.Configuration.Slider;
        var item = _library.GetItemById(itemId);
        if (item is null) return NotFound();

        var url = await _trailer.GetTrailerUrlAsync(item, cfg, ct);
        if (url is null) return NotFound(new { message = "No trailer found" });

        return Ok(new { url });
    }

    // ── Badges ──────────────────────────────────────────────────

    /// <summary>GET preview image with badges for a specific item (used by Live Preview).</summary>
    [HttpGet("badges/preview/{itemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetBadgePreview(Guid itemId)
    {
        return Ok(new { itemId, message = "Preview rendered via middleware on /Items/{itemId}/Images/Primary" });
    }

    /// <summary>POST — clear the badge image cache.</summary>
    [HttpPost("badges/cache/clear")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult ClearCache()
    {
        _cache.ClearAll();
        return NoContent();
    }

    /// <summary>GET badge cache statistics.</summary>
    [HttpGet("badges/cache/stats")]
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

    private Guid? GetUserId()
    {
        var claim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}
