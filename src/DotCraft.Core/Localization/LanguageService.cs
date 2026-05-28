using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotCraft.Localization;

/// <summary>
/// Supported languages
/// </summary>
public enum Language
{
    Chinese,
    English
}

/// <summary>
/// Service for managing language settings and localization.
/// Loads translations from embedded JSON language packs and provides key-based lookup.
/// </summary>
public sealed class LanguageService
{
    private static readonly Dictionary<Language, string> LanguageFileMap = new()
    {
        [Language.Chinese] = "zh",
        [Language.English] = "en"
    };

    /// <summary>
    /// Ambient context – set once during DI registration, then accessible everywhere.
    /// Falls back to a default (Chinese) instance if not explicitly configured.
    /// </summary>
    public static LanguageService Current { get; set; } = new();

    private static readonly Dictionary<Language, IReadOnlyDictionary<string, string>> TranslationCache = new();
    private static readonly object CacheLock = new();

    private volatile Language _currentLanguage;
    private IReadOnlyDictionary<string, string> _translations;

    public LanguageService(Language language = Language.Chinese)
    {
        _currentLanguage = language;
        _translations = LoadTranslations(language);
    }

    public Language CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            if (_currentLanguage != value)
            {
                _currentLanguage = value;
                _translations = LoadTranslations(value);
            }
        }
    }

    /// <summary>
    /// Resolves a key for a specific language without mutating <see cref="Current"/> (thread-safe for concurrent callers).
    /// </summary>
    public static string Translate(string key, Language language)
    {
        var translations = GetCachedTranslations(language);
        return translations.TryGetValue(key, out var value) ? value : key;
    }

    private static IReadOnlyDictionary<string, string> GetCachedTranslations(Language language)
    {
        lock (CacheLock)
        {
            if (!TranslationCache.TryGetValue(language, out var translations))
            {
                translations = LoadTranslations(language);
                TranslationCache[language] = translations;
            }

            return translations;
        }
    }

    /// <summary>
    /// Look up a translated string by key. Returns the key itself when not found,
    /// making missing translations easy to spot during development.
    /// </summary>
    public string T(string key)
    {
        return _translations.TryGetValue(key, out var value) ? value : key;
    }

    /// <summary>
    /// Look up a translated string by key and format it with the supplied arguments.
    /// </summary>
    public string T(string key, params object[] args)
    {
        var template = _translations.TryGetValue(key, out var value) ? value : key;
        return args.Length > 0 ? string.Format(template, args) : template;
    }

    /// <summary>
    /// Toggle between Chinese and English, reload translations.
    /// </summary>
    public Language ToggleLanguage()
    {
        var newLang = _currentLanguage == Language.Chinese ? Language.English : Language.Chinese;
        CurrentLanguage = newLang; // triggers reload via setter
        return newLang;
    }

    /// <summary>
    /// Switch language and persist the choice to the specified config.json file.
    /// </summary>
    public void SetLanguageAndPersist(Language language, string configPath)
    {
        CurrentLanguage = language;

        try
        {
            JsonObject configNode;
            if (File.Exists(configPath))
            {
                var existingJson = File.ReadAllText(configPath);
                configNode = JsonNode.Parse(existingJson) as JsonObject ?? new JsonObject();
            }
            else
            {
                configNode = new JsonObject();
            }

            configNode["Language"] = language.ToString();

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(configPath, configNode.ToJsonString(options));
        }
        catch
        {
            // Best-effort persist — don't crash the CLI if config write fails.
        }
    }

    // -----------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------

    private static IReadOnlyDictionary<string, string> LoadTranslations(Language language)
    {
        if (!LanguageFileMap.TryGetValue(language, out var fileSuffix))
            fileSuffix = "zh";

        var resourceName = $"DotCraft.Localization.Languages.{fileSuffix}.json";
        var assembly = typeof(LanguageService).Assembly;

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            return new Dictionary<string, string>();

        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();

        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        return dict ?? new Dictionary<string, string>();
    }
}
