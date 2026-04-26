using JellyFusion.Configuration;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace JellyFusion.Modules.Badges;

/// <summary>
/// Composites quality badges onto Jellyfin poster/thumbnail images server-side.
/// Supports Resolution, HDR, Codec, Audio, Language (with LAT/SUB logic),
/// and Status (NUEVO / KID) badges.
/// </summary>
public class BadgeRenderService
{
    private readonly ILogger<BadgeRenderService> _logger;
    private readonly ImageCacheService _cache;
    private readonly IMediaSourceManager _mediaSourceManager;

    // Known Latin-Spanish audio language codes
    private static readonly HashSet<string> LatinSpanishCodes =
        new(StringComparer.OrdinalIgnoreCase) { "es-419", "es-MX", "es-AR", "es-CO", "spa-419" };

    // Kid-friendly parental ratings
    private static readonly HashSet<string> KidRatings =
        new(StringComparer.OrdinalIgnoreCase) { "G", "TV-Y", "TV-Y7", "TV-G", "PG", "NR" };

    public BadgeRenderService(
        ILogger<BadgeRenderService> logger,
        ImageCacheService cache,
        IMediaSourceManager mediaSourceManager)
    {
        _logger             = logger;
        _cache              = cache;
        _mediaSourceManager = mediaSourceManager;
    }

    /// <summary>
    /// v3.0.13: replaces the removed <c>BaseItem.GetMediaStreams()</c>
    /// instance method (gone since Jellyfin 10.11). The new canonical API
    /// is <see cref="IMediaSourceManager.GetMediaStreams(System.Guid)"/>,
    /// which queries the same data via the media source registry. Wrapped
    /// in try/catch because a fresh-scan or virtual item may not have
    /// streams indexed yet, and the badge renderer must NEVER throw -
    /// otherwise the middleware serves an empty image for the whole poster.
    /// </summary>
    private IReadOnlyList<MediaStream> GetStreams(BaseItem item)
    {
        try
        {
            var list = _mediaSourceManager.GetMediaStreams(item.Id);
            return list ?? (IReadOnlyList<MediaStream>)Array.Empty<MediaStream>();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "GetMediaStreams failed for {ItemId}", item.Id);
            return Array.Empty<MediaStream>();
        }
    }

    /// <summary>
    /// Looks up a custom text override from the list by key.
    /// Returns <paramref name="defaultValue"/> if no override is found.
    /// Replaces Dictionary.GetValueOrDefault so the config can be XML-serialized.
    /// </summary>
    private static string Ct(
        IEnumerable<CustomTextEntry> list,
        string key,
        string defaultValue)
        => list.FirstOrDefault(e =>
               string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase))
               ?.Value ?? defaultValue;

    /// <summary>
    /// Composites badges onto <paramref name="originalImageData"/> and returns
    /// the resulting JPEG/PNG/WebP bytes.
    /// </summary>
    public byte[]? RenderBadges(
        byte[]          originalImageData,
        BaseItem        item,
        BadgesConfig    cfg,
        bool            isThumb = false)
    {
        if (!cfg.Enabled) return null;
        if (isThumb && !cfg.EnableOnThumbs) return null;
        if (!isThumb && !cfg.EnableOnPosters) return null;

        try
        {
            using var bitmap = SKBitmap.Decode(originalImageData);
            if (bitmap is null) return null;

            using var surface = SKSurface.Create(new SKImageInfo(bitmap.Width, bitmap.Height));
            var canvas = surface.Canvas;
            canvas.DrawBitmap(bitmap, 0, 0);

            // v3.0.18: render in 4 INDEPENDENT BUCKETS, one per category,
            // each at its own corner with its own gap/size/margin.
            //   - Language + Status   -> LanguageStyle
            //   - Resolution + Codec  -> ResolutionStyle
            //   - Audio               -> AudioStyle
            //   - HDR                 -> HdrStyle
            // The legacy single-stack model (everything stacked at one
            // corner with shared metrics) is gone — users explicitly
            // asked for independent positioning per type.

            var languageBucket   = new List<BadgeInfo>();
            var resolutionBucket = new List<BadgeInfo>();
            var audioBucket      = new List<BadgeInfo>();
            var hdrBucket        = new List<BadgeInfo>();

            foreach (var category in cfg.BadgeOrder)
            {
                switch (category)
                {
                    case "Resolution": AddResolutionBadge(item, cfg, resolutionBucket); break;
                    case "Codec":      AddCodecBadge(item, cfg, resolutionBucket);      break;
                    case "HDR":        AddHdrBadge(item, cfg, hdrBucket);               break;
                    case "Audio":      AddAudioBadge(item, cfg, audioBucket);           break;
                    case "Language":   AddLanguageBadges(item, cfg, languageBucket);    break;
                    case "Status":     AddStatusBadges(item, cfg, languageBucket);      break;
                }
            }

            int total =
                languageBucket.Count + resolutionBucket.Count +
                audioBucket.Count + hdrBucket.Count;
            if (total == 0) return null;

            // Draw each non-empty bucket at its configured anchor.
            DrawBucket(canvas, bitmap, languageBucket,   cfg.LanguageStyle,   cfg, isThumb);
            DrawBucket(canvas, bitmap, resolutionBucket, cfg.ResolutionStyle, cfg, isThumb);
            DrawBucket(canvas, bitmap, audioBucket,      cfg.AudioStyle,      cfg, isThumb);
            DrawBucket(canvas, bitmap, hdrBucket,        cfg.HdrStyle,        cfg, isThumb);

            using var image = surface.Snapshot();
            using var data  = cfg.OutputFormat switch
            {
                "PNG"  => image.Encode(SKEncodedImageFormat.Png, 100),
                "WebP" => image.Encode(SKEncodedImageFormat.Webp, cfg.JpegQuality),
                _      => image.Encode(SKEncodedImageFormat.Jpeg, cfg.JpegQuality)
            };

            return data.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rendering badges for {ItemId}", item.Id);
            return null;
        }
    }

    /// <summary>
    /// v3.0.18: draws one category bucket at its anchor position.
    /// Anchor positions supported (matches BadgeStyleConfig.Position):
    ///   TopLeft | TopRight | TopCenter | BottomLeft | BottomRight | BottomCenter
    /// Stacks Vertical (downwards from top anchors, upwards from bottom)
    /// or Horizontal (rightwards from left anchors, leftwards from right).
    /// </summary>
    private static void DrawBucket(
        SKCanvas canvas,
        SKBitmap bitmap,
        List<BadgeInfo> bucket,
        BadgeStyleConfig style,
        BadgesConfig cfg,
        bool isThumb)
    {
        if (bucket.Count == 0 || style is null || !style.Enabled) return;

        // Per-category sizing. Mirrors the previous global formula but
        // reads from this bucket's BadgeStyleConfig instead of the
        // shared cfg.Language values.
        float sizeMul = isThumb
            ? Math.Max(0.01f, cfg.ThumbSameAsPoster
                ? (float)(style.BadgeSize - cfg.ThumbSizeReduction) / 100f
                : style.BadgeSize / 100f)
            : Math.Max(0.01f, style.BadgeSize / 100f);

        float badgeH   = bitmap.Height * sizeMul;
        float fontSize = badgeH * 0.55f;
        float marginX  = bitmap.Width  * (style.Margin / 100f);
        float marginY  = bitmap.Height * (style.Margin / 100f);
        float gap      = badgeH * (style.Gap / 100f);

        // Estimated badge width (matches DrawTextBadge.w fallback).
        float badgeW   = badgeH * 2.5f;
        bool vertical  = !string.Equals(style.Layout, "Horizontal",
                                         StringComparison.OrdinalIgnoreCase);

        // Total stack dimensions (used for right/bottom/center anchoring).
        float stackH = vertical
            ? bucket.Count * badgeH + (bucket.Count - 1) * gap
            : badgeH;
        float stackW = vertical
            ? badgeW
            : bucket.Count * badgeW + (bucket.Count - 1) * gap;

        // Compute anchor origin (top-left of the stack).
        float originX, originY;
        switch ((style.Position ?? "TopRight").ToLowerInvariant())
        {
            case "topleft":
                originX = marginX;
                originY = marginY;
                break;
            case "topcenter":
                originX = (bitmap.Width - stackW) / 2f;
                originY = marginY;
                break;
            case "topright":
                originX = bitmap.Width - stackW - marginX;
                originY = marginY;
                break;
            case "bottomleft":
                originX = marginX;
                originY = bitmap.Height - stackH - marginY;
                break;
            case "bottomcenter":
                originX = (bitmap.Width - stackW) / 2f;
                originY = bitmap.Height - stackH - marginY;
                break;
            case "bottomright":
            default:
                originX = bitmap.Width  - stackW - marginX;
                originY = bitmap.Height - stackH - marginY;
                break;
        }

        float x = originX;
        float y = originY;
        foreach (var badge in bucket)
        {
            DrawTextBadge(canvas, badge.Text, badge.BgColor, badge.TextColor,
                          x, y, badgeH, fontSize, badge.Flag);
            if (vertical) y += badgeH + gap;
            else          x += badgeW + gap;
        }
    }

    // ── Badge collection ────────────────────────────────────────
    //
    // v3.0.18: CollectBadges() removed — RenderBadges now categorises
    // directly into 4 buckets (language/resolution/audio/hdr) so each
    // can be drawn at its own anchor with its own metrics. The Add*
    // helpers below are unchanged in signature; they just receive the
    // appropriate bucket list as their `list` parameter.

    private void AddResolutionBadge(BaseItem item, BadgesConfig cfg, List<BadgeInfo> list)
    {
        // v3.0.13: was item.GetMediaStreams() - removed in 10.11.
        var streams = GetStreams(item);
        var video   = streams.FirstOrDefault(s => s.Type == MediaStreamType.Video);
        if (video is null) return;

        string res = video.Width switch
        {
            >= 3840 => Ct(cfg.CustomText, "4K",    "4K"),
            >= 1920 => Ct(cfg.CustomText, "1080p", "1080p"),
            >= 1280 => Ct(cfg.CustomText, "720p",  "720p"),
            _       => Ct(cfg.CustomText, "SD",    "SD")
        };

        list.Add(new BadgeInfo(res, "#1e3a5f", "#6ec6ff"));
    }

    private void AddHdrBadge(BaseItem item, BadgesConfig cfg, List<BadgeInfo> list)
    {
        var streams = GetStreams(item);
        var video   = streams.FirstOrDefault(s => s.Type == MediaStreamType.Video);
        if (video is null) return;

        var hdr = video.VideoRangeType switch
        {
            VideoRangeType.DOVI
                or VideoRangeType.DOVIWithHDR10
                or VideoRangeType.DOVIWithHLG
                or VideoRangeType.DOVIWithSDR
                => Ct(cfg.CustomText, "DolbyVision", "DV"),
            VideoRangeType.HDR10Plus
                => Ct(cfg.CustomText, "HDR10Plus", "HDR10+"),
            VideoRangeType.HDR10
                => Ct(cfg.CustomText, "HDR10", "HDR10"),
            VideoRangeType.HLG
                => Ct(cfg.CustomText, "HLG", "HLG"),
            _   => null
        };

        if (hdr is not null)
            list.Add(new BadgeInfo(hdr, "#1e1a3f", "#a78fff"));
    }

    private void AddCodecBadge(BaseItem item, BadgesConfig cfg, List<BadgeInfo> list)
    {
        var streams = GetStreams(item);
        var video   = streams.FirstOrDefault(s => s.Type == MediaStreamType.Video);
        if (video is null) return;

        var codec = video.Codec?.ToUpperInvariant() switch
        {
            "HEVC" or "H265" => Ct(cfg.CustomText, "HEVC", "HEVC"),
            "AV1"            => Ct(cfg.CustomText, "AV1",  "AV1"),
            "VP9"            => Ct(cfg.CustomText, "VP9",  "VP9"),
            "H264" or "AVC"  => Ct(cfg.CustomText, "H264", "H.264"),
            _                => null
        };

        if (codec is not null)
            list.Add(new BadgeInfo(codec, "#1a1a1a", "#aaa"));
    }

    private void AddAudioBadge(BaseItem item, BadgesConfig cfg, List<BadgeInfo> list)
    {
        var streams = GetStreams(item);
        var audio   = streams.FirstOrDefault(s => s.Type == MediaStreamType.Audio);
        if (audio is null) return;

        var a = (audio.Profile ?? audio.Codec ?? "").ToUpperInvariant() switch
        {
            var p when p.Contains("ATMOS")   => Ct(cfg.CustomText, "DolbyAtmos", "ATMOS"),
            var p when p.Contains("TRUEHD")  => Ct(cfg.CustomText, "TrueHD",     "TrueHD"),
            var p when p.Contains("DTS:X")   => Ct(cfg.CustomText, "DTSX",       "DTS:X"),
            var p when p.Contains("DTS-HD")  => Ct(cfg.CustomText, "DTSHD",      "DTS-HD MA"),
            "DTS"                            => "DTS",
            var p when p.Contains("7.1")     => Ct(cfg.CustomText, "7.1", "7.1"),
            var p when p.Contains("5.1")     => Ct(cfg.CustomText, "5.1", "5.1"),
            "AAC" or "AC3" or "EAC3"        => "Stereo",
            _                               => null
        };

        if (a is not null)
            list.Add(new BadgeInfo(a, "#1e3a5f", "#6ec6ff"));
    }

    private void AddLanguageBadges(BaseItem item, BadgesConfig cfg, List<BadgeInfo> list)
    {
        var langCfg = cfg.Language;
        if (!langCfg.Enabled) return;

        var streams     = GetStreams(item);
        var audioLangs  = streams
            .Where(s => s.Type == MediaStreamType.Audio)
            .Select(s => s.Language ?? "")
            .ToList();

        var subLangs = streams
            .Where(s => s.Type == MediaStreamType.Subtitle)
            .Select(s => s.Language ?? "")
            .ToList();

        bool hasLatin = audioLangs.Any(l => LatinSpanishCodes.Contains(l));

        if (hasLatin)
        {
            // Show country-of-production flag + LAT text
            string flag = langCfg.LatinFlagStyle switch
            {
                "Mexico" => "🇲🇽",
                "Spain"  => "🇪🇸",
                _        => ""
            };

            // If ShowProductionCountryFlag, try to get the actual production country
            if (langCfg.ShowProductionCountryFlag && item is MediaBrowser.Controller.Entities.Movies.Movie movie)
            {
                var prodCountry = movie.ProductionLocations?.FirstOrDefault();
                if (!string.IsNullOrEmpty(prodCountry))
                    flag = CountryToFlag(prodCountry) ?? flag;
            }

            list.Add(new BadgeInfo(langCfg.LatinText, "#1e1e1e", "#ddd", flag));
        }

        // Other audio languages (non-LAT)
        var otherAudio = audioLangs
            .Where(l => !LatinSpanishCodes.Contains(l) && !string.IsNullOrEmpty(l))
            .Distinct()
            .Take(3)
            .ToList();

        foreach (var lang in otherAudio)
        {
            var flag = LanguageToFlag(lang);
            if (flag is not null)
                list.Add(new BadgeInfo("", "#1e1e1e", "#ddd", flag));
        }

        // SUB badge — collapse if too many subtitle languages
        if (subLangs.Count >= langCfg.SubThreshold && langCfg.SimplifiedSubMode)
        {
            list.Add(new BadgeInfo(langCfg.SubText, "#1a1a1a", "#aaa"));
        }
        else
        {
            foreach (var lang in subLangs.Distinct().Take(3))
            {
                var flag = LanguageToFlag(lang);
                if (flag is not null)
                    list.Add(new BadgeInfo("", "#1a1a1a", "#888", flag));
            }
        }
    }

    private static void AddStatusBadges(BaseItem item, BadgesConfig cfg, List<BadgeInfo> list)
    {
        var status = cfg.Status;

        // NUEVO badge
        if (status.NewEnabled)
        {
            var added = item.DateCreated;
            if (added != default &&
                (DateTime.UtcNow - added).TotalDays <= status.NewDaysThreshold)
            {
                list.Add(new BadgeInfo(status.NewText, status.NewBgColor, status.NewTextColor));
            }
        }

        // KID badge
        if (status.KidEnabled && IsKidContent(item, status))
            list.Add(new BadgeInfo(status.KidText, status.KidBgColor, status.KidTextColor));
    }

    private static bool IsKidContent(BaseItem item, StatusBadgeConfig cfg)
    {
        bool byRating = !string.IsNullOrEmpty(item.OfficialRating) &&
                        KidRatings.Contains(item.OfficialRating);
        bool byTag    = item.Tags?.Any(t =>
                            t.Equals("kids", StringComparison.OrdinalIgnoreCase) ||
                            t.Equals("children", StringComparison.OrdinalIgnoreCase) ||
                            t.Equals("infantil", StringComparison.OrdinalIgnoreCase)) ?? false;

        return cfg.KidDetectionMode switch
        {
            "Tag"  => byTag,
            "Both" => byRating || byTag,
            _      => byRating  // ParentalRating (default)
        };
    }

    // ── Drawing ─────────────────────────────────────────────────

    private static void DrawTextBadge(
        SKCanvas canvas,
        string   text,
        string   bgHex,
        string   fgHex,
        float    x, float y, float h,
        float    fontSize,
        string?  flag = null)
    {
        using var bgPaint  = new SKPaint { Color = ParseColor(bgHex), IsAntialias = true };
        using var txtPaint = new SKPaint
        {
            Color       = ParseColor(fgHex),
            TextSize    = fontSize,
            IsAntialias = true,
            Typeface    = SKTypeface.FromFamilyName("sans-serif", SKFontStyle.Bold)
        };

        string display = string.IsNullOrEmpty(flag) ? text : $"{flag} {text}".Trim();
        float  w       = Math.Max(h * 1.5f, txtPaint.MeasureText(display) + h * 0.4f);
        float  r       = h * 0.2f;

        canvas.DrawRoundRect(new SKRoundRect(new SKRect(x, y, x + w, y + h), r), bgPaint);
        canvas.DrawText(display, x + w / 2 - txtPaint.MeasureText(display) / 2, y + h * 0.72f, txtPaint);
    }

    // ── Helpers ─────────────────────────────────────────────────

    private static SKColor ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return SKColors.Transparent;
        hex = hex.TrimStart('#');
        // Expand 3-digit shorthand (#rgb → #rrggbb) before prepending alpha
        if (hex.Length == 3)
            hex = new string(new[] { hex[0], hex[0], hex[1], hex[1], hex[2], hex[2] });
        if (hex.Length == 6)
            hex = "FF" + hex;   // prepend full opacity
        return SKColor.TryParse(hex, out var color) ? color : SKColors.Transparent;
    }

    private static string? CountryToFlag(string country) => country.ToUpperInvariant() switch
    {
        "FRANCE" or "FR"    => "🇫🇷",
        "JAPAN"  or "JP"    => "🇯🇵",
        "UNITED STATES" or "US" => "🇺🇸",
        "UNITED KINGDOM" or "GB" => "🇬🇧",
        "GERMANY" or "DE"   => "🇩🇪",
        "SOUTH KOREA" or "KR" => "🇰🇷",
        "ITALY" or "IT"     => "🇮🇹",
        "SPAIN" or "ES"     => "🇪🇸",
        "BRAZIL" or "BR"    => "🇧🇷",
        "MEXICO" or "MX"    => "🇲🇽",
        _                   => null
    };

    private static string? LanguageToFlag(string lang) => lang.ToLowerInvariant() switch
    {
        "en" or "eng"       => "🇬🇧",
        "es" or "spa"       => "🇪🇸",
        var l when LatinSpanishCodes.Contains(l) => "🇲🇽",
        "fr" or "fre"       => "🇫🇷",
        "de" or "ger"       => "🇩🇪",
        "ja" or "jpn"       => "🇯🇵",
        "ko" or "kor"       => "🇰🇷",
        "pt" or "por"       => "🇧🇷",
        "it" or "ita"       => "🇮🇹",
        "zh" or "chi" or "zho" => "🇨🇳",
        _                   => null
    };

    private record BadgeInfo(string Text, string BgColor, string TextColor, string? Flag = null);
}
