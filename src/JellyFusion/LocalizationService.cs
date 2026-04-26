using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace JellyFusion;

/// <summary>
/// Loads and provides translated strings from embedded JSON resources.
/// Supported languages: es (Español Latino), en (English), pt (Português), fr (Français).
/// </summary>
public class LocalizationService
{
    private readonly ILogger<LocalizationService> _logger;
    private readonly Dictionary<string, Dictionary<string, string>> _cache = new();

    public LocalizationService(ILogger<LocalizationService> logger)
    {
        _logger = logger;
        LoadAll();
    }

    /// <summary>Get a translated string. Falls back to key if not found.</summary>
    public string Get(string key, string? lang = null)
    {
        lang ??= Plugin.Instance?.Configuration?.Language ?? "es";

        if (_cache.TryGetValue(lang, out var dict) && dict.TryGetValue(key, out var val))
            return val;

        // Fall back to English
        if (_cache.TryGetValue("en", out var enDict) && enDict.TryGetValue(key, out var enVal))
            return enVal;

        return key;
    }

    /// <summary>Returns all cached key/value pairs for the given language,
    /// merged on top of English as a fallback layer.</summary>
    public IReadOnlyDictionary<string, string> GetAllForLanguage(string lang)
    {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);

        if (_cache.TryGetValue("en", out var enDict))
            foreach (var kvp in enDict) merged[kvp.Key] = kvp.Value;

        if (!string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase) &&
            _cache.TryGetValue(lang, out var dict))
            foreach (var kvp in dict) merged[kvp.Key] = kvp.Value;

        return merged;
    }

    private void LoadAll()
    {
        foreach (var lang in new[] { "es", "en", "pt", "fr" })
        {
            var resourceName = $"JellyFusion.Localization.{lang}.strings.json";
            var assembly     = GetType().Assembly;
            using var stream = assembly.GetManifestResourceStream(resourceName);

            if (stream is null)
            {
                _logger.LogWarning("Localization file not found: {Resource}", resourceName);
                continue;
            }

            try
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
                if (data is not null) _cache[lang] = data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse localization: {Lang}", lang);
            }
        }
    }
}
