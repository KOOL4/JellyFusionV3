using JellyFusion.Modules.Badges;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace JellyFusion.Middleware;

/// <summary>
/// ASP.NET Core middleware that intercepts Jellyfin image requests and composites
/// JellyTag badges onto posters and thumbnails server-side.
/// Works for ALL Jellyfin clients without any client-side configuration.
/// </summary>
public class BadgeMiddleware : IMiddleware
{
    private readonly BadgeService         _badgeService;
    private readonly BadgeRenderService   _renderService;
    private readonly ImageCacheService    _cache;
    private readonly ILogger<BadgeMiddleware> _logger;

    // Matches /Items/{id}/Images/Primary and /Items/{id}/Images/Thumb
    private static readonly System.Text.RegularExpressions.Regex ImagePathRegex =
        new(@"/Items/([0-9a-f-]+)/Images/(Primary|Thumb|Backdrop)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.Compiled);

    public BadgeMiddleware(
        BadgeService badgeService,
        BadgeRenderService renderService,
        ImageCacheService cache,
        ILogger<BadgeMiddleware> logger)
    {
        _badgeService  = badgeService;
        _renderService = renderService;
        _cache         = cache;
        _logger        = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var cfg = Plugin.Instance?.Configuration?.Badges;
        if (cfg is null || !cfg.Enabled)
        {
            await next(context);
            return;
        }

        var path  = context.Request.Path.Value ?? "";
        var match = ImagePathRegex.Match(path);

        if (!match.Success)
        {
            await next(context);
            return;
        }

        string itemIdStr  = match.Groups[1].Value;
        string imageType  = match.Groups[2].Value;
        bool   isThumb    = imageType.Equals("Thumb", StringComparison.OrdinalIgnoreCase);

        if (!Guid.TryParse(itemIdStr, out var itemId))
        {
            await next(context);
            return;
        }

        // Check cache first — pass the configured TTL so disk expiry matches memory expiry.
        var cacheTtl  = TimeSpan.FromHours(cfg.CacheDurationHours);
        string cacheKey = $"{itemId}_{imageType}_{cfg.GetHashCode()}";
        var cached = _cache.Get(cacheKey, cacheTtl);

        if (cached is not null)
        {
            await WriteImageResponse(context, cached, cfg.OutputFormat);
            return;
        }

        // Capture the upstream response
        var originalBody = context.Response.Body;
        using var memStream = new MemoryStream();
        context.Response.Body = memStream;

        await next(context);

        memStream.Seek(0, SeekOrigin.Begin);
        byte[] originalBytes = memStream.ToArray();

        // Try to render badges
        var item = _badgeService.GetItem(itemId);
        if (item is not null && originalBytes.Length > 0)
        {
            try
            {
                var rendered = _renderService.RenderBadges(originalBytes, item, cfg, isThumb);
                if (rendered is not null)
                {
                    _cache.Set(cacheKey, rendered, TimeSpan.FromHours(cfg.CacheDurationHours));
                    context.Response.Body = originalBody;
                    await WriteImageResponse(context, rendered, cfg.OutputFormat);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Badge render failed for {ItemId}, serving original", itemId);
            }
        }

        // Fall back to original image
        context.Response.Body = originalBody;
        await originalBody.WriteAsync(originalBytes);
    }

    private static async Task WriteImageResponse(HttpContext ctx, byte[] data, string fmt)
    {
        string mime = fmt switch
        {
            "PNG"  => "image/png",
            "WebP" => "image/webp",
            _      => "image/jpeg"
        };
        ctx.Response.ContentType   = mime;
        ctx.Response.ContentLength = data.Length;
        await ctx.Response.Body.WriteAsync(data);
    }
}
