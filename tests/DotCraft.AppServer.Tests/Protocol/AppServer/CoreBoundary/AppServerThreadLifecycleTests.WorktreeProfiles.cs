using DotCraft.Agents;
using DotCraft.AppServer;
using DotCraft.Memory;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed partial class AppServerThreadLifecycleTests
{
    [Fact]
    public async Task WorktreeCreateAndStart_WithAgentProfile_AppliesTheResolvedProfile()
    {
        using var craft = new TempCraftDirectory();
        var craftPath = craft.Path;
        using var harness = new CoreAppServerTestHarness(workspaceCraftPath: craftPath);
        await harness.InitializeAsync();
        InitializeGitWorkspace(harness.Identity.WorkspacePath);
        await UpsertWorkspaceProfileAsync(harness, "Night Shift", "memory: own\npermissions:\n  approvalPolicy: autoApprove");

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.WorktreeCreateAndStart, new
        {
            identity = new { channelName = "appserver", userId = "test_user", workspacePath = harness.Identity.WorkspacePath },
            config = new { agentProfileId = "Night Shift" },
            branchName = "dotcraft/profile-start",
            copyDirtyChanges = false
        }));

        string threadId;
        using (var response = await harness.Transport.ReadNextSentAsync())
        {
            CoreAppServerTestHarness.AssertIsSuccessResponse(response);
            var result = response.RootElement.GetProperty("result");
            var thread = result.GetProperty("thread");
            var worktree = result.GetProperty("worktree");
            threadId = thread.GetProperty("id").GetString()!;
            Assert.Equal(worktree.GetProperty("path").GetString(), thread.GetProperty("effectiveWorkspacePath").GetString());
            Assert.Equal("dotcraft/profile-start", worktree.GetProperty("branchName").GetString());
        }

        using (var notification = await harness.Transport.ReadNextSentAsync())
        {
            CoreAppServerTestHarness.AssertIsNotification(notification, DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStarted);
            Assert.Equal(threadId, notification.RootElement.GetProperty("params").GetProperty("thread").GetProperty("id").GetString());
        }

        var configuration = (await harness.Service.GetThreadAsync(threadId)).Configuration!;
        Assert.Equal("Night Shift", configuration.AgentProfileId);
        Assert.Equal("workspace", configuration.AgentProfileSource);
        Assert.Equal(ApprovalPolicy.AutoApprove, configuration.ApprovalPolicy);
        Assert.Equal(
            Path.Combine(craftPath, "scopes", AgentProfileName.MemoryScope("Night Shift"), "memory"),
            MemoryScopes.StoreDirectory(configuration, craftPath));
    }

    [Fact]
    public async Task WorktreeCreateAndStart_WithUnknownAgentProfile_FailsBeforeCreatingTheWorktree()
    {
        using var craft = new TempCraftDirectory();
        var craftPath = craft.Path;
        using var harness = new CoreAppServerTestHarness(workspaceCraftPath: craftPath);
        await harness.InitializeAsync();
        InitializeGitWorkspace(harness.Identity.WorkspacePath);

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.WorktreeCreateAndStart, new
        {
            identity = new { channelName = "appserver", userId = "test_user", workspacePath = harness.Identity.WorkspacePath },
            config = new { agentProfileId = "Missing" },
            branchName = "dotcraft/profile-missing",
            copyDirtyChanges = false
        }));

        using var response = await harness.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.AgentProfileNotFoundCode);
        var worktrees = Path.Combine(harness.Identity.WorkspacePath, ".craft", "worktrees");
        Assert.False(Directory.Exists(worktrees) && Directory.EnumerateFileSystemEntries(worktrees).Any());
    }

    private sealed class TempCraftDirectory : IDisposable
    {
        private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WorktreeProfile_" + Guid.NewGuid().ToString("N")[..8]);

        public TempCraftDirectory() => Directory.CreateDirectory(Path);

        public string Path => System.IO.Path.Combine(_root, ".craft");

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
    }

    private static async Task UpsertWorkspaceProfileAsync(CoreAppServerTestHarness harness, string name, string frontmatter)
    {
        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.AgentProfileUpsert, new
        {
            id = name,
            source = "workspace",
            rawContent = $"---\nname: {name}\ndescription: Test profile\n{frontmatter}\n---\n\nBody.\n"
        }));
        using var response = await harness.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(response);
    }
}
