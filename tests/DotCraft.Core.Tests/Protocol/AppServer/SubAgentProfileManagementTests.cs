using System.Text.Json.Nodes;
using DotCraft.Protocol.AppServer;

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

        var req = harness.BuildRequest(AppServerMethods.SubAgentProfileList, new { });
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

        var listReq = harness.BuildRequest(AppServerMethods.SubAgentProfileList, new { });
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

        var removeReq = harness.BuildRequest(AppServerMethods.SubAgentProfileRemove, new { name = "codex-cli" });
        await harness.ExecuteRequestAsync(removeReq);
        var removeSent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AppServerTestHarness.AssertIsSuccessResponse(removeSent[0]);

        var listReq = harness.BuildRequest(AppServerMethods.SubAgentProfileList, new { });
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

        var req = harness.BuildRequest(AppServerMethods.SubAgentProfileSetEnabled, new
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

        var req = harness.BuildRequest(AppServerMethods.SubAgentProfileSetEnabled, new
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
    public async Task SettingsUpdate_Model_PersistsWorkspaceModel_AndListReflectsSetting()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();

        var updateReq = harness.BuildRequest(AppServerMethods.SubAgentSettingsUpdate, new
        {
            model = "gpt-subagent"
        });
        await harness.ExecuteRequestAsync(updateReq);

        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AppServerTestHarness.AssertIsSuccessResponse(sent[0]);
        var settings = sent[0].RootElement.GetProperty("result").GetProperty("settings");
        Assert.False(settings.GetProperty("externalCliSessionResumeEnabled").GetBoolean());
        Assert.Equal("gpt-subagent", settings.GetProperty("model").GetString());

        var configPath = Path.Combine(_workspaceCraftPath, "config.json");
        var json = await File.ReadAllTextAsync(configPath);
        var root = JsonNode.Parse(json)!.AsObject();
        Assert.Equal("gpt-subagent", root["SubAgent"]!["Model"]!.GetValue<string>());

        var listReq = harness.BuildRequest(AppServerMethods.SubAgentProfileList, new { });
        await harness.ExecuteRequestAsync(listReq);
        var listSent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        var listedSettings = listSent[0].RootElement.GetProperty("result").GetProperty("settings");
        Assert.Equal("gpt-subagent", listedSettings.GetProperty("model").GetString());
    }

    [Fact]
    public async Task SettingsUpdate_ModelNull_ClearsWorkspaceModel()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.SubAgentSettingsUpdate, new
        {
            model = "gpt-subagent"
        }));
        await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            AppServerMethods.SubAgentSettingsUpdate,
            new JsonObject { ["model"] = null }));
        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AppServerTestHarness.AssertIsSuccessResponse(sent[0]);

        var settings = sent[0].RootElement.GetProperty("result").GetProperty("settings");
        Assert.False(settings.TryGetProperty("model", out _));

        var configPath = Path.Combine(_workspaceCraftPath, "config.json");
        var json = await File.ReadAllTextAsync(configPath);
        var root = JsonNode.Parse(json)!.AsObject();
        Assert.False(root.TryGetPropertyValue("SubAgent", out _));
    }

    private static AppServerIncomingMessage BuildUpsertRequest(
        AppServerTestHarness harness,
        string name,
        int timeout)
    {
        return harness.BuildRequest(AppServerMethods.SubAgentProfileUpsert, new
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
