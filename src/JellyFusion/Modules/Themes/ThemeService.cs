using JellyFusion.Configuration;

namespace JellyFusion.Modules.Themes;

/// <summary>
/// Returns CSS variable overrides for each built-in theme.
/// Injected into the page via the client script.
/// </summary>
public class ThemeService
{
    private static readonly Dictionary<string, ThemeVars> BuiltIn = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Netflix"] = new("#e50914", "#141414", "'Georgia', serif",
            HeaderBg: "#000", AccentHover: "#b20710"),

        ["PrimeVideo"] = new("#00a8e1", "#0f171e", "'Amazon Ember', Arial, sans-serif",
            HeaderBg: "#0f171e", AccentHover: "#0090c0"),

        ["DisneyPlus"] = new("#0063e5", "#040714", "'Avenir', Arial, sans-serif",
            HeaderBg: "#040714", AccentHover: "#0050b8"),

        ["AppleTvPlus"] = new("#ffffff", "#000000", "-apple-system, 'Helvetica Neue', sans-serif",
            HeaderBg: "#1c1c1e", AccentHover: "#cccccc"),

        ["Crunchyroll"] = new("#f47521", "#1a1a1a", "'Arial Black', Impact, sans-serif",
            HeaderBg: "#0a0a0a", AccentHover: "#d4601a"),

        ["ParamountPlus"] = new("#0056b8", "#0d2040", "'Helvetica Neue', Arial, sans-serif",
            HeaderBg: "#0a1830", AccentHover: "#0045a0"),
    };

    /// <summary>
    /// Normalises a FontFamily config value.
    /// "system", empty string, or null all mean "use the default/fallback value".
    /// </summary>
    private static string? NormalisedFont(string? raw)
        => string.IsNullOrWhiteSpace(raw) || raw.Equals("system", StringComparison.OrdinalIgnoreCase)
            ? null
            : raw;

    /// <summary>Returns an inline CSS block with theme variables for the active theme.</summary>
    public string GetThemeCss(ThemeConfig cfg)
    {
        ThemeVars vars;
        var fontOverride = NormalisedFont(cfg.FontFamily);

        if (BuiltIn.TryGetValue(cfg.ActiveTheme, out var preset))
        {
            // Allow per-user overrides on top of the preset
            vars = preset with
            {
                Primary    = cfg.PrimaryColor    ?? preset.Primary,
                Background = cfg.BackgroundColor ?? preset.Background,
                Font       = fontOverride        ?? preset.Font
            };
        }
        else
        {
            // Default / custom
            vars = new ThemeVars(
                cfg.PrimaryColor    ?? "#00a4dc",
                cfg.BackgroundColor ?? "#101010",
                fontOverride        ?? "inherit");
        }

        // v3.0.7 THEME FIX: previous versions emitted `--jf-primary`,
        // `--jf-background`, `--jf-font` as CSS custom properties. But
        // Jellyfin's own stylesheet NEVER references those - it uses
        // `--primary-color`, `--background-color`, etc. So setting them
        // was a no-op: the user clicked Netflix theme, the bootstrap
        // happily injected the CSS, the browser cached it, and visually
        // nothing changed (which matches the v3.0.6 feedback "themes
        // don't reflect anything").
        //
        // Fix: override BOTH our own vars (kept so any in-plugin UI can
        // still key off them) AND Jellyfin's real vars, plus a handful
        // of explicit `!important` overrides on the elements that
        // Jellyfin styles with hard-coded colors (body background, skin
        // header, emby-button submit, raised button). Each override is
        // wrapped so it CAN'T affect anything outside the scope we
        // intend - `html[data-theme]` / `body` are the safe anchors.
        var primary     = vars.Primary;
        var primaryHov  = vars.AccentHover ?? vars.Primary;
        var background  = vars.Background;
        var headerBg    = vars.HeaderBg ?? vars.Background;
        var font        = vars.Font;

        return $$"""
            /* JellyFusion theme - own scope (plugin UI) */
            :root {
              --jf-primary:      {{primary}};
              --jf-primary-hover:{{primaryHov}};
              --jf-background:   {{background}};
              --jf-header-bg:    {{headerBg}};
              --jf-font:         {{font}};

              /* Jellyfin's actual CSS variables - the ones its own
                 stylesheet reads. Overriding these is what actually
                 repaints the UI. */
              --primary-color:         {{primary}};
              --primary-color--hover:  {{primaryHov}};
              --accent:                {{primary}};
              --theme-primary-color:   {{primary}};
              --theme-accent-text-color:{{primary}};
              --background-color:      {{background}};
              --color-background:      {{background}};
              --color-background-main: {{background}};
            }

            html, body {
              background-color: {{background}} !important;
              font-family: {{font}} !important;
            }

            .skinHeader,
            .mainDrawer,
            .mainDrawer-scrollContainer,
            .mainDrawer-scrollSlider {
              background-color: {{headerBg}} !important;
            }

            /* Primary action buttons (Play, Save, Sign In...) */
            .button-submit,
            .raised.button-submit,
            .emby-button.button-submit,
            .emby-button.raised.button-submit,
            .paper-icon-button-light.button-submit,
            .jf-btn-primary {
              background-color: {{primary}} !important;
              color: #fff !important;
            }
            .button-submit:hover,
            .raised.button-submit:hover,
            .emby-button.button-submit:hover,
            .emby-button.raised.button-submit:hover {
              background-color: {{primaryHov}} !important;
            }

            /* Nav active item + accent links */
            .navMenuOption-selected,
            .navMenuOption.selected,
            .mainDrawer .navMenuOption.selected {
              color: {{primary}} !important;
            }
            a, .button-link {
              color: {{primary}};
            }
            """;
    }

    private record ThemeVars(
        string Primary,
        string Background,
        string Font,
        string? HeaderBg    = null,
        string? AccentHover = null);
}
