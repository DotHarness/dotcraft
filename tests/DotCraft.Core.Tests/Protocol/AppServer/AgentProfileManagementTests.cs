using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Tools;
using System.Text.Json;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AgentProfileManagementTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"agent_profile_mgmt_{Guid.NewGuid():N}");
    private readonly string _workspaceCraftPath;

    public AgentProfileManagementTests()
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
    public async Task CrudMethods_RoundTripRawMarkdownAndDiagnostics()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();

        var raw = ProfileMarkdown("reviewer-lite", "Review with read-focused defaults");
        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.AgentProfileValidate, new
        {
            rawContent = raw,
            source = "workspace"
        }));
        using (var validateResponse = await harness.Transport.ReadNextSentAsync())
        {
            AppServerTestHarness.AssertIsSuccessResponse(validateResponse);
            var result = validateResponse.RootElement.GetProperty("result");
            Assert.True(result.GetProperty("valid").GetBoolean());
            Assert.Equal("reviewer-lite", result.GetProperty("summary").GetProperty("id").GetString());
            Assert.Equal("reviewer-lite", result.GetProperty("compiledConfig").GetProperty("agentProfileId").GetString());
        }

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.AgentProfileUpsert, new
        {
            id = "reviewer-lite",
            source = "workspace",
            rawContent = raw
        }));
        using (var upsertResponse = await harness.Transport.ReadNextSentAsync())
        {
            AppServerTestHarness.AssertIsSuccessResponse(upsertResponse);
            var profile = upsertResponse.RootElement.GetProperty("result").GetProperty("profile");
            Assert.True(profile.GetProperty("valid").GetBoolean());
            Assert.Equal("workspace", profile.GetProperty("source").GetString());
            Assert.Equal(raw, profile.GetProperty("rawContent").GetString());
        }

        Assert.True(File.Exists(Path.Combine(_workspaceCraftPath, "agents", "reviewer-lite.md")));

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.AgentProfileList, new { }));
        using (var listResponse = await harness.Transport.ReadNextSentAsync())
        {
            AppServerTestHarness.AssertIsSuccessResponse(listResponse);
            var profiles = listResponse.RootElement.GetProperty("result").GetProperty("profiles").EnumerateArray().ToList();
            Assert.Contains(profiles, profile =>
                profile.GetProperty("id").GetString() == "reviewer-lite"
                && profile.GetProperty("source").GetString() == "workspace"
                && profile.GetProperty("valid").GetBoolean());
        }

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.AgentProfileRead, new
        {
            id = "reviewer-lite"
        }));
        using (var readResponse = await harness.Transport.ReadNextSentAsync())
        {
            AppServerTestHarness.AssertIsSuccessResponse(readResponse);
            var profile = readResponse.RootElement.GetProperty("result").GetProperty("profile");
            Assert.Equal(raw, profile.GetProperty("rawContent").GetString());
            Assert.Equal("Review with read-focused defaults", profile.GetProperty("description").GetString());
        }

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.AgentProfileRemove, new
        {
            id = "reviewer-lite",
            source = "workspace"
        }));
        using (var removeResponse = await harness.Transport.ReadNextSentAsync())
        {
            AppServerTestHarness.AssertIsSuccessResponse(removeResponse);
            Assert.True(removeResponse.RootElement.GetProperty("result").GetProperty("removed").GetBoolean());
        }
    }

    [Fact]
    public async Task Upsert_RejectsBuiltInProfileSource()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.AgentProfileUpsert, new
        {
            id = "team-reviewer",
            source = "builtIn",
            rawContent = ProfileMarkdown("team-reviewer", "Built-in override")
        }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.AgentProfileProtectedCode);
    }

    [Fact]
    public async Task CrudMethods_SurfaceAvatarMetadata()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();

        var raw = """
---
name: avatar-bot
description: Uses a persisted avatar
avatar: 278
model: inherit
---

Avatar body.
""";

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.AgentProfileUpsert, new
        {
            id = "avatar-bot",
            source = "workspace",
            rawContent = raw
        }));

        using (var upsertResponse = await harness.Transport.ReadNextSentAsync())
        {
            AppServerTestHarness.AssertIsSuccessResponse(upsertResponse);
            Assert.Equal(
                AgentProfileAvatarCodec.Encode(6, 1, 2),
                upsertResponse.RootElement.GetProperty("result").GetProperty("profile").GetProperty("avatar").GetInt32());
        }

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.AgentProfileList, new { }));
        using (var listResponse = await harness.Transport.ReadNextSentAsync())
        {
            AppServerTestHarness.AssertIsSuccessResponse(listResponse);
            var profile = listResponse.RootElement.GetProperty("result").GetProperty("profiles")
                .EnumerateArray()
                .Single(profile => profile.GetProperty("id").GetString() == "avatar-bot");
            Assert.Equal(AgentProfileAvatarCodec.Encode(6, 1, 2), profile.GetProperty("avatar").GetInt32());
        }

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.AgentProfileRead, new
        {
            id = "avatar-bot"
        }));

        using var readResponse = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(readResponse);
        Assert.Equal(
            AgentProfileAvatarCodec.Encode(6, 1, 2),
            readResponse.RootElement.GetProperty("result")
                .GetProperty("profile")
                .GetProperty("avatar")
                .GetInt32());
    }

    [Fact]
    public async Task ThreadStart_WithAgentProfile_PersistsResolvedConfigurationSnapshot()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();

        var raw = ProfileMarkdown("reviewer-lite", "Review with read-focused defaults");
        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.AgentProfileUpsert, new
        {
            id = "reviewer-lite",
            source = "workspace",
            rawContent = raw
        }));
        await harness.Transport.ReadNextSentAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.ThreadStart, new
        {
            identity = new { channelName = "appserver", userId = "test_user", workspacePath = harness.Identity.WorkspacePath },
            config = new
            {
                agentProfileId = "reviewer-lite",
                mode = "agent",
                useToolProfileOnly = false,
                overrideBasePrompt = false,
                approvalPolicy = "default",
                model = "gpt-4o-mini"
            }
        }));

        string threadId;
        string fingerprint;
        using (var startResponse = await harness.Transport.ReadNextSentAsync())
        {
            AppServerTestHarness.AssertIsSuccessResponse(startResponse);
            var config = startResponse.RootElement.GetProperty("result").GetProperty("thread").GetProperty("configuration");
            threadId = startResponse.RootElement.GetProperty("result").GetProperty("thread").GetProperty("id").GetString()!;
            Assert.Equal("reviewer-lite", config.GetProperty("agentProfileId").GetString());
            Assert.Equal("workspace", config.GetProperty("agentProfileSource").GetString());
            fingerprint = config.GetProperty("agentProfileFingerprint").GetString()!;
            Assert.StartsWith("sha256:", fingerprint);
            Assert.Equal("gpt-4o-mini", config.GetProperty("model").GetString());
            Assert.Equal("plan", config.GetProperty("mode").GetString());
            Assert.Equal("Profile body for reviewer-lite.", config.GetProperty("roleInstructions").GetString());
            Assert.Equal("WriteFile", config.GetProperty("toolPolicy").GetProperty("deny")[0].GetString());
            Assert.Equal("interrupt", config.GetProperty("approvalPolicy").GetString());
        }

        await harness.Transport.ReadNextSentAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.AgentProfileRemove, new
        {
            id = "reviewer-lite",
            source = "workspace"
        }));
        await harness.Transport.ReadNextSentAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.ThreadRead, new
        {
            threadId
        }));
        using var readResponse = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(readResponse);
        var persistedConfig = readResponse.RootElement.GetProperty("result").GetProperty("thread").GetProperty("configuration");
        Assert.Equal("reviewer-lite", persistedConfig.GetProperty("agentProfileId").GetString());
        Assert.Equal(fingerprint, persistedConfig.GetProperty("agentProfileFingerprint").GetString());
        Assert.Equal("Profile body for reviewer-lite.", persistedConfig.GetProperty("roleInstructions").GetString());
    }

    [Fact]
    public async Task ThreadStart_WithUnsupportedProfileOverlay_ReturnsValidationError()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.AgentProfileUpsert, new
        {
            id = "reviewer-lite",
            source = "workspace",
            rawContent = ProfileMarkdown("reviewer-lite", "Review with read-focused defaults")
        }));
        await harness.Transport.ReadNextSentAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.ThreadStart, new
        {
            identity = new { channelName = "appserver", userId = "test_user", workspacePath = harness.Identity.WorkspacePath },
            config = new
            {
                agentProfileId = "reviewer-lite",
                toolPolicy = new { allow = new[] { "WriteFile" } }
            }
        }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.AgentProfileValidationFailedCode);
    }

    [Fact]
    public async Task RefreshThread_UpdatesProfileSnapshotAndWritesAudit()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.AgentProfileUpsert, new
        {
            id = "reviewer-lite",
            source = "workspace",
            rawContent = ProfileMarkdown("reviewer-lite", "Initial reviewer", "Initial body.")
        }));
        await harness.Transport.ReadNextSentAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.ThreadStart, new
        {
            identity = new { channelName = "appserver", userId = "test_user", workspacePath = harness.Identity.WorkspacePath },
            config = new
            {
                agentProfileId = "reviewer-lite"
            }
        }));

        string threadId;
        string oldFingerprint;
        using (var startResponse = await harness.Transport.ReadNextSentAsync())
        {
            AppServerTestHarness.AssertIsSuccessResponse(startResponse);
            var thread = startResponse.RootElement.GetProperty("result").GetProperty("thread");
            threadId = thread.GetProperty("id").GetString()!;
            oldFingerprint = thread.GetProperty("configuration").GetProperty("agentProfileFingerprint").GetString()!;
        }
        await harness.Transport.ReadNextSentAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.AgentProfileUpsert, new
        {
            id = "reviewer-lite",
            source = "workspace",
            rawContent = ProfileMarkdown("reviewer-lite", "Initial reviewer", "Updated body.")
        }));
        await harness.Transport.ReadNextSentAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.AgentProfileList, new { }));
        using (var listResponse = await harness.Transport.ReadNextSentAsync())
        {
            AppServerTestHarness.AssertIsSuccessResponse(listResponse);
            var profile = listResponse.RootElement.GetProperty("result").GetProperty("profiles").EnumerateArray()
                .Single(item => item.GetProperty("id").GetString() == "reviewer-lite"
                                && item.GetProperty("source").GetString() == "workspace");
            Assert.Contains(threadId, profile.GetProperty("staleThreadIds").EnumerateArray().Select(item => item.GetString()));
        }

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.AgentProfileRefreshThread, new
        {
            threadId
        }));
        string newFingerprint;
        using (var refreshResponse = await harness.Transport.ReadNextSentAsync())
        {
            AppServerTestHarness.AssertIsSuccessResponse(refreshResponse);
            var result = refreshResponse.RootElement.GetProperty("result");
            Assert.True(result.GetProperty("wasStale").GetBoolean());
            Assert.Equal("AgentProfileThreadRefreshed", result.GetProperty("audit").GetProperty("code").GetString());
            var config = result.GetProperty("config");
            Assert.Equal("Updated body.", config.GetProperty("roleInstructions").GetString());
            newFingerprint = config.GetProperty("agentProfileFingerprint").GetString()!;
            Assert.NotEqual(oldFingerprint, newFingerprint);
        }

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.ThreadRead, new
        {
            threadId
        }));
        using (var readResponse = await harness.Transport.ReadNextSentAsync())
        {
            AppServerTestHarness.AssertIsSuccessResponse(readResponse);
            var config = readResponse.RootElement.GetProperty("result").GetProperty("thread").GetProperty("configuration");
            Assert.Equal(newFingerprint, config.GetProperty("agentProfileFingerprint").GetString());
            Assert.Equal("Updated body.", config.GetProperty("roleInstructions").GetString());
        }

        var auditPath = Path.Combine(_workspaceCraftPath, "agents", "audit.jsonl");
        Assert.True(File.Exists(auditPath));
        Assert.Contains(
            File.ReadAllLines(auditPath).Select(line => JsonDocument.Parse(line)).ToList(),
            document => document.RootElement.GetProperty("code").GetString() == "AgentProfileThreadRefreshed"
                        && document.RootElement.GetProperty("threadId").GetString() == threadId);
    }

    [Fact]
    public async Task BuilderDraftMethods_RejectOrdinaryThread()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.AgentProfileBuilderDraftRead, new
        {
            threadId = thread.Id
        }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.InvalidParamsCode);
    }

    [Fact]
    public async Task BuilderDraftUpdate_SeedsAndReadReturnsSameDraft()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(
            harness.Identity,
            new ThreadConfiguration
            {
                AgentBuilderTargetId = "draft-agent",
                AgentBuilderTargetSource = "workspace"
            });
        var raw = ProfileMarkdown("draft-agent", "Draft synced from editor", "Synced builder draft.");

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.AgentProfileBuilderDraftUpdate, new
        {
            threadId = thread.Id,
            rawContent = raw
        }));

        using (var updateResponse = await harness.Transport.ReadNextSentAsync())
        {
            AppServerTestHarness.AssertIsSuccessResponse(updateResponse);
            var result = updateResponse.RootElement.GetProperty("result");
            Assert.Equal(thread.Id, result.GetProperty("threadId").GetString());
            Assert.Equal("draft-agent", result.GetProperty("targetId").GetString());
            Assert.Equal("workspace", result.GetProperty("targetSource").GetString());
            Assert.Equal(raw, result.GetProperty("rawContent").GetString());
        }

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.AgentProfileBuilderDraftRead, new
        {
            threadId = thread.Id
        }));

        using var readResponse = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(readResponse);
        Assert.Equal(
            raw,
            readResponse.RootElement.GetProperty("result").GetProperty("rawContent").GetString());

        ProfileBuilderDraftStore.Remove(thread.Id);
    }

    [Fact]
    public async Task BuilderPromptProvider_UsesDraftSyncedThroughAppServer()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(
            harness.Identity,
            new ThreadConfiguration
            {
                AgentBuilderTargetId = "draft-agent",
                AgentBuilderTargetSource = "workspace"
            });
        var raw = ProfileMarkdown("draft-agent", "Draft synced from editor", "Prompt-visible instructions.");

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.AgentProfileBuilderDraftUpdate, new
        {
            threadId = thread.Id,
            rawContent = raw
        }));
        await harness.Transport.ReadNextSentAsync();

        var section = new ProfileBuilderSystemPromptProvider()
            .GetSystemPromptSection(new ThreadSystemPromptContext(thread.Id, harness.Identity.WorkspacePath));

        Assert.NotNull(section);
        Assert.Contains("Prompt-visible instructions.", section);
        Assert.Contains("draft-agent", section);

        ProfileBuilderDraftStore.Remove(thread.Id);
    }

    private static string ProfileMarkdown(string id, string description, string? body = null) =>
        $"""
---
name: {id}
description: {description}
model: inherit
mode: plan
tools:
  deny: [WriteFile]
  agentControl: disabled
skills:
  allowManage: false
permissions:
  approvalPolicy: interrupt
teams:
  reservedTools: keep
---

{body ?? $"Profile body for {id}."}
""";
}
