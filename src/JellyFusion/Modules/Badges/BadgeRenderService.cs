using JellyFusion.Configuration;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
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

    // Known Latin-Spanish audio language codes
    private static readonly HashSet<string> LatinSpanishCodes =
        new(StringComparer.OrdinalIgnoreCase) { "es-419", "es-MX", "es-AR", "es-CO", "spa-419" };

    // Kid-friendly parental ratings
    private static readonly HashSet<string> KidRatings =
        new(StringComparer.OrdinalIgnoreCase) { "G", "TV-Y", "TV-Y7", "TV-G", "PG", "NR" };

    public BadgeRenderService(ILogger<BadgeRenderService> logger, ImageCacheService cache)
    {
        _logger = logger;
        _cache  = cache;
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

            // Calculate badge size relative to image
            // BUG FIX: was Math.Max(1, value/100f) which always returned 1.0
            // because value/100f (e.g. 15/100 = 0.15) is always < 1, making
            // badgeH = 100% of the image height. Minimum is now 1% (0.01f).
            float sizeMultiplier = isThumb
                ? Math.Max(0.01f, cfg.ThumbSameAsPoster
                    ? (float)(cfg.Language.BadgeSize - cfg.ThumbSizeReduction) / 100f
                    : cfg.Language.BadgeSize / 100f)
                : Math.Max(0.01f, cfg.Language.BadgeSize / 100f);

            float badgeH   = bitmap.Height * sizeMultiplier;
            float fontSize = badgeH * 0.55f;
            float margin   = bitmap.Width * (cfg.Language.Margin / 100f);
            float gap      = badgeH * (cfg.Language.Gap / 100f);

            // Collect badges in configured order
            var badges = CollectBadges(item, cfg, badgeH);
            if (badges.Count == 0) return null;

            // Draw badges
            float x = margin;
            float y = margin;

            foreach (var badge in badges)
            {
                DrawTextBadge(canvas, badge.Text, badge.BgColor, badge.TextColor,
                              x, y, badgeH, fontSize, badge.Flag);
                if (cfg.Language.Layout == "Vertical")
                    y += badgeH + gap;
                else
                    x += badgeH * 2.5f + gap;
            }

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

    // ── Badge collection ────────────────────────────────────────

    private List<BadgeInfo> CollectBadges(BaseItem item, BadgesConfig cfg, float badgeH)
    {
        var result = new List<BadgeInfo>();

        foreach (var category in cfg.BadgeOrder)
        {
            switch (category)
            {
                case "Resolution": AddResolutionBadge(item, cfg, result); break;
                case "HDR":        AddHdrBadge(item, cfg, result);        break;
                case "Codec":      AddCodecBadge(item, cfg, result);      break;
                case "Audio":      AddAudioBadge(item, cfg, result);      break;
                case "Language":   AddLanguageBadges(item, cfg, result);  break;
                case "Status":     AddStatusBadges(item, cfg, result);    break;
            }
        }

        return result;
    }

    private static void AddResolutionBadge(BaseItem item, BadgesConfig cfg, List<BadgeInfo> list)
    {
        // Try to read resolution from media streams
        var streams = item.GetMediaStreams();
        var video   = streams?.FirstOrDefault(s => s.Type == MediaBrowser.Model.Entities.MediaStreamType.Video);
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

    private static void AddHdrBadge(BaseItem item, BadgesConfig cfg, List<BadgeInfo> list)
    {
        var streams = item.GetMediaStreams();
        var video   = streams?.FirstOrDefault(s => s.Type == MediaBrowser.Model.Entities.MediaStreamType.Video);
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

    private static void AddCodecBadge(BaseItem item, BadgesConfig cfg, List<BadgeInfo> list)
    {
        var streams = item.GetMediaStreams();
        var video   = streams?.FirstOrDefault(s => s.Type == MediaBrowser.Model.Entities.MediaStreamType.Video);
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

    private static void AddAudioBadge(BaseItem item, BadgesConfig cfg, List<BadgeInfo> list)
    {
        var streams = item.GetMediaStreams();
        var audio   = streams?.FirstOrDefault(s => s.Type == MediaBrowser.Model.Entities.MediaStreamType.Audio);
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

        var streams     = item.GetMediaStreams();
        var audioLangs  = streams?
            .Where(s => s.Type == MediaBrowser.Model.Entities.MediaStreamType.Audio)
            .Select(s => s.Language ?? "")
            .ToList() ?? new();

        var subLangs = streams?
            .Where(s => s.Type == MediaBrowser.Model.Entities.MediaStreamType.Subtitle)
            .Select(s => s.Language ?? "")
            .ToList() ?? new();

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
