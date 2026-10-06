using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.AppServer;
using DotCraft.Configuration;
using DotCraft.Lsp;
using DotCraft.Workspaces;
using Xunit;
using Methods = DotCraft.Protocol.AppServer.AppServerMethodNames;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class ConfigMethodsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"config_methods_{Guid.NewGuid():N}");
    private readonly string _craftPath;
    private readonly string _workspaceConfigPath;

    public ConfigMethodsTests()
    {
        _craftPath = Path.Combine(_root, ".craft");
        _workspaceConfigPath = Path.Combine(_craftPath, "config.json");
        Directory.CreateDirectory(_craftPath);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Read_WithoutLayers_ReturnsMaskedEffectiveConfigAndOrigins()
    {
        await File.WriteAllTextAsync(_workspaceConfigPath, """{ "InstantInterruptEnabled": false }""");
        using var harness = new AppServerTestHarness(workspaceCraftPath: _craftPath);
        WriteUserConfig(harness, """{ "DashBoard": { "Password": "secret" } }""");
        await harness.InitializeAsync();

        var result = await SendAsync(harness, Methods.ConfigRead, new JsonObject());

        var config = result.GetProperty("config");
        Assert.False(config.GetProperty("InstantInterruptEnabled").GetBoolean());
        Assert.Equal("***", config.GetProperty("DashBoard").GetProperty("Password").GetString());
        var origin = result.GetProperty("origins").GetProperty("InstantInterruptEnabled");
        Assert.Equal("workspace", origin.GetProperty("name").GetProperty("type").GetString());
        Assert.Equal(Path.GetFullPath(_workspaceConfigPath), origin.GetProperty("name").GetProperty("file").GetString());
        Assert.StartsWith("sha256:", origin.GetProperty("version").GetString());
        Assert.Equal("user", result.GetProperty("origins").GetProperty("DashBoard.Password").GetProperty("name").GetProperty("type").GetString());
        Assert.False(result.TryGetProperty("layers", out _));
    }

    [Fact]
    public async Task Read_WithLayers_ReturnsUserThenWorkspaceLayerContent()
    {
        await File.WriteAllTextAsync(_workspaceConfigPath, """{ "InstantInterruptEnabled": false }""");
        using var harness = new AppServerTestHarness(workspaceCraftPath: _craftPath);
        WriteUserConfig(harness, """{ "DashBoard": { "Password": "secret" }, "InstantInterruptEnabled": true }""");
        await harness.InitializeAsync();

        var result = await SendAsync(harness, Methods.ConfigRead, new JsonObject { ["includeLayers"] = true });

        var layers = result.GetProperty("layers").EnumerateArray().ToArray();
        Assert.Equal(["user", "workspace"], layers.Select(l => l.GetProperty("name").GetProperty("type").GetString()));
        Assert.Equal("***", layers[0].GetProperty("config").GetProperty("DashBoard").GetProperty("Password").GetString());
        Assert.True(layers[0].GetProperty("config").GetProperty("InstantInterruptEnabled").GetBoolean());
        Assert.False(layers[1].GetProperty("config").GetProperty("InstantInterruptEnabled").GetBoolean());
        Assert.Equal(
            result.GetProperty("origins").GetProperty("InstantInterruptEnabled").GetProperty("version").GetString(),
            layers[1].GetProperty("version").GetString());
    }

    [Fact]
    public async Task ValueWrite_HotField_WritesWorkspaceLayerAppliesAndReportsKeyPath()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _craftPath);
        using var bridge = ConfigChangedTests.AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        await harness.ExecuteRequestAsync(harness.BuildRequest(Methods.ConfigValueWrite, Edit("InstantInterruptEnabled", "false")));

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        var result = Assert.Single(sent, d => d.RootElement.TryGetProperty("result", out _)).RootElement.GetProperty("result");
        Assert.Equal("ok", result.GetProperty("status").GetString());
        Assert.Equal(Path.GetFullPath(_workspaceConfigPath), result.GetProperty("filePath").GetString());
        Assert.False(result.TryGetProperty("overriddenMetadata", out _));
        ConfigChangedTests.AssertSingleConfigChanged(sent, Methods.ConfigValueWrite, "InstantInterruptEnabled");
        Assert.False(harness.Monitor.Current.InstantInterruptEnabled);
        Assert.Equal(0, harness.Service.AgentInvalidationCount);
        Assert.False(ReadWorkspaceConfig().RootElement.GetProperty("InstantInterruptEnabled").GetBoolean());

        var read = await SendAsync(harness, Methods.ConfigRead, new JsonObject { ["includeLayers"] = true });
        Assert.Equal(result.GetProperty("version").GetString(), read.GetProperty("layers")[1].GetProperty("version").GetString());
    }

    [Fact]
    public async Task ValueWrite_NullValue_RemovesKeyAndRestoresDefault()
    {
        await File.WriteAllTextAsync(_workspaceConfigPath, """{ "InstantInterruptEnabled": false, "Theme": "dark" }""");
        using var harness = new AppServerTestHarness(workspaceCraftPath: _craftPath);
        harness.Monitor.Current.InstantInterruptEnabled = false;
        await harness.InitializeAsync();

        await SendAsync(harness, Methods.ConfigValueWrite, Edit("InstantInterruptEnabled", "null"));

        using var file = ReadWorkspaceConfig();
        Assert.False(file.RootElement.TryGetProperty("InstantInterruptEnabled", out _));
        Assert.Equal("dark", file.RootElement.GetProperty("Theme").GetString());
        Assert.True(harness.Monitor.Current.InstantInterruptEnabled);
    }

    [Fact]
    public async Task BatchWrite_AppliesEditsTogetherAndInvalidatesThreadAgentsOnce()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _craftPath);
        using var bridge = ConfigChangedTests.AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        await harness.ExecuteRequestAsync(harness.BuildRequest(Methods.ConfigBatchWrite, new JsonObject
        {
            ["edits"] = new JsonArray(
                Edit("Tools.ImageGeneration.Enabled", "false"),
                Edit("Tools.CodeMode.Mode", "\"on\""))
        }));

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        var notification = Assert.Single(sent, d => d.RootElement.TryGetProperty("method", out _));
        Assert.Equal(
            ["Tools.CodeMode.Mode", "Tools.ImageGeneration.Enabled"],
            notification.RootElement.GetProperty("params").GetProperty("regions").EnumerateArray()
                .Select(r => r.GetString()).Order(StringComparer.Ordinal));
        Assert.Equal(1, harness.Service.AgentInvalidationCount);
        Assert.Equal(AppConfig.CodeModeSetting.On, harness.Monitor.Current.Tools.CodeMode.Mode);
        Assert.False(harness.Monitor.Current.Tools.ImageGeneration.Enabled);
    }

    [Fact]
    public async Task ValueWrite_ProviderSelection_InvalidatesThreadAgentsOnlyWhenChanged()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _craftPath);
        using var bridge = ConfigChangedTests.AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        await harness.ExecuteRequestAsync(harness.BuildRequest(Methods.ConfigValueWrite, Edit("ProviderId", "\"anthropic-main\"")));
        var first = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        ConfigChangedTests.AssertSingleConfigChanged(first, Methods.ConfigValueWrite, "ProviderId");
        Assert.Equal("anthropic-main", harness.Monitor.Current.ProviderId);
        Assert.Equal(1, harness.Service.AgentInvalidationCount);

        await harness.ExecuteRequestAsync(harness.BuildRequest(Methods.ConfigValueWrite, Edit("ProviderId", "\"anthropic-main\"")));
        var second = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        ConfigChangedTests.AssertNoConfigChanged(second);
        Assert.Equal(1, harness.Service.AgentInvalidationCount);
    }

    [Fact]
    public async Task ValueWrite_SkillsSelfLearning_InvalidatesThreadAgents()
    {
        await File.WriteAllTextAsync(_workspaceConfigPath, """{ "Skills": { "SelfLearning": { "Enabled": true } } }""");
        using var harness = new AppServerTestHarness(workspaceCraftPath: _craftPath);
        await harness.InitializeAsync();

        await SendAsync(harness, Methods.ConfigValueWrite, Edit("Skills.SelfLearning.Enabled", "false"));

        Assert.False(harness.Monitor.Current.Skills.SelfLearning.Enabled);
        Assert.Equal(1, harness.Service.AgentInvalidationCount);
    }

    [Fact]
    public async Task ValueWrite_EnablingLsp_InitializesLspServers()
    {
        var config = new AppConfig();
        var lsp = new CountingLspServerManager(config, _root);
        using var harness = new AppServerTestHarness(
            workspaceCraftPath: _craftPath,
            appConfigMonitor: new AppConfigMonitor(config),
            lspServerManager: lsp);
        await harness.InitializeAsync();

        await SendAsync(harness, Methods.ConfigValueWrite, Edit("Tools.Lsp.Enabled", "true"));

        Assert.True(config.Tools.Lsp.Enabled);
        Assert.Equal(1, lsp.Initializations);
    }

    [Fact]
    public async Task UserLayerWrite_HiddenByWorkspaceOverride_ReportsOkOverridden()
    {
        await File.WriteAllTextAsync(_workspaceConfigPath, """{ "InstantInterruptEnabled": false }""");
        using var harness = new AppServerTestHarness(workspaceCraftPath: _craftPath);
        await harness.InitializeAsync();
        var userPath = Path.GetFullPath(harness.Monitor.Current.GlobalConfigPath!);

        var edit = Edit("InstantInterruptEnabled", "true");
        edit["filePath"] = userPath;
        var result = await SendAsync(harness, Methods.ConfigValueWrite, edit);

        Assert.Equal("okOverridden", result.GetProperty("status").GetString());
        Assert.Equal(userPath, result.GetProperty("filePath").GetString());
        var overridden = result.GetProperty("overriddenMetadata");
        Assert.Equal("workspace", overridden.GetProperty("overridingLayer").GetProperty("name").GetProperty("type").GetString());
        Assert.False(overridden.GetProperty("effectiveValue").GetBoolean());
        Assert.True(JsonDocument.Parse(await File.ReadAllTextAsync(userPath)).RootElement.GetProperty("InstantInterruptEnabled").GetBoolean());
    }

    [Theory]
    [InlineData("Tools.Unknown", "true", null, null, "configSchemaUnknownKey")]
    [InlineData("InstantInterruptEnabled", "true", "elsewhere.json", null, "configLayerReadonly")]
    [InlineData("InstantInterruptEnabled", "true", null, "sha256:stale", "configVersionConflict")]
    [InlineData("Permissions.DefaultApprovalPolicy", "\"prompt\"", null, null, "configValidationError")]
    [InlineData("Dreams.Interval", "\"00:00:00\"", null, null, "configValidationError")]
    [InlineData("ProviderPreferences.openai", "{ \"Model\": \" \" }", null, null, "configValidationError")]
    public async Task ValueWrite_Rejected_ReturnsErrorCodeAndLeavesFileUnchanged(
        string keyPath,
        string valueJson,
        string? fileName,
        string? expectedVersion,
        string expectedCode)
    {
        await File.WriteAllTextAsync(_workspaceConfigPath, """{ "Theme": "dark" }""");
        var before = await File.ReadAllBytesAsync(_workspaceConfigPath);
        using var harness = new AppServerTestHarness(workspaceCraftPath: _craftPath);
        using var bridge = ConfigChangedTests.AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var edit = Edit(keyPath, valueJson);
        edit["filePath"] = fileName == null ? null : Path.Combine(_root, fileName);
        edit["expectedVersion"] = expectedVersion;
        await harness.ExecuteRequestAsync(harness.BuildRequest(Methods.ConfigValueWrite, edit));

        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AppServerTestHarness.AssertConfigWriteError(Assert.Single(sent), expectedCode);
        Assert.Equal(before, await File.ReadAllBytesAsync(_workspaceConfigPath));
        Assert.Equal(0, harness.Service.AgentInvalidationCount);
    }

    private static JsonObject Edit(string keyPath, string valueJson) => new()
    {
        ["keyPath"] = keyPath,
        ["value"] = JsonNode.Parse(valueJson),
        ["mergeStrategy"] = "replace"
    };

    private static async Task<JsonElement> SendAsync(AppServerTestHarness harness, string method, JsonObject parameters)
    {
        await harness.ExecuteRequestAsync(harness.BuildRequest(method, parameters));
        var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        return response.RootElement.GetProperty("result").Clone();
    }

    private static void WriteUserConfig(AppServerTestHarness harness, string json)
    {
        var path = harness.Monitor.Current.GlobalConfigPath!;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    private JsonDocument ReadWorkspaceConfig() => JsonDocument.Parse(File.ReadAllText(_workspaceConfigPath));

    private sealed class CountingLspServerManager(AppConfig config, string workspacePath) : LspServerManager(
        config,
        new DotCraftPaths(workspacePath, Path.Combine(workspacePath, ".craft"), userDataPath: null))
    {
        public int Initializations { get; private set; }

        public override Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            Initializations++;
            return Task.CompletedTask;
        }
    }
}
