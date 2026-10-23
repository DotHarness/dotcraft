using System.Globalization;
using System.Text.RegularExpressions;
using DotCraft.Satellite.Localization;
using Xunit;

namespace DotCraft.Satellite.Tests;

public sealed partial class SatelliteStringsTests
{
    [Fact]
    public void EveryCatalog_CarriesTheSameKeysAndPlaceholders()
    {
        var english = SatelliteStrings.Catalog(SatelliteStrings.FallbackLocale);

        foreach (var locale in SatelliteStrings.AvailableLocales)
        {
            var catalog = SatelliteStrings.Catalog(locale);
            Assert.Equal(english.Keys.Order(StringComparer.Ordinal), catalog.Keys.Order(StringComparer.Ordinal));
            foreach (var (key, value) in english)
                Assert.Equal(Placeholders(value), Placeholders(catalog[key]));
        }
    }

    [Theory]
    [InlineData("zh-CN", "zh-Hans")]
    [InlineData("zh", "zh-Hans")]
    [InlineData("en-GB", "en")]
    [InlineData("ja-JP", "en")]
    public void Locale_ResolvesThroughTheAliasTable(string requested, string expected)
    {
        Assert.Equal(expected, SatelliteStrings.For(requested, CultureInfo.InvariantCulture).Locale);
    }

    [Fact]
    public void SystemCulture_IsUsedWhenNoOverrideIsSet()
    {
        Assert.Equal("zh-Hans", SatelliteStrings.For(null, new CultureInfo("zh-CN")).Locale);
        Assert.Equal("zh-Hans", SatelliteStrings.For(null, new CultureInfo("zh-Hant-TW")).Locale);
        Assert.Equal("en", SatelliteStrings.For(null, new CultureInfo("ko-KR")).Locale);
    }

    private static IReadOnlyList<string> Placeholders(string value) =>
        [.. PlaceholderPattern().Matches(value).Select(match => match.Value).Order(StringComparer.Ordinal)];

    [GeneratedRegex(@"\{\d+\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();
}
