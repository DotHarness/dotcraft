using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Contributions;
using DotCraft.AppServer;
using ModelPreference = DotCraft.Configuration.ModelPreference;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class SubAgentProfileManagementTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"subagent_profile_mgmt_{Guid.NewGuid():N}");
    private readonly string _workspaceCraftPath;

    public SubAgentProfileManagementTests()
    {
        _workspaceCraftPath = Path.Combine(_tempRoot, ".craft");
        Directory.CreateDirectory(_workspaceCraftPath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public async Task List_ReturnsBuiltInsAndTemplateState()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();

        var req = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentProfileList, new { });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AppServerTestHarness.AssertIsSuccessResponse(sent[0]);

        var result = sent[0].RootElement.GetProperty("result");
        Assert.Equal("native", result.GetProperty("defaultName").GetString());

        var profiles = result.GetProperty("profiles").EnumerateArray().ToList();
        Assert.Contains(profiles, profile => profile.GetProperty("name").GetString() == "native");
        Assert.Contains(profiles, profile => profile.GetProperty("name").GetString() == "codex-cli");
        Assert.Contains(profiles, profile => profile.GetProperty("name").GetString() == "cursor-cli");

        var template = profiles.Single(profile => profile.GetProperty("name").GetString() == "custom-cli-oneshot");
        Assert.True(template.GetProperty("isBuiltIn").GetBoolean());
        Assert.True(template.GetProperty("isTemplate").GetBoolean());
        Assert.False(template.GetProperty("hasWorkspaceOverride").GetBoolean());
    }

    [Fact]
    public async Task Upsert_PersistsWorkspaceOverride_AndListReflectsOverride()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(BuildUpsertRequest(harness, "codex-cli", timeout: 600));
        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AppServerTestHarness.AssertIsSuccessResponse(sent[0]);

        var configPath = Path.Combine(_workspaceCraftPath, "config.json");
        var json = await File.ReadAllTextAsync(configPath);
        var root = JsonNode.Parse(json)!.AsObject();
        var profiles = root["SubAgentProfiles"]!.AsObject();
        var codex = profiles["codex-cli"]!.AsObject();
        Assert.Equal(600, codex["Timeout"]?.GetValue<int>());

        var listReq = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentProfileList, new { });
        await harness.ExecuteRequestAsync(listReq);
        var listSent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        var listedProfiles = listSent[0].RootElement.GetProperty("result").GetProperty("profiles").EnumerateArray().ToList();
        var listedCodex = listedProfiles.Single(profile => profile.GetProperty("name").GetString() == "codex-cli");
        Assert.True(listedCodex.GetProperty("isBuiltIn").GetBoolean());
        Assert.True(listedCodex.GetProperty("hasWorkspaceOverride").GetBoolean());
        Assert.Equal(600, listedCodex.GetProperty("definition").GetProperty("timeout").GetInt32());
    }

    [Fact]
    public async Task Remove_BuiltinOverride_RestoresDefaults()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(BuildUpsertRequest(harness, "codex-cli", timeout: 600));
        await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));

        var removeReq = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentProfileRemove, new { name = "codex-cli" });
        await harness.ExecuteRequestAsync(removeReq);
        var removeSent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AppServerTestHarness.AssertIsSuccessResponse(removeSent[0]);

        var listReq = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentProfileList, new { });
        await harness.ExecuteRequestAsync(listReq);
        var listSent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        var profiles = listSent[0].RootElement.GetProperty("result").GetProperty("profiles").EnumerateArray().ToList();
        var codex = profiles.Single(profile => profile.GetProperty("name").GetString() == "codex-cli");
        Assert.False(codex.GetProperty("hasWorkspaceOverride").GetBoolean());
        Assert.Equal(300, codex.GetProperty("definition").GetProperty("timeout").GetInt32());
    }

    [Fact]
    public async Task SetEnabled_RejectsProtectedDefaultProfile()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();

        var req = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentProfileSetEnabled, new
        {
            name = "native",
            enabled = false
        });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AppServerTestHarness.AssertIsErrorResponse(sent[0], AppServerErrors.SubAgentProfileProtectedCode);
    }

    [Fact]
    public async Task SetEnabled_PersistsDisabledProfiles_AndListReflectsDisabledState()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();

        var req = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentProfileSetEnabled, new
        {
            name = "cursor-cli",
            enabled = false
        });
        await harness.ExecuteRequestAsync(req);
        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AppServerTestHarness.AssertIsSuccessResponse(sent[0]);

        var configPath = Path.Combine(_workspaceCraftPath, "config.json");
        var json = await File.ReadAllTextAsync(configPath);
        Assert.Contains("\"DisabledProfiles\"", json, StringComparison.Ordinal);
        Assert.Contains("\"cursor-cli\"", json, StringComparison.Ordinal);

        var profile = sent[0].RootElement.GetProperty("result").GetProperty("profile");
        Assert.False(profile.GetProperty("enabled").GetBoolean());
        Assert.True(profile.GetProperty("diagnostic").GetProperty("hiddenFromPrompt").GetBoolean());
    }


    [Fact]
    public async Task SettingsUpdate_ProviderPreferences_PersistsUnderSubAgentSection_AndListReflects()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();

        var updateReq = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentSettingsUpdate, new
        {
            providerPreferences = new Dictionary<string, ModelPreference>
            {
                ["openai"] = ModelPreferenceRules.CreateManual("gpt-x"),
                ["anthropic-main"] = ModelPreferenceRules.CreateManual("claude-y")
            }
        });
        await harness.ExecuteRequestAsync(updateReq);

        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AppServerTestHarness.AssertIsSuccessResponse(sent[0]);
        var settings = sent[0].RootElement.GetProperty("result").GetProperty("settings");
        var resultPreferences = settings.GetProperty("providerPreferences");
        Assert.Equal("gpt-x", resultPreferences.GetProperty("openai").GetProperty("model").GetString());
        Assert.Equal("claude-y", resultPreferences.GetProperty("anthropic-main").GetProperty("model").GetString());

        var configPath = Path.Combine(_workspaceCraftPath, "config.json");
        var root = JsonNode.Parse(await File.ReadAllTextAsync(configPath))!.AsObject();
        var persisted = root["SubAgent"]!["ProviderPreferences"]!.AsObject();
        Assert.Equal("gpt-x", persisted["openai"]!["Model"]!.GetValue<string>());
        Assert.Equal("claude-y", persisted["anthropic-main"]!["Model"]!.GetValue<string>());

        var listReq = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentProfileList, new { });
        await harness.ExecuteRequestAsync(listReq);
        var listSent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        var listedSettings = listSent[0].RootElement.GetProperty("result").GetProperty("settings");
        Assert.Equal("gpt-x", listedSettings.GetProperty("providerPreferences").GetProperty("openai").GetProperty("model").GetString());
    }

    [Fact]
    public async Task SettingsUpdate_ProviderPreferencesEmpty_RemovesKey()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentSettingsUpdate, new
        {
            providerPreferences = new Dictionary<string, ModelPreference> { ["openai"] = ModelPreferenceRules.CreateManual("gpt-x") }
        }));
        await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentSettingsUpdate, new
        {
            providerPreferences = new Dictionary<string, ModelPreference>()
        }));
        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AppServerTestHarness.AssertIsSuccessResponse(sent[0]);
        var settings = sent[0].RootElement.GetProperty("result").GetProperty("settings");
        Assert.False(settings.TryGetProperty("providerPreferences", out _));

        var configPath = Path.Combine(_workspaceCraftPath, "config.json");
        var root = JsonNode.Parse(await File.ReadAllTextAsync(configPath))!.AsObject();
        var subAgentNode = root.TryGetPropertyValue("SubAgent", out var node) ? node as JsonObject : null;
        Assert.True(subAgentNode is null || !subAgentNode.ContainsKey("ProviderPreferences"));
    }

    [Fact]
    public async Task SettingsUpdate_ProviderPreferences_PreservesUnrelatedFields()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();

        var configPath = Path.Combine(_workspaceCraftPath, "config.json");
        await File.WriteAllTextAsync(configPath, "{\"SubAgent\":{\"CustomSettings\":{\"keep\":true}}}");

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentSettingsUpdate, new
        {
            providerPreferences = new Dictionary<string, ModelPreference> { ["openai"] = ModelPreferenceRules.CreateManual("gpt-x") }
        }));
        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AppServerTestHarness.AssertIsSuccessResponse(sent[0]);
        var settings = sent[0].RootElement.GetProperty("result").GetProperty("settings");
        Assert.Equal("gpt-x", settings.GetProperty("providerPreferences").GetProperty("openai").GetProperty("model").GetString());

        var root = JsonNode.Parse(await File.ReadAllTextAsync(configPath))!.AsObject();
        Assert.True(root["SubAgent"]!["CustomSettings"]!["keep"]!.GetValue<bool>());
        Assert.Equal("gpt-x", root["SubAgent"]!["ProviderPreferences"]!["openai"]!["Model"]!.GetValue<string>());
    }

    [Fact]
    public async Task SettingsUpdate_OtherSetting_PreservesUnrelatedFields()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();

        var configPath = Path.Combine(_workspaceCraftPath, "config.json");
        await File.WriteAllTextAsync(configPath, "{\"SubAgent\":{\"CustomSettings\":{\"keep\":true}}}");

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentSettingsUpdate,
            new JsonObject { ["externalCliSessionResumeEnabled"] = true }));
        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AppServerTestHarness.AssertIsSuccessResponse(sent[0]);

        var json = await File.ReadAllTextAsync(configPath);
        var root = JsonNode.Parse(json)!.AsObject();
        Assert.True(root["SubAgent"]!["CustomSettings"]!["keep"]!.GetValue<bool>());
    }

    [Fact]
    public async Task SettingsUpdate_WaitTimeouts_PersistsWorkspaceSettings_AndListReflectsSetting()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();

        var updateReq = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentSettingsUpdate, new
        {
            minWaitTimeoutMs = 500,
            defaultWaitTimeoutMs = 1000,
            maxWaitTimeoutMs = 2000
        });
        await harness.ExecuteRequestAsync(updateReq);

        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AppServerTestHarness.AssertIsSuccessResponse(sent[0]);
        var settings = sent[0].RootElement.GetProperty("result").GetProperty("settings");
        Assert.Equal(500, settings.GetProperty("minWaitTimeoutMs").GetInt32());
        Assert.Equal(1000, settings.GetProperty("defaultWaitTimeoutMs").GetInt32());
        Assert.Equal(2000, settings.GetProperty("maxWaitTimeoutMs").GetInt32());

        var configPath = Path.Combine(_workspaceCraftPath, "config.json");
        var json = await File.ReadAllTextAsync(configPath);
        var root = JsonNode.Parse(json)!.AsObject();
        Assert.Equal(500, root["SubAgent"]!["MinWaitTimeoutMs"]!.GetValue<int>());
        Assert.Equal(1000, root["SubAgent"]!["DefaultWaitTimeoutMs"]!.GetValue<int>());
        Assert.Equal(2000, root["SubAgent"]!["MaxWaitTimeoutMs"]!.GetValue<int>());

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentProfileSetEnabled, new
        {
            name = "codex-cli",
            enabled = false
        }));
        await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        json = await File.ReadAllTextAsync(configPath);
        root = JsonNode.Parse(json)!.AsObject();
        Assert.Equal(500, root["SubAgent"]!["MinWaitTimeoutMs"]!.GetValue<int>());
        Assert.Equal(1000, root["SubAgent"]!["DefaultWaitTimeoutMs"]!.GetValue<int>());
        Assert.Equal(2000, root["SubAgent"]!["MaxWaitTimeoutMs"]!.GetValue<int>());

        var listReq = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentProfileList, new { });
        await harness.ExecuteRequestAsync(listReq);
        var listSent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        var listedSettings = listSent[0].RootElement.GetProperty("result").GetProperty("settings");
        Assert.Equal(500, listedSettings.GetProperty("minWaitTimeoutMs").GetInt32());
        Assert.Equal(1000, listedSettings.GetProperty("defaultWaitTimeoutMs").GetInt32());
        Assert.Equal(2000, listedSettings.GetProperty("maxWaitTimeoutMs").GetInt32());
    }

    [Fact]
    public async Task SettingsUpdate_WaitTimeoutsRejectsInvalidRange()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();

        var updateReq = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentSettingsUpdate, new
        {
            minWaitTimeoutMs = 2000,
            defaultWaitTimeoutMs = 1000,
            maxWaitTimeoutMs = 3000
        });
        await harness.ExecuteRequestAsync(updateReq);

        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AppServerTestHarness.AssertIsErrorResponse(sent[0], AppServerErrors.InvalidParamsCode);
    }

    [Fact]
    public async Task List_ContributedRuntimeProfileIsNotHiddenAsUnregistered()
    {
        var registry = new ContributionRegistry();
        registry.Add<ISubAgentRuntimeSource>(new StubRuntimeContribution());
        using var harness = new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            contributions: registry);
        await harness.InitializeAsync();

        var req = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentProfileList, new { });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AppServerTestHarness.AssertIsSuccessResponse(sent[0]);

        var profiles = sent[0].RootElement.GetProperty("result").GetProperty("profiles").EnumerateArray().ToList();
        var contributed = profiles.Single(profile => profile.GetProperty("name").GetString() == StubRuntimeContribution.ProfileName);
        var diagnostic = contributed.GetProperty("diagnostic");
        Assert.True(diagnostic.GetProperty("enabled").GetBoolean());
        Assert.False(diagnostic.GetProperty("hiddenFromPrompt").GetBoolean());
        Assert.Empty(diagnostic.GetProperty("warnings").EnumerateArray());
    }

    private sealed class StubRuntimeContribution : ISubAgentRuntimeSource
    {
        public const string RuntimeTypeName = "stub-remote";
        public const string ProfileName = "stub-remote-review";

        public ISubAgentRuntime Runtime { get; } = new StubRuntime();

        public IReadOnlyList<SubAgentProfile> Profiles { get; } =
        [
            new() { Name = ProfileName, Runtime = RuntimeTypeName, WorkingDirectoryMode = "workspace" }
        ];
    }

    private sealed class StubRuntime : ISubAgentRuntime
    {
        public string RuntimeType => StubRuntimeContribution.RuntimeTypeName;

        public Task<SubAgentSessionHandle> CreateSessionAsync(
            SubAgentProfile profile,
            SubAgentLaunchContext context,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SubAgentSessionHandle(RuntimeType, profile.Name));

        public Task<SubAgentRunResult> RunAsync(
            SubAgentSessionHandle session,
            SubAgentTaskRequest request,
            ISubAgentEventSink sink,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SubAgentRunResult { Text = request.Task });

        public Task CancelAsync(SubAgentSessionHandle session, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DisposeSessionAsync(SubAgentSessionHandle session, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private static AppServerIncomingMessage BuildUpsertRequest(
        AppServerTestHarness harness,
        string name,
        int timeout)
    {
        return harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentProfileUpsert, new
        {
            name,
            definition = new
            {
                runtime = "cli-oneshot",
                bin = "codex",
                args = new[] { "exec", "--skip-git-repo-check" },
                workingDirectoryMode = "workspace",
                inputMode = "arg",
                outputFormat = "text",
                outputFileArgTemplate = "--output-last-message {path}",
                readOutputFile = true,
                deleteOutputFileAfterRead = true,
                supportsStreaming = false,
                supportsResume = false,
                timeout,
                maxOutputBytes = 1048576,
                trustLevel = "prompt"
            }
        });
    }
}
