using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace DotCraft.Satellite.Localization;

internal sealed class SatelliteStrings
{
    public const string FallbackLocale = "en";

    private static readonly string[] Locales = [FallbackLocale, "zh-Hans"];

    private readonly IReadOnlyDictionary<string, string> _catalog;
    private readonly IReadOnlyDictionary<string, string> _fallback;

    private SatelliteStrings(
        string locale,
        IReadOnlyDictionary<string, string> catalog,
        IReadOnlyDictionary<string, string> fallback)
    {
        Locale = locale;
        _catalog = catalog;
        _fallback = fallback;
    }

    public static SatelliteStrings Current { get; } = For(ReadOverride(), CultureInfo.CurrentUICulture);

    public string Locale { get; }

    public static SatelliteStrings For(string? preferred, CultureInfo culture)
    {
        var locale = Resolve(preferred) ?? Resolve(culture.Name) ?? ResolveParents(culture) ?? FallbackLocale;
        var fallback = Load(FallbackLocale);
        return new SatelliteStrings(
            locale,
            string.Equals(locale, FallbackLocale, StringComparison.Ordinal) ? fallback : Load(locale),
            fallback);
    }

    public static IReadOnlyList<string> AvailableLocales => Locales;

    public static IReadOnlyDictionary<string, string> Catalog(string locale) => Load(locale);

    public string this[string key] =>
        _catalog.TryGetValue(key, out var value) || _fallback.TryGetValue(key, out value)
            ? value
            : string.Empty;

    public string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, this[key], arguments);

    private static string? Resolve(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        var candidate = name.Trim();
        if (Locales.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            return Locales.First(locale => string.Equals(locale, candidate, StringComparison.OrdinalIgnoreCase));
        return candidate.ToLowerInvariant() switch
        {
            "zh" or "zh-cn" or "zh-sg" or "zh-hans-cn" or "zh-chs" => "zh-Hans",
            var other when other.StartsWith("en-", StringComparison.Ordinal) => FallbackLocale,
            var other when other.StartsWith("zh-hans", StringComparison.Ordinal) => "zh-Hans",
            _ => null
        };
    }

    private static string? ResolveParents(CultureInfo culture)
    {
        for (var parent = culture.Parent; !string.IsNullOrEmpty(parent.Name); parent = parent.Parent)
        {
            if (Resolve(parent.Name) is { } resolved)
                return resolved;
        }
        return null;
    }

    private static string? ReadOverride()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".craft",
                "satellite.json");
            if (!File.Exists(path))
                return null;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty("locale", out var locale) ? locale.GetString() : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static Dictionary<string, string> Load(string locale)
    {
        var name = $"DotCraft.Satellite.Strings.{locale}.json";
        using var stream = typeof(SatelliteStrings).GetTypeInfo().Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The catalog '{name}' is not embedded in the assembly.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException($"The catalog '{name}' is empty.");
    }
}
