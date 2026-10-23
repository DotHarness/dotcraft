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
        }
    }

    [Fact]
    public async Task ProfileOverrides_AreListedPersistedAndRemovedBackToBuiltInDefaults()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();

        var initial = await ListAsync(harness);
        Assert.Equal("native", initial.GetProperty("defaultName").GetString());
        var initialProfiles = Profiles(initial);
        Assert.Contains("native", initialProfiles.Keys);
        Assert.Contains("cursor-cli", initialProfiles.Keys);
        var template = initialProfiles["custom-cli-oneshot"];
        Assert.True(template.GetProperty("isBuiltIn").GetBoolean());
        Assert.True(template.GetProperty("isTemplate").GetBoolean());
        Assert.False(template.GetProperty("hasWorkspaceOverride").GetBoolean());
        Assert.Equal(300, initialProfiles["codex-cli"].GetProperty("definition").GetProperty("timeout").GetInt32());

        await harness.ExecuteRequestAsync(BuildUpsertRequest(harness, "codex-cli", timeout: 600));
        AppServerTestHarness.AssertIsSuccessResponse((await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5)))[0]);
        var root = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(_workspaceCraftPath, "config.json")))!.AsObject();
        Assert.Equal(600, root["SubAgentProfiles"]!["codex-cli"]!["Timeout"]?.GetValue<int>());
        var overridden = Profiles(await ListAsync(harness))["codex-cli"];
        Assert.True(overridden.GetProperty("isBuiltIn").GetBoolean());
        Assert.True(overridden.GetProperty("hasWorkspaceOverride").GetBoolean());
        Assert.Equal(600, overridden.GetProperty("definition").GetProperty("timeout").GetInt32());

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentProfileRemove, new { name = "codex-cli" }));
        AppServerTestHarness.AssertIsSuccessResponse((await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5)))[0]);
        var restored = Profiles(await ListAsync(harness))["codex-cli"];
        Assert.False(restored.GetProperty("hasWorkspaceOverride").GetBoolean());
        Assert.Equal(300, restored.GetProperty("definition").GetProperty("timeout").GetInt32());
    }

    [Fact]
    public async Task SetEnabled_PersistsDisabledProfilesButRejectsTheProtectedDefault()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentProfileSetEnabled, new
        {
            name = "native",
            enabled = false
        }));
        AppServerTestHarness.AssertIsErrorResponse(
            (await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5)))[0],
            AppServerErrors.SubAgentProfileProtectedCode);

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentProfileSetEnabled, new
        {
            name = "cursor-cli",
            enabled = false
        }));
        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AppServerTestHarness.AssertIsSuccessResponse(sent[0]);

        var json = await File.ReadAllTextAsync(Path.Combine(_workspaceCraftPath, "config.json"));
        Assert.Contains("\"DisabledProfiles\"", json, StringComparison.Ordinal);
        Assert.Contains("\"cursor-cli\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"native\"", json, StringComparison.Ordinal);
        var profile = sent[0].RootElement.GetProperty("result").GetProperty("profile");
        Assert.False(profile.GetProperty("enabled").GetBoolean());
        Assert.True(profile.GetProperty("diagnostic").GetProperty("hiddenFromPrompt").GetBoolean());
    }

    [Fact]
    public async Task SettingsUpdate_PersistsProviderPreferencesUnderSubAgentPreservingUnrelatedFields()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();
        var configPath = Path.Combine(_workspaceCraftPath, "config.json");
        await File.WriteAllTextAsync(configPath, "{\"SubAgent\":{\"CustomSettings\":{\"keep\":true}}}");

        var settings = await UpdateSettingsAsync(harness, new
        {
            providerPreferences = new Dictionary<string, ModelPreference>
            {
                ["openai"] = ModelPreferenceRules.CreateManual("gpt-x"),
                ["anthropic-main"] = ModelPreferenceRules.CreateManual("claude-y")
            }
        });
        Assert.Equal("gpt-x", settings.GetProperty("providerPreferences").GetProperty("openai").GetProperty("model").GetString());
        Assert.Equal("claude-y", settings.GetProperty("providerPreferences").GetProperty("anthropic-main").GetProperty("model").GetString());
        var root = JsonNode.Parse(await File.ReadAllTextAsync(configPath))!.AsObject();
        Assert.True(root["SubAgent"]!["CustomSettings"]!["keep"]!.GetValue<bool>());
        Assert.Equal("gpt-x", root["SubAgent"]!["ProviderPreferences"]!["openai"]!["Model"]!.GetValue<string>());
        Assert.Equal("claude-y", root["SubAgent"]!["ProviderPreferences"]!["anthropic-main"]!["Model"]!.GetValue<string>());
        Assert.Equal(
            "gpt-x",
            (await ListAsync(harness)).GetProperty("settings").GetProperty("providerPreferences").GetProperty("openai").GetProperty("model").GetString());

        await UpdateSettingsAsync(harness, new JsonObject { ["externalCliSessionResumeEnabled"] = true });
        root = JsonNode.Parse(await File.ReadAllTextAsync(configPath))!.AsObject();
        Assert.True(root["SubAgent"]!["CustomSettings"]!["keep"]!.GetValue<bool>());

        var cleared = await UpdateSettingsAsync(harness, new { providerPreferences = new Dictionary<string, ModelPreference>() });
        Assert.False(cleared.TryGetProperty("providerPreferences", out _));
        root = JsonNode.Parse(await File.ReadAllTextAsync(configPath))!.AsObject();
        Assert.False(root["SubAgent"]!.AsObject().ContainsKey("ProviderPreferences"));
        Assert.True(root["SubAgent"]!["CustomSettings"]!["keep"]!.GetValue<bool>());
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

    private static async Task<System.Text.Json.JsonElement> ListAsync(AppServerTestHarness harness)
    {
        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentProfileList, new { }));
        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AppServerTestHarness.AssertIsSuccessResponse(sent[0]);
        return sent[0].RootElement.GetProperty("result").Clone();
    }

    private static Dictionary<string, System.Text.Json.JsonElement> Profiles(System.Text.Json.JsonElement listResult) =>
        listResult.GetProperty("profiles").EnumerateArray().ToDictionary(profile => profile.GetProperty("name").GetString()!);

    private static async Task<System.Text.Json.JsonElement> UpdateSettingsAsync(AppServerTestHarness harness, object parameters)
    {
        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentSettingsUpdate, parameters));
        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AppServerTestHarness.AssertIsSuccessResponse(sent[0]);
        return sent[0].RootElement.GetProperty("result").GetProperty("settings").Clone();
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
