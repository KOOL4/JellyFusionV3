using JellyFusion.Configuration;
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

    // Matches /Items/{id}/Images/Primary and /Items/{id}/Images/Thumb.
    //
    // v3.0.7 regex note: Jellyfin emits GUIDs in BOTH canonical formats:
    //   - "/Items/abcd1234efab5678.../Images/Primary" (32-char no hyphens, most clients)
    //   - "/Items/abcd1234-efab-5678-...-............/Images/Primary" (hyphenated, rare)
    // The old class `[0-9a-f-]+` technically accepted both because `-` was
    // inside the char class, but it would also match pathological inputs
    // like `/Items/---/Images/Primary`. Tightening the pattern to exactly
    // 32 hex chars with optional hyphens avoids ambiguity and matches
    // what `Guid.TryParse` accepts.
    private static readonly System.Text.RegularExpressions.Regex ImagePathRegex =
        new(@"/Items/([0-9a-fA-F]{8}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{12})/Images/(Primary|Thumb|Backdrop)",
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

        // v3.0.7: log at Info on first hit so admins can verify the middleware
        // is actually wired into the pipeline. The v3.0.6 symptom "badges
        // never show up" could be caused either by the middleware never
        // firing (IStartupFilter not picked up) OR by the compositing failing
        // silently. This log disambiguates.
        _logger.LogDebug("BadgeMiddleware hit: {Path} (item {ItemId})", path, itemId);

        // Check cache first — pass the configured TTL so disk expiry matches memory expiry.
        //
        // v3.0.8 defensive fix #4: cache key was `{itemId}_{type}_{cfg.GetHashCode()}`.
        // BadgesConfig has no overridden GetHashCode, so the default reference
        // hash was used — a fresh hash on every Jellyfin restart, which made
        // the disk cache effectively single-session. Files orphaned, first
        // request after restart was always slow, and ImageCacheService size
        // grew unbounded. New key uses a stable composition of the fields
        // that actually affect the rendered output.
        var cacheTtl  = TimeSpan.FromHours(cfg.CacheDurationHours);
        string cacheKey = BuildCacheKey(itemId.ToString("N"), imageType, cfg);
        var cached = _cache.Get(cacheKey, cacheTtl);

        if (cached is not null)
        {
            await WriteImageResponse(context, cached, cfg.OutputFormat);
            return;
        }

        // Capture the upstream response.
        //
        // v3.0.7 IMPORTANT: previous version did NOT restore
        // `context.Response.Body = originalBody` on every path, so when the
        // item-lookup failed or the render returned null we'd leave the body
        // pointing at a disposed MemoryStream and kestrel would write the
        // fallback bytes to a closed stream (silently no-op, user gets an
        // empty image). try/finally guarantees the body is restored before
        // ANY return path, and the compositing path explicitly clears any
        // Content-Length that upstream set for the ORIGINAL image size -
        // otherwise browsers would truncate the composited-larger response.
        var originalBody   = context.Response.Body;
        byte[] originalBytes = Array.Empty<byte>();
        try
        {
            using var memStream = new MemoryStream();
            context.Response.Body = memStream;

            await next(context);

            memStream.Seek(0, SeekOrigin.Begin);
            originalBytes = memStream.ToArray();

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
                        // Clear Content-Length set by upstream so WriteImageResponse
                        // can set the one that matches the composited bytes.
                        context.Response.Headers.Remove("Content-Length");
                        await WriteImageResponse(context, rendered, cfg.OutputFormat);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Badge render failed for {ItemId}, serving original", itemId);
                }
            }
        }
        finally
        {
            // Always restore so a later middleware isn't writing to a
            // disposed MemoryStream. If we already restored in the success
            // path this is a no-op.
            if (!ReferenceEquals(context.Response.Body, originalBody))
            {
                context.Response.Body = originalBody;
            }
        }

        // Fall back to original image.
        if (originalBytes.Length > 0)
        {
            await originalBody.WriteAsync(originalBytes);
        }
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

    /// <summary>
    /// v3.0.8: build a STABLE cache key from BadgesConfig. The previous
    /// `cfg.GetHashCode()` returned the default reference hash, which is
    /// fresh on every restart, so disk-cache hits were impossible across
    /// restarts. We now compose a deterministic string from the fields
    /// that actually change the rendered output: enabled flags, badge
    /// order, output format, language and status badge styling, and
    /// padding settings. Any field NOT in this string is intentionally
    /// excluded (e.g. CacheDurationHours: changing TTL shouldn't
    /// invalidate the bytes already on disk).
    /// </summary>
    private static string BuildCacheKey(string itemId, string imageType, BadgesConfig cfg)
    {
        var sb = new System.Text.StringBuilder(192);
        sb.Append(itemId).Append('_').Append(imageType).Append('_');
        sb.Append(cfg.Enabled ? '1' : '0');
        sb.Append(cfg.EnableOnPosters ? '1' : '0');
        sb.Append(cfg.EnableOnThumbs ? '1' : '0');
        sb.Append(cfg.ThumbSameAsPoster ? '1' : '0');
        sb.Append(cfg.ThumbSizeReduction).Append('_');
        sb.Append(cfg.OutputFormat).Append(cfg.JpegQuality).Append('_');
        if (cfg.BadgeOrder is not null)
        {
            foreach (var b in cfg.BadgeOrder)
            {
                sb.Append(b).Append('|');
            }
        }
        if (cfg.Language is not null)
        {
            sb.Append('L');
            sb.Append(cfg.Language.Enabled ? '1' : '0');
            sb.Append(cfg.Language.LatinFlagStyle).Append(cfg.Language.LatinText);
            sb.Append(cfg.Language.SubText).Append(cfg.Language.SubThreshold);
            sb.Append(cfg.Language.Position).Append(cfg.Language.Layout);
            sb.Append(cfg.Language.Gap).Append(cfg.Language.BadgeSize).Append(cfg.Language.Margin);
            sb.Append(cfg.Language.BadgeStyle);
        }
        if (cfg.Status is not null)
        {
            sb.Append('S');
            sb.Append(cfg.Status.NewEnabled ? '1' : '0');
            sb.Append(cfg.Status.NewDaysThreshold).Append(cfg.Status.NewText);
            sb.Append(cfg.Status.NewBgColor).Append(cfg.Status.NewTextColor);
            sb.Append(cfg.Status.KidEnabled ? '1' : '0');
            sb.Append(cfg.Status.KidText).Append(cfg.Status.KidBgColor).Append(cfg.Status.KidTextColor);
            sb.Append(cfg.Status.KidDetectionMode);
        }

        // v3.0.18: per-category style configs - mix into the cache key
        // so changing a position/gap/size/margin invalidates the cached
        // composited image and the user sees the new layout right away.
        AppendStyle(sb, "ls", cfg.LanguageStyle);
        AppendStyle(sb, "rs", cfg.ResolutionStyle);
        AppendStyle(sb, "as", cfg.AudioStyle);
        AppendStyle(sb, "hs", cfg.HdrStyle);

        // Hash to a short fixed-length suffix so cache filenames stay tidy.
        var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        var hash  = System.Security.Cryptography.SHA256.HashData(bytes);
        var suffix = Convert.ToHexString(hash, 0, 8);
        return $"{itemId}_{imageType}_{suffix}";
    }

    /// <summary>
    /// v3.0.18 cache-key helper for the new per-category style configs.
    /// </summary>
    private static void AppendStyle(System.Text.StringBuilder sb, string tag, BadgeStyleConfig? style)
    {
        if (style is null) return;
        sb.Append(tag);
        sb.Append(style.Enabled ? '1' : '0');
        sb.Append(style.Position).Append(style.Layout);
        sb.Append(style.Gap).Append(style.BadgeSize).Append(style.Margin);
    }
}
