using System.Reflection;
using System.Text.Json;
using DotCraft.Plugins;
using Xunit;

namespace DotCraft.Tests.Plugins;

public sealed class PluginDesktopManifestTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"dotcraft-desktop-manifest-{Guid.NewGuid():N}");

    [Fact]
    public void Parser_AcceptsInlineDesktopDeclaration()
    {
        var pluginRoot = PluginRoot("valid");
        WriteOutput(pluginRoot, "index.mjs", "export function activate() { return {}; }");
        WriteOutput(pluginRoot, "theme.css", ":root { color: red; }");
        WriteManifest(pluginRoot, "./desktop/dist/index.mjs", "[\"./desktop/dist/theme.css\"]");

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.DoesNotContain(result.Diagnostics, static diagnostic =>
            diagnostic.Severity == PluginDiagnosticSeverity.Error);
        var desktop = Assert.IsType<PluginDesktopManifest>(result.Manifest?.Desktop);
        Assert.Equal("./desktop/dist/index.mjs", desktop.Entry);
        Assert.Equal(["./desktop/dist/theme.css"], desktop.Styles);
        Assert.Matches("^[0-9a-f]{64}$", desktop.Revision);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    public void Parser_TreatsMissingOrNullStylesAsEmpty(string? stylesJson)
    {
        var pluginRoot = PluginRoot(Guid.NewGuid().ToString("N"));
        WriteOutput(pluginRoot, "index.mjs", "export function activate() { return {}; }");
        WriteManifest(pluginRoot, "./desktop/dist/index.mjs", stylesJson);

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.Empty(Assert.IsType<PluginDesktopManifest>(result.Manifest?.Desktop).Styles);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("\"1.0\"")]
    [InlineData("7")]
    public void Parser_RequiresCanonicalVersionForDesktopPlugin(string? versionJson)
    {
        var pluginRoot = PluginRoot(Guid.NewGuid().ToString("N"));
        WriteOutput(pluginRoot, "index.mjs", "export function activate() { return {}; }");
        WriteManifest(pluginRoot, "./desktop/dist/index.mjs", versionJson: versionJson);

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.Null(result.Manifest);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Code == "InvalidPluginVersion");
    }

    [Theory]
    [InlineData("./desktop/index.mjs")]
    [InlineData("./desktop/dist/../index.mjs")]
    [InlineData("./desktop/dist/nested//index.mjs")]
    [InlineData(".\\desktop\\dist\\index.mjs")]
    [InlineData("./desktop/dist/index.js")]
    public void Parser_RejectsInvalidDesktopEntry(string entry)
    {
        var pluginRoot = PluginRoot(Guid.NewGuid().ToString("N"));
        WriteOutput(pluginRoot, "index.mjs", "export function activate() { return {}; }");
        WriteManifest(pluginRoot, entry);

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.Null(result.Manifest);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Code == "InvalidPluginDesktopEntry");
    }

    [Fact]
    public void Parser_RejectsMissingEntryAndDuplicateStyles()
    {
        var missingRoot = PluginRoot("missing");
        WriteManifest(missingRoot, "./desktop/dist/index.mjs");

        var missing = PluginManifestParser.Load(missingRoot);

        Assert.Null(missing.Manifest);
        Assert.Contains(missing.Diagnostics, static diagnostic =>
            diagnostic.Code == "InvalidPluginDesktopEntry");

        var duplicateRoot = PluginRoot("duplicate");
        WriteOutput(duplicateRoot, "index.mjs", "export function activate() { return {}; }");
        WriteOutput(duplicateRoot, "theme.css", ":root {}");
        WriteManifest(
            duplicateRoot,
            "./desktop/dist/index.mjs",
            "[\"./desktop/dist/theme.css\", \"./desktop/dist/theme.css\"]");

        var duplicate = PluginManifestParser.Load(duplicateRoot);

        Assert.Null(duplicate.Manifest);
        Assert.Contains(duplicate.Diagnostics, static diagnostic =>
            diagnostic.Code == "DuplicatePluginDesktopStyle");
    }

    [Theory]
    [InlineData("./desktop/theme.css")]
    [InlineData("./desktop/dist/../theme.css")]
    [InlineData("./desktop/dist/theme.scss")]
    [InlineData("./desktop/dist/missing.css")]
    public void Parser_RejectsInvalidDesktopStyle(string style)
    {
        var pluginRoot = PluginRoot(Guid.NewGuid().ToString("N"));
        WriteOutput(pluginRoot, "index.mjs", "export function activate() { return {}; }");
        WriteManifest(
            pluginRoot,
            "./desktop/dist/index.mjs",
            JsonSerializer.Serialize(new[] { style }));

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.Null(result.Manifest);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Code == "InvalidPluginDesktopStyle");
    }

    [Fact]
    public void Revision_CoversDeclarationAndCompleteOutputTree()
    {
        var pluginRoot = PluginRoot("revision");
        WriteOutput(pluginRoot, "index.mjs", "import './chunk.mjs';");
        WriteOutput(pluginRoot, "alternate.mjs", "import './chunk.mjs';");
        WriteOutput(pluginRoot, "chunk.mjs", "export const value = 1;");
        WriteOutput(pluginRoot, "styles/base.css", ":root {}");
        WriteOutput(pluginRoot, "styles/theme.css", ".theme {}");
        WriteOutput(pluginRoot, "assets/icon.txt", "icon-one");
        WriteManifest(
            pluginRoot,
            "./desktop/dist/index.mjs",
            "[\"./desktop/dist/styles/base.css\", \"./desktop/dist/styles/theme.css\"]");
        var initial = Revision(pluginRoot);

        Assert.Equal(initial, Revision(pluginRoot));

        WriteOutput(pluginRoot, "assets/icon.txt", "icon-two");
        Assert.NotEqual(initial, Revision(pluginRoot));
        WriteOutput(pluginRoot, "assets/icon.txt", "icon-one");
        Assert.Equal(initial, Revision(pluginRoot));

        WriteOutput(pluginRoot, "chunk.mjs", "export const value = 2;");
        Assert.NotEqual(initial, Revision(pluginRoot));
        WriteOutput(pluginRoot, "chunk.mjs", "export const value = 1;");
        Assert.Equal(initial, Revision(pluginRoot));

        WriteOutput(pluginRoot, "styles/base.css", ":root { color: red; }");
        Assert.NotEqual(initial, Revision(pluginRoot));
        WriteOutput(pluginRoot, "styles/base.css", ":root {}");
        Assert.Equal(initial, Revision(pluginRoot));

        WriteManifest(
            pluginRoot,
            "./desktop/dist/index.mjs",
            "[\"./desktop/dist/styles/theme.css\", \"./desktop/dist/styles/base.css\"]");
        Assert.NotEqual(initial, Revision(pluginRoot));

        WriteManifest(
            pluginRoot,
            "./desktop/dist/alternate.mjs",
            "[\"./desktop/dist/styles/base.css\", \"./desktop/dist/styles/theme.css\"]");
        Assert.NotEqual(initial, Revision(pluginRoot));
    }

    [Fact]
    public void Revision_UsesOrdinalTreeOrderInsteadOfCreationOrder()
    {
        var first = PluginRoot("first-order");
        WriteOutput(first, "index.mjs", "export function activate() { return {}; }");
        WriteOutput(first, "assets/z.txt", "z");
        WriteOutput(first, "assets/a.txt", "a");
        WriteManifest(first, "./desktop/dist/index.mjs");

        var second = PluginRoot("second-order");
        WriteOutput(second, "assets/a.txt", "a");
        WriteOutput(second, "assets/z.txt", "z");
        WriteOutput(second, "index.mjs", "export function activate() { return {}; }");
        WriteManifest(second, "./desktop/dist/index.mjs");

        Assert.Equal(Revision(first), Revision(second));
    }

    [Fact]
    public void Revision_MatchesSharedCrossLanguageFixture()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("DotCraft.Tests.DesktopPluginRevision.json");
        using var document = JsonDocument.Parse(Assert.IsAssignableFrom<Stream>(stream));
        var fixture = document.RootElement;
        var pluginRoot = PluginRoot("shared-fixture");
        foreach (var file in fixture.GetProperty("files").EnumerateArray())
        {
            WriteOutput(
                pluginRoot,
                file.GetProperty("path").GetString()!,
                file.GetProperty("content").GetString()!);
        }
        var stylesJson = fixture.GetProperty("styles").GetRawText();
        WriteManifest(pluginRoot, fixture.GetProperty("entry").GetString()!, stylesJson);

        Assert.Equal(fixture.GetProperty("expectedRevision").GetString(), Revision(pluginRoot));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string PluginRoot(string name) => Path.Combine(_root, name);

    private static string Revision(string pluginRoot)
    {
        var result = PluginManifestParser.Load(pluginRoot);
        Assert.DoesNotContain(result.Diagnostics, static diagnostic =>
            diagnostic.Severity == PluginDiagnosticSeverity.Error);
        return Assert.IsType<PluginDesktopManifest>(result.Manifest?.Desktop).Revision;
    }

    private static void WriteOutput(string pluginRoot, string relativePath, string content)
    {
        var path = Path.Combine(
            pluginRoot,
            "desktop",
            "dist",
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static void WriteManifest(
        string pluginRoot,
        string entry,
        string? stylesJson = null,
        string? versionJson = "\"1.0.0\"")
    {
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        var styles = stylesJson == null ? string.Empty : $",\n    \"styles\": {stylesJson}";
        var version = versionJson == null ? string.Empty : $"\n  \"version\": {versionJson},";
        var entryJson = JsonSerializer.Serialize(entry);
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            $$"""
{
  "schemaVersion": 1,
  "id": "desktop.fixture",{{version}}
  "displayName": "Desktop Fixture",
  "desktop": {
    "entry": {{entryJson}}{{styles}}
  }
}
""");
    }
}
