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

        // $$ raw string: single { } are literal, {{ }} are interpolation holes.
        // Used here so CSS braces can stay unescaped.
        return $$"""
            :root {
              --jf-primary:      {{vars.Primary}};
              --jf-primary-hover:{{vars.AccentHover ?? vars.Primary}};
              --jf-background:   {{vars.Background}};
              --jf-header-bg:    {{vars.HeaderBg ?? vars.Background}};
              --jf-font:         {{vars.Font}};
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
