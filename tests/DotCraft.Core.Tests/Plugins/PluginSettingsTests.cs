using System.Text.Json;
using DotCraft.Plugins;
using DotCraft.Workspaces;
using Xunit;

namespace DotCraft.Tests.Plugins;

public sealed class PluginSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dotcraft-plugin-settings-{Guid.NewGuid():N}");

    [Fact]
    public void Manifest_AcceptsSupportedFieldsAndDefaults()
    {
        var pluginRoot = CreatePlugin(
            "valid",
            """
            {
              "fields": [
                { "key": "title", "type": "text", "defaultValue": "Hello" },
                { "key": "body", "type": "textarea" },
                { "key": "scale", "type": "number", "defaultValue": 2, "min": 1, "max": 3 },
                { "key": "enabled", "type": "bool", "defaultValue": true },
                { "key": "density", "type": "select", "defaultValue": "compact", "options": ["compact", "comfortable"] },
                { "key": "tags", "type": "stringList", "defaultValue": ["one"] },
                { "key": "labels", "type": "keyValueMap", "defaultValue": { "a": "A" } },
                { "key": "advanced", "type": "json", "defaultValue": { "nested": true } }
              ]
            }
            """);

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == PluginDiagnosticSeverity.Error);
        Assert.Equal(8, Assert.IsType<PluginSettingsSchema>(result.Manifest?.Settings).Fields.Count);
    }

    [Theory]
    [InlineData("{ \"fields\": [{ \"key\": \"Name\", \"type\": \"text\" }, { \"key\": \"name\", \"type\": \"text\" }] }", "DuplicatePluginSettingsField")]
    [InlineData("{ \"fields\": [{ \"key\": \"count\", \"type\": \"integer\" }] }", "InvalidPluginSettingsFieldType")]
    [InlineData("{ \"fields\": [{ \"key\": \"enabled\", \"type\": \"bool\", \"defaultValue\": \"yes\" }] }", "InvalidPluginSettingsDefaultValue")]
    [InlineData("{}", "InvalidPluginSettingsSchema")]
    public void Manifest_RejectsInvalidSchema(string schema, string code)
    {
        var pluginRoot = CreatePlugin(Guid.NewGuid().ToString("N"), schema);

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.Null(result.Manifest);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Fact]
    public void Manifest_RejectsMissingSchemaFile()
    {
        var pluginRoot = CreatePlugin("missing-schema", "{}");
        File.Delete(Path.Combine(pluginRoot, "settings.schema.json"));

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.Null(result.Manifest);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "PluginSettingsSchemaMissing");
    }

    [Fact]
    public void Store_ResolvesDefaultsPersonalAndWorkspaceWithDeepMerge()
    {
        var manifest = LoadManifest(CreatePlugin(
            "layered",
            """
            {
              "fields": [
                { "key": "theme", "type": "text", "defaultValue": "light" },
                { "key": "advanced", "type": "json", "defaultValue": { "left": 1, "nested": { "a": 1 } } },
                { "key": "tags", "type": "stringList", "defaultValue": ["default"] }
              ]
            }
            """));
        var paths = Paths(withUserData: true);
        Directory.CreateDirectory(paths.UserData.RootPath!);
        Directory.CreateDirectory(paths.Data.RootPath);
        File.WriteAllText(
            Path.Combine(paths.UserData.RootPath!, PluginConfigStore.FileName),
            """{"layered":{"advanced":{"right":2,"nested":{"b":2}},"tags":["personal"]}}""");
        File.WriteAllText(
            Path.Combine(paths.Data.RootPath, PluginConfigStore.FileName),
            """{"layered":{"theme":"dark","advanced":{"nested":{"a":3}},"tags":["workspace"]}}""");

        var snapshot = new PluginConfigStore(paths).Get(manifest);

        Assert.Equal("dark", snapshot.Value.GetProperty("theme").GetString());
        Assert.Equal(1, snapshot.Value.GetProperty("advanced").GetProperty("left").GetInt32());
        Assert.Equal(2, snapshot.Value.GetProperty("advanced").GetProperty("right").GetInt32());
        Assert.Equal(3, snapshot.Value.GetProperty("advanced").GetProperty("nested").GetProperty("a").GetInt32());
        Assert.Equal(2, snapshot.Value.GetProperty("advanced").GetProperty("nested").GetProperty("b").GetInt32());
        Assert.Equal(["workspace"], snapshot.Value.GetProperty("tags").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(["personal", "workspace"], snapshot.WritableScopes);
    }

    [Fact]
    public void Store_UnsetRestoresInheritedValueAndPreservesOtherNamespaces()
    {
        var manifest = LoadManifest(CreatePlugin(
            "sample",
            """{"fields":[{"key":"title","type":"text","defaultValue":"default"}]}"""));
        var paths = Paths(withUserData: false);
        Directory.CreateDirectory(paths.Data.RootPath);
        File.WriteAllText(
            Path.Combine(paths.Data.RootPath, PluginConfigStore.FileName),
            """{"other":{"untouched":true},"sample":{"title":"workspace"}}""");
        var store = new PluginConfigStore(paths);

        var snapshot = store.Mutate(
            manifest,
            "workspace",
            [new PluginConfigMutation("unset", "TITLE")]);

        Assert.Equal("default", snapshot.Value.GetProperty("title").GetString());
        using var document = JsonDocument.Parse(File.ReadAllText(store.WorkspacePath));
        Assert.True(document.RootElement.GetProperty("other").GetProperty("untouched").GetBoolean());
        Assert.False(document.RootElement.TryGetProperty("sample", out _));
    }

    [Fact]
    public void Store_RejectsUnknownFieldsAndNeverOverwritesBrokenDocument()
    {
        var manifest = LoadManifest(CreatePlugin(
            "strict",
            """{"fields":[{"key":"enabled","type":"bool"}]}"""));
        var paths = Paths(withUserData: false);
        Directory.CreateDirectory(paths.Data.RootPath);
        var store = new PluginConfigStore(paths);
        File.WriteAllText(store.WorkspacePath, """{"strict":{"unknown":true}}""");
        var invalidNamespace = Assert.Throws<PluginConfigException>(() => store.Get(manifest));
        Assert.Equal(PluginConfigStore.NamespaceInvalid, invalidNamespace.Code);

        const string broken = "{ not-json";
        File.WriteAllText(store.WorkspacePath, broken);
        var invalidDocument = Assert.Throws<PluginConfigException>(() => store.Mutate(
            manifest,
            "workspace",
            [new PluginConfigMutation("set", "enabled", JsonSerializer.SerializeToElement(true))]));

        Assert.Equal(PluginConfigStore.DocumentInvalid, invalidDocument.Code);
        Assert.Equal(broken, File.ReadAllText(store.WorkspacePath));
    }

    [Fact]
    public void DataPath_UsesUserDataWhenConfiguredAndWorkspaceDataOtherwise()
    {
        var personal = Paths(withUserData: true);
        var workspaceOnly = Paths(withUserData: false);

        Assert.Equal(
            Path.Combine(personal.UserData.RootPath!, "plugins", "sample", "data"),
            PluginDataPaths.Resolve(personal, "sample"));
        Assert.Equal(
            Path.Combine(workspaceOnly.Data.RootPath, "plugin-data", "sample"),
            PluginDataPaths.Resolve(workspaceOnly, "sample"));
    }

    [Fact]
    public void Store_RejectsPersonalMutationWhenUserDataIsUnavailable()
    {
        var manifest = LoadManifest(CreatePlugin(
            "workspace-only",
            """{"fields":[{"key":"enabled","type":"bool"}]}"""));
        var store = new PluginConfigStore(Paths(withUserData: false));

        var exception = Assert.Throws<PluginConfigException>(() => store.Mutate(
            manifest,
            "personal",
            [new PluginConfigMutation("set", "enabled", JsonSerializer.SerializeToElement(true))]));

        Assert.Equal(PluginConfigStore.ScopeUnavailable, exception.Code);
        Assert.False(File.Exists(store.WorkspacePath));
    }

    [Fact]
    public async Task Store_ConcurrentNamespacesDoNotOverwriteEachOther()
    {
        var first = LoadManifest(CreatePlugin(
            "first",
            """{"fields":[{"key":"value","type":"number"}]}"""));
        var second = LoadManifest(CreatePlugin(
            "second",
            """{"fields":[{"key":"value","type":"number"}]}"""));
        var store = new PluginConfigStore(Paths(withUserData: false));

        await Task.WhenAll(
            Task.Run(() => store.Mutate(
                first,
                "workspace",
                [new PluginConfigMutation("set", "value", JsonSerializer.SerializeToElement(1))])),
            Task.Run(() => store.Mutate(
                second,
                "workspace",
                [new PluginConfigMutation("set", "value", JsonSerializer.SerializeToElement(2))])));

        using var document = JsonDocument.Parse(File.ReadAllText(store.WorkspacePath));
        Assert.Equal(1, document.RootElement.GetProperty("first").GetProperty("value").GetInt32());
        Assert.Equal(2, document.RootElement.GetProperty("second").GetProperty("value").GetInt32());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string CreatePlugin(string id, string schema)
    {
        var pluginRoot = Path.Combine(_root, "plugins", id);
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        Directory.CreateDirectory(Path.Combine(pluginRoot, "skills"));
        File.WriteAllText(Path.Combine(pluginRoot, "settings.schema.json"), schema);
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            $$"""
            {
              "schemaVersion": 1,
              "id": "{{id}}",
              "displayName": "{{id}}",
              "skills": "./skills",
              "settings": "./settings.schema.json"
            }
            """);
        return pluginRoot;
    }

    private static PluginManifest LoadManifest(string pluginRoot)
    {
        var result = PluginManifestParser.Load(pluginRoot);
        return Assert.IsType<PluginManifest>(result.Manifest);
    }

    private DotCraftPaths Paths(bool withUserData)
    {
        var workspace = Path.Combine(_root, withUserData ? "with-user" : "workspace-only");
        return new DotCraftPaths(
            workspace,
            Path.Combine(workspace, ".craft"),
            withUserData ? Path.Combine(_root, "user-data") : null);
    }
}
