using MediaBrowser.Model.Plugins;

namespace JellyFusion.Configuration;

/// <summary>Root configuration for the JellyFusion plugin.</summary>
public class PluginConfiguration : BasePluginConfiguration
{
    public PluginConfiguration()
    {
        Language       = "es";
        Home           = new HomeConfig();
        Slider         = new SliderConfig();
        Badges         = new BadgesConfig();
        Studios        = new StudiosConfig();
        Theme          = new ThemeConfig();
        Navigation     = new NavigationConfig();
        Notifications  = new NotificationsConfig();
    }

    // ── Global ──────────────────────────────────────────────────
    /// <summary>UI language: "es" | "en" | "pt" | "fr"</summary>
    public string Language { get; set; }

    /// <summary>
    /// v3.0.8 — one-shot flag: has Plugin.SeedDefaultsIfNeeded() ever run on
    /// this config? When false on plugin load we populate all list-backed
    /// defaults (Rails, Studios.Items, Navigation.Items, Badges.BadgeOrder)
    /// and persist. After that, empty lists mean "user explicitly cleared".
    ///
    /// Why this flag exists: in earlier versions lists were populated in
    /// property initializers, so XmlSerializer appended persisted items to
    /// the defaults each load and the options grew geometrically - the
    /// "se siguen multiplicando las opciones" regression. v3.0.8 moves
    /// defaults out of initializers and seeds them exactly once via this
    /// flag, which is the only way to respect user edits AND keep a
    /// freshly installed plugin non-empty.
    /// </summary>
    public bool HasBeenSeeded { get; set; } = false;

    // ── Modules (order reflects the config-page tab order) ──────
    public HomeConfig          Home          { get; set; }
    public SliderConfig        Slider        { get; set; }
    public BadgesConfig        Badges        { get; set; }
    public StudiosConfig       Studios       { get; set; }
    public ThemeConfig         Theme         { get; set; }
    public NavigationConfig    Navigation    { get; set; }
    public NotificationsConfig Notifications { get; set; }
}

// ═══════════════════════════════════════════════════════════════
// HOME — home page rails (Netflix-style rows)
// ═══════════════════════════════════════════════════════════════
public class HomeConfig
{
    /// <summary>Data source: "Local" | "TMDB" | "MDBList" | "Trakt"</summary>
    public string  DataSource   { get; set; } = "Local";
    public string? TmdbApiKey   { get; set; }
    public string? MdbListApiKey { get; set; }
    public string? TraktApiKey  { get; set; }

    /// <summary>
    /// v3.0.15 — when true, JellyFusion injects CSS that hides Jellyfin's
    /// stock "Mis medios" library cards row (the big tiles users see
    /// directly after the banner). Useful when the JellyFusion top-nav
    /// already exposes Movies/Series/etc. and the stock row is redundant.
    /// Default false so existing installs are not surprised by a missing
    /// section after upgrade.
    /// </summary>
    public bool HideStockMyMedia { get; set; } = false;

    /// <summary>
    /// Rails enabled on the home page, in visible order (top→bottom).
    ///
    /// v3.0.8 — CRITICAL BUGFIX: this list MUST start empty.
    ///
    /// Jellyfin persists plugin config with XmlSerializer. XmlSerializer's
    /// contract for collection-typed properties is: call the default
    /// constructor of the owning class (which sets the property to a
    /// pre-populated list), THEN read each &lt;HomeRail&gt; element from the
    /// XML and APPEND it to that list via List&lt;T&gt;.Add. There is no
    /// "replace"; the pre-populated items survive.
    ///
    /// Previous versions wrote defaults here. Each save+load cycle the
    /// persisted list had +N extra duplicates (N = default count = 9),
    /// so the admin UI showed more options every time. That's the
    /// "se siguen multiplicando las opciones" regression.
    ///
    /// Fix: start empty here, provide defaults via DefaultRails() which
    /// BuildRailsAsync calls ONLY when the persisted list is empty
    /// (i.e. true first-run).
    /// </summary>
    public List<HomeRail> Rails { get; set; } = new List<HomeRail>();

    /// <summary>First-run defaults for Rails. Not applied automatically —
    /// callers (HomeService.BuildRailsAsync) must invoke this only when
    /// the persisted list is empty, otherwise duplication resumes.</summary>
    public static List<HomeRail> DefaultRails() => new()
    {
        // Default order: Banner is rendered on top by the Slider module, so
        // the first rail below the banner is "Studios" (spec requirement).
        new HomeRail { Id = "studios",           Enabled = true  },
        new HomeRail { Id = "continueWatching",  Enabled = true  },
        new HomeRail { Id = "top10Series",       Enabled = true  },
        new HomeRail { Id = "top10Movies",       Enabled = true  },
        new HomeRail { Id = "becauseYouWatched", Enabled = true  },
        new HomeRail { Id = "newReleases",       Enabled = true  },
        new HomeRail { Id = "categories",        Enabled = true  },
        new HomeRail { Id = "recommended",       Enabled = true  },
        new HomeRail { Id = "upcoming",          Enabled = false },
    };
}

public class HomeRail
{
    public string  Id      { get; set; } = string.Empty;
    public bool    Enabled { get; set; } = true;
    public string? Title   { get; set; }   // null = use i18n default
    public int     MaxItems { get; set; } = 20;
}

// ═══════════════════════════════════════════════════════════════
// SLIDER / BANNER (Editor's Choice → Banner)
// ═══════════════════════════════════════════════════════════════
public class SliderConfig
{
    public bool   Enabled          { get; set; } = true;
    public string Mode             { get; set; } = "Random"; // Favourites | Random | Collections | New
    public string? FavouritesUser  { get; set; }
    public bool   AutoplayEnabled  { get; set; } = true;
    public int    AutoplayInterval { get; set; } = 10;

    // Filtering
    public int    MaxItems               { get; set; } = 5;
    public double MinCommunityRating     { get; set; } = 0;
    public int    MinCriticRating        { get; set; } = 0;
    public string MaxParentalRating      { get; set; } = "UserProfile";
    public bool   ShowPlayedItems        { get; set; } = true;

    // Display
    public string? BannerHeading        { get; set; }
    public bool    ShowPlayButton       { get; set; } = true;
    public string? CustomPlayButtonText { get; set; }
    public bool    ShowCommunityRating  { get; set; } = true;
    public bool    ShowDescription      { get; set; } = true;
    public bool    HideOnTv             { get; set; } = true;
    public bool    UseHeroDisplayStyle  { get; set; } = true;
    public string  ImagePosition        { get; set; } = "Bottom";  // Top | Center | Bottom
    public string  TransitionEffect     { get; set; } = "Slide";   // Slide | Fade | Zoom
    public string  BannerHeight         { get; set; } = "Large";   // Small | Medium | Large | Fullscreen
    /// <summary>Visual style: "netflix" | "disney" | "prime" | "apple"</summary>
    public string  PlatformStyle        { get; set; } = "netflix";

    // Trailers
    public bool   TrailerEnabled        { get; set; } = true;
    public string TrailerSource         { get; set; } = "TMDB";    // TMDB | YouTube | Local
    public string? TmdbApiKey           { get; set; }
    public string TrailerLanguage       { get; set; } = "es-419";  // es-419 | es-ES | en | pt | fr | sub | Auto
    public bool   TrailerSubtitleFallback { get; set; } = true;

    // Technical
    public bool   ClientScriptInjection { get; set; } = true;
    public bool   UseFileTransformation { get; set; } = false;
    public bool   ReduceImageSizes      { get; set; } = true;
}

// ═══════════════════════════════════════════════════════════════
// BADGES (JellyTag)
// ═══════════════════════════════════════════════════════════════
public class BadgesConfig
{
    public bool   Enabled            { get; set; } = true;
    public bool   EnableOnPosters    { get; set; } = true;
    public bool   EnableOnThumbs     { get; set; } = true;
    public bool   ThumbSameAsPoster  { get; set; } = true;
    public int    ThumbSizeReduction { get; set; } = 5;

    /// <summary>
    /// Badge order (index = priority, lower = rendered first / top).
    /// v3.0.8 — see HomeConfig.Rails for the XmlSerializer append-not-replace
    /// bug this empty initializer exists to avoid. Defaults are applied
    /// at use-time via DefaultOrder() when the persisted list is empty.
    /// </summary>
    public List<string> BadgeOrder { get; set; } = new List<string>();

    /// <summary>First-run defaults for BadgeOrder. Only apply when the
    /// persisted list is empty; calling this on every load re-introduces
    /// the duplication regression.</summary>
    public static List<string> DefaultOrder() => new()
        { "Resolution", "HDR", "Codec", "Audio", "Language", "Status" };

    public LanguageBadgeConfig Language { get; set; } = new();
    public StatusBadgeConfig   Status   { get; set; } = new();

    /// <summary>
    /// v3.0.18 — per-category positioning. Each badge category (Language,
    /// Resolution+Codec, Audio, HDR/3D) now has its OWN BadgeStyleConfig
    /// so the user can place language flags top-right, resolution
    /// top-left, audio bottom-right and HDR bottom-left, etc.
    ///
    /// LanguageStyle DOES NOT replace LanguageBadgeConfig.Position/Gap/
    /// BadgeSize/Margin — those remain for backward compatibility with
    /// configs from v3.0.17 and earlier. BadgeRenderService prefers the
    /// new per-category style when present and falls back to the legacy
    /// LanguageBadgeConfig values when the user hasn't customised it.
    /// </summary>
    public BadgeStyleConfig LanguageStyle   { get; set; } = new() { Position = "TopRight",    Gap = 10, BadgeSize = 15, Margin = 2 };
    public BadgeStyleConfig ResolutionStyle { get; set; } = new() { Position = "TopLeft",     Gap = 10, BadgeSize = 15, Margin = 2 };
    public BadgeStyleConfig AudioStyle      { get; set; } = new() { Position = "BottomRight", Gap = 10, BadgeSize = 15, Margin = 2 };
    public BadgeStyleConfig HdrStyle        { get; set; } = new() { Position = "BottomLeft",  Gap = 10, BadgeSize = 15, Margin = 2 };

    /// <summary>
    /// Custom text overrides for badge labels.
    /// Keyed by badge type (e.g. "4K", "HDR10", "HEVC", "DolbyAtmos").
    /// Uses a List instead of Dictionary so Jellyfin's XmlSerializer can persist it.
    ///
    /// v3.0.8 defensive fix #5: explicitly use `new List&lt;T&gt;()` instead of
    /// `new()` and DO NOT seed defaults inline. CustomText currently has
    /// zero defaults so the duplication bug doesn't manifest, but keeping
    /// the safer pattern prevents a future contributor from re-arming it
    /// by accident with a `new() { ... }` initializer.
    /// </summary>
    public List<CustomTextEntry> CustomText { get; set; } = new List<CustomTextEntry>();

    public int    CacheDurationHours { get; set; } = 24;
    public string OutputFormat       { get; set; } = "JPEG";  // JPEG | PNG | WebP
    public int    JpegQuality        { get; set; } = 90;
}

public class LanguageBadgeConfig
{
    public bool   Enabled                  { get; set; } = true;
    public string LatinFlagStyle           { get; set; } = "Mexico"; // Mexico | Spain | None
    public string LatinText                { get; set; } = "LAT";
    public bool   ShowProductionCountryFlag { get; set; } = true;

    public bool   SimplifiedSubMode        { get; set; } = true;
    public int    SubThreshold             { get; set; } = 3;
    public string SubText                  { get; set; } = "SUB";

    public string Position   { get; set; } = "TopRight";  // TopLeft | TopRight | BottomLeft | BottomRight
    public string Show       { get; set; } = "All";
    public string Layout     { get; set; } = "Vertical";  // Vertical | Horizontal
    public int    Gap        { get; set; } = 10;
    public int    BadgeSize  { get; set; } = 15;
    public int    Margin     { get; set; } = 2;
    public string BadgeStyle { get; set; } = "Image";     // Image | Text
}

/// <summary>
/// v3.0.18 — shared positioning + sizing for any badge category.
/// One instance per category lives on BadgesConfig (LanguageStyle,
/// ResolutionStyle, AudioStyle, HdrStyle).
/// </summary>
public class BadgeStyleConfig
{
    /// <summary>
    /// 6 anchor positions:
    ///   TopLeft | TopRight | TopCenter | BottomLeft | BottomRight | BottomCenter
    /// </summary>
    public string Position  { get; set; } = "TopRight";

    /// <summary>Layout for stacking multiple badges within the category.</summary>
    public string Layout    { get; set; } = "Vertical";  // Vertical | Horizontal

    /// <summary>Gap between badges (% of badge height).</summary>
    public int    Gap       { get; set; } = 10;

    /// <summary>Badge size (% of image height).</summary>
    public int    BadgeSize { get; set; } = 15;

    /// <summary>Margin from edge (% of image width).</summary>
    public int    Margin    { get; set; } = 2;

    /// <summary>Per-category enable; lets users hide e.g. HDR badges entirely.</summary>
    public bool   Enabled   { get; set; } = true;
}

public class StatusBadgeConfig
{
    public bool   NewEnabled        { get; set; } = true;
    public int    NewDaysThreshold  { get; set; } = 30;
    public string NewText           { get; set; } = "NUEVO";
    public string NewBgColor        { get; set; } = "#1a3a1a";
    public string NewTextColor      { get; set; } = "#6fcf6f";

    public bool   KidEnabled        { get; set; } = true;
    public string KidText           { get; set; } = "KID";
    public string KidBgColor        { get; set; } = "#3a2a1a";
    public string KidTextColor      { get; set; } = "#f0a050";
    public string KidDetectionMode  { get; set; } = "ParentalRating"; // ParentalRating | Tag | Both
}

// ═══════════════════════════════════════════════════════════════
// STUDIOS
// ═══════════════════════════════════════════════════════════════
public class StudiosConfig
{
    public bool   Enabled       { get; set; } = true;
    public string SectionTitle  { get; set; } = "Estudios";
    public string ImageStyle    { get; set; } = "Logo"; // Logo | Text | LogoAndName
    public int    CardWidth     { get; set; } = 120;
    public int    CardHeight    { get; set; } = 60;
    public int    BorderRadius  { get; set; } = 8;
    public bool   ShowName      { get; set; } = true;
    public bool   HoverEffect   { get; set; } = true;
    /// <summary>
    /// Configured studios, in display order.
    /// v3.0.8 — see HomeConfig.Rails. List starts empty to prevent the
    /// XmlSerializer append-duplication bug. DefaultStudios() is applied
    /// at use-time (StudiosService) ONLY when the list is empty.
    /// </summary>
    public List<StudioItem> Items { get; set; } = new List<StudioItem>();

    /// <summary>First-run defaults for Studios. Public so StudiosService
    /// can seed a freshly installed plugin with the canonical 7 studios.
    /// DO NOT call this on every load — that's the duplication bug.</summary>
    public static List<StudioItem> DefaultStudios() => new()
    {
        new StudioItem { Name = "Apple TV+",   Tags = "Apple TV,Apple Originals,Apple Studios LLC,Apple TV+",
                         Gradient = "linear-gradient(135deg,#1a1a2e,#0a0a0a)",
                         LogoUrl  = "https://image.tmdb.org/t/p/w780_filter(duotone,ffffff,bababa)/4KAy34EHvRM25Ih8wb82AuGU7zJ.png",
                         SortOrder = 0 },
        new StudioItem { Name = "Prime Video", Tags = "Amazon Prime Video,Amazon MGM Studios,AMC",
                         Gradient = "linear-gradient(135deg,#0d1b2a,#010409)",
                         LogoUrl  = "https://image.tmdb.org/t/p/w780_filter(duotone,ffffff,bababa)/ifhbNuuVnlwYy5oXA5VIb2YR8AZ.png",
                         SortOrder = 1 },
        new StudioItem { Name = "Hulu",        Tags = "Hulu,Hulu Originals",
                         Gradient = "linear-gradient(135deg,#0f2e1d,#07150d)",
                         LogoUrl  = "https://image.tmdb.org/t/p/w780_filter(duotone,ffffff,bababa)/pqUTCleNUiTLAVlelGxUgWn1ELh.png",
                         SortOrder = 2 },
        new StudioItem { Name = "Netflix",     Tags = "Netflix",
                         Gradient = "linear-gradient(135deg,#7a0000,#1a0000)",
                         LogoUrl  = "https://image.tmdb.org/t/p/w780_filter(duotone,ffffff,bababa)/wwemzKWzjKYJFfCeiB57q3r4Bcm.png",
                         SortOrder = 3 },
        new StudioItem { Name = "HBO Max",     Tags = "HBO Max,Max,Warner Bros,Warner Bros. Pictures,Warner Bros Television,Warner Bros Animation,DC Studios",
                         Gradient = "linear-gradient(135deg,#1a0a2e,#0d0018)",
                         LogoUrl  = "https://image.tmdb.org/t/p/w500_filter(duotone,ffffff,bababa)/nmU0UMDJB3dRRQSTUqawzF2Od1a.png",
                         SortOrder = 4 },
        new StudioItem { Name = "Disney+",     Tags = "Disney Plus,Disney+,Walt Disney Pictures,Walt Disney Animation Studios,Marvel Studios,Lucasfilm,20th Century Studios,20th Television",
                         Gradient = "linear-gradient(135deg,#0c1b3a,#050d1a)",
                         LogoUrl  = "https://image.tmdb.org/t/p/w780_filter(duotone,ffffff,bababa)/1edZOYAfoyZyZ3rklNSiUpXX30Q.png",
                         Invert   = true, SortOrder = 5 },
        new StudioItem { Name = "Pixar",       Tags = "Pixar",
                         Gradient = "linear-gradient(135deg,#0a1525,#0d2540 50%,#0a0a12)",
                         LogoUrl  = "https://image.tmdb.org/t/p/w780_filter(duotone,ffffff,bababa)/1TjvGVDMYsj6JBxOAkUHpPEwLf7.png",
                         SortOrder = 6 },
    };
}

public class StudioItem
{
    public string  Name      { get; set; } = string.Empty;
    public string? LogoUrl   { get; set; }
    public string? Tags      { get; set; }  // comma-separated Jellyfin studio names to match
    public string? Gradient  { get; set; }
    public bool    Invert    { get; set; }
    public string? CustomUrl { get; set; }
    public string  LinkMode  { get; set; } = "Auto"; // Auto | CustomUrl | Library
    public int     SortOrder { get; set; }
}

// ═══════════════════════════════════════════════════════════════
// THEMES
// ═══════════════════════════════════════════════════════════════
public class ThemeConfig
{
    /// <summary>Default | Netflix | PrimeVideo | DisneyPlus | AppleTvPlus | Crunchyroll | ParamountPlus</summary>
    public string  ActiveTheme     { get; set; } = "Default";
    public string? PrimaryColor    { get; set; }
    public string? BackgroundColor { get; set; }
    public string? FontFamily      { get; set; }
}

// ═══════════════════════════════════════════════════════════════
// NAVIGATION (top menu "Mi Contenido" → shortcuts)
// ═══════════════════════════════════════════════════════════════
public class NavigationConfig
{
    /// <summary>Replace "Mi Contenido" with fixed shortcuts.</summary>
    public bool ReplaceMyContent { get; set; } = true;

    /// <summary>
    /// Navigation shortcuts.
    /// v3.0.8 — see HomeConfig.Rails. Starts empty; DefaultItems() is
    /// applied at use-time when the persisted list is empty.
    /// </summary>
    public List<NavItem> Items { get; set; } = new List<NavItem>();

    /// <summary>First-run defaults for navigation items. Only apply when
    /// the persisted list is empty; calling this on every load re-creates
    /// the duplication regression.</summary>
    public static List<NavItem> DefaultItems() => new()
    {
        new NavItem { Id = "home",   LabelKey = "nav.home",   Icon = "home"   },
        new NavItem { Id = "movies", LabelKey = "nav.movies", Icon = "movie"  },
        new NavItem { Id = "series", LabelKey = "nav.series", Icon = "tv"     },
        new NavItem { Id = "live",   LabelKey = "nav.live",   Icon = "live_tv" },
    };
}

public class NavItem
{
    public string  Id       { get; set; } = string.Empty;
    public string  LabelKey { get; set; } = string.Empty;
    public string  Icon     { get; set; } = string.Empty;
    public string? Url      { get; set; }   // null = default route
}

// ═══════════════════════════════════════════════════════════════
// NOTIFICATIONS
// ═══════════════════════════════════════════════════════════════
public class NotificationsConfig
{
    public bool NotifyNewContent { get; set; } = true;
    public bool NotifyKidContent { get; set; } = false;

    public DiscordConfig  Discord  { get; set; } = new();
    public TelegramConfig Telegram { get; set; } = new();
}

public class DiscordConfig
{
    public bool    Enabled    { get; set; } = false;
    public string? WebhookUrl { get; set; }
}

public class TelegramConfig
{
    public bool    Enabled  { get; set; } = false;
    public string? BotToken { get; set; }
    public string? ChatId   { get; set; }
}

// ═══════════════════════════════════════════════════════════════
// CUSTOM TEXT ENTRY — replaces Dictionary<string,string> so that
// Jellyfin's XmlSerializer can persist badge label overrides.
// ═══════════════════════════════════════════════════════════════
/// <summary>
/// A single badge-label override: Key = badge type identifier,
/// Value = display string (e.g. Key="4K", Value="UHD").
/// </summary>
public class CustomTextEntry
{
    public string Key   { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
