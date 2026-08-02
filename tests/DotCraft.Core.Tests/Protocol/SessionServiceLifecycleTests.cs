using DotCraft.Mcp;
using DotCraft.Protocol;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Sessions.Protocol;

/// <summary>
/// Unit tests for ISessionService lifecycle: create, pause, resume, archive, find.
/// Uses a FakeSessionService backed by a real ThreadStore to verify the contract.
/// </summary>
public sealed class SessionServiceLifecycleTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ThreadStore _store;
    private readonly FakeSessionService _svc;

    public SessionServiceLifecycleTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dotcraft-session-lifecycle-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _store = new ThreadStore(_tempDir);
        _svc = new FakeSessionService(_store);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort */ }
    }

    // -------------------------------------------------------------------------
    // CreateThread
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateThread_ReturnsActiveThread()
    {
        var identity = MakeIdentity();
        var thread = await _svc.CreateThreadAsync(identity);

        Assert.NotNull(thread);
        Assert.False(string.IsNullOrEmpty(thread.Id));
        Assert.Equal(ThreadStatus.Active, thread.Status);
        Assert.Equal(identity.WorkspacePath, thread.WorkspacePath);
        Assert.Equal(identity.UserId, thread.UserId);
        Assert.Equal(identity.ChannelName, thread.OriginChannel);
    }

    [Fact]
    public async Task CreateThread_IsPersisted()
    {
        var thread = await _svc.CreateThreadAsync(MakeIdentity());
        var loaded = await _store.LoadThreadAsync(thread.Id);
        Assert.NotNull(loaded);
        Assert.Equal(thread.Id, loaded.Id);
    }

    [Fact]
    public async Task CreateThread_AppearsInIndex()
    {
        var thread = await _svc.CreateThreadAsync(MakeIdentity());
        var index = await _store.LoadIndexAsync();
        Assert.Contains(index, s => s.Id == thread.Id);
    }

    [Fact]
    public async Task CreateThread_InvokesThreadCreatedForBroadcast()
    {
        SessionThread? seen = null;
        _svc.ThreadCreatedForBroadcast = t => seen = t;
        var thread = await _svc.CreateThreadAsync(MakeIdentity());
        Assert.NotNull(seen);
        Assert.Equal(thread.Id, seen!.Id);
    }

    [Fact]
    public async Task DeleteThreadPermanentlyAsync_InvokesThreadDeletedForBroadcast()
    {
        string? seenId = null;
        _svc.ThreadDeletedForBroadcast = id => seenId = id;
        var thread = await _svc.CreateThreadAsync(MakeIdentity());
        await _svc.DeleteThreadPermanentlyAsync(thread.Id);
        Assert.Equal(thread.Id, seenId);
    }

    [Fact]
    public async Task RenameThreadAsync_InvokesThreadRenamedForBroadcast_WhenNameChanges()
    {
        SessionThread? seen = null;
        _svc.ThreadRenamedForBroadcast = t => seen = t;
        var thread = await _svc.CreateThreadAsync(MakeIdentity());
        await _svc.RenameThreadAsync(thread.Id, "New title");
        Assert.NotNull(seen);
        Assert.Equal(thread.Id, seen!.Id);
        Assert.Equal("New title", seen.DisplayName);
    }

    [Fact]
    public async Task RenameThreadAsync_DoesNotInvokeThreadRenamedForBroadcast_WhenNameUnchanged()
    {
        var invokes = 0;
        _svc.ThreadRenamedForBroadcast = _ => invokes++;
        var thread = await _svc.CreateThreadAsync(MakeIdentity(), displayName: "Same");
        await _svc.RenameThreadAsync(thread.Id, "Same");
        Assert.Equal(0, invokes);
    }

    [Fact]
    public async Task CreateThread_IdFormat_IsValid()
    {
        var thread = await _svc.CreateThreadAsync(MakeIdentity());
        // Format: thread_{timestamp}_{random}
        Assert.StartsWith("thread_", thread.Id);
    }

    // -------------------------------------------------------------------------
    // PauseThread
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PauseThread_SetsStatusPaused()
    {
        var thread = await _svc.CreateThreadAsync(MakeIdentity());
        await _svc.PauseThreadAsync(thread.Id);

        var loaded = await _store.LoadThreadAsync(thread.Id);
        Assert.NotNull(loaded);
        Assert.Equal(ThreadStatus.Paused, loaded.Status);
    }

    [Fact]
    public async Task PauseThread_IsIdempotent()
    {
        var thread = await _svc.CreateThreadAsync(MakeIdentity());
        await _svc.PauseThreadAsync(thread.Id);
        await _svc.PauseThreadAsync(thread.Id); // second call should not throw
        var loaded = await _store.LoadThreadAsync(thread.Id);
        Assert.NotNull(loaded);
        Assert.Equal(ThreadStatus.Paused, loaded.Status);
    }

    // -------------------------------------------------------------------------
    // ResumeThread
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ResumeThread_SetsStatusActive()
    {
        var thread = await _svc.CreateThreadAsync(MakeIdentity());
        await _svc.PauseThreadAsync(thread.Id);
        await _svc.ResumeThreadAsync(thread.Id);

        var loaded = await _store.LoadThreadAsync(thread.Id);
        Assert.NotNull(loaded);
        Assert.Equal(ThreadStatus.Active, loaded.Status);
    }

    [Fact]
    public async Task ResumeThread_AlreadyActive_DoesNotThrow()
    {
        var thread = await _svc.CreateThreadAsync(MakeIdentity());
        // Resuming an already-active thread should be a no-op
        var resumed = await _svc.ResumeThreadAsync(thread.Id);
        Assert.Equal(ThreadStatus.Active, resumed.Status);
    }

    [Fact]
    public async Task ResumeThread_Archived_Throws()
    {
        var thread = await _svc.CreateThreadAsync(MakeIdentity());
        await _svc.ArchiveThreadAsync(thread.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _svc.ResumeThreadAsync(thread.Id));
    }

    // -------------------------------------------------------------------------
    // ArchiveThread
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ArchiveThread_SetsStatusArchived()
    {
        var thread = await _svc.CreateThreadAsync(MakeIdentity());
        await _svc.ArchiveThreadAsync(thread.Id);

        var loaded = await _store.LoadThreadAsync(thread.Id);
        Assert.NotNull(loaded);
        Assert.Equal(ThreadStatus.Archived, loaded.Status);
    }

    [Fact]
    public async Task ArchiveThread_IsIdempotent()
    {
        var thread = await _svc.CreateThreadAsync(MakeIdentity());
        await _svc.ArchiveThreadAsync(thread.Id);
        await _svc.ArchiveThreadAsync(thread.Id);

        var loaded = await _store.LoadThreadAsync(thread.Id);
        Assert.NotNull(loaded);
        Assert.Equal(ThreadStatus.Archived, loaded.Status);
    }

    // -------------------------------------------------------------------------
    // UnarchiveThread
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UnarchiveThread_SetsStatusActive()
    {
        var thread = await _svc.CreateThreadAsync(MakeIdentity());
        await _svc.ArchiveThreadAsync(thread.Id);
        await _svc.UnarchiveThreadAsync(thread.Id);

        var loaded = await _store.LoadThreadAsync(thread.Id);
        Assert.NotNull(loaded);
        Assert.Equal(ThreadStatus.Active, loaded.Status);
    }

    [Fact]
    public async Task UnarchiveThread_IsIdempotent_WhenAlreadyActive()
    {
        var thread = await _svc.CreateThreadAsync(MakeIdentity());
        await _svc.UnarchiveThreadAsync(thread.Id);

        var loaded = await _store.LoadThreadAsync(thread.Id);
        Assert.NotNull(loaded);
        Assert.Equal(ThreadStatus.Active, loaded.Status);
    }

    // -------------------------------------------------------------------------
    // FindThreads
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FindThreads_ReturnsMatchingThreadsByWorkspaceAndUser()
    {
        var identity = MakeIdentity("user1", "/ws/alpha");
        await _svc.CreateThreadAsync(identity);
        await _svc.CreateThreadAsync(identity);
        await _svc.CreateThreadAsync(MakeIdentity("user2", "/ws/alpha")); // different user

        var found = await _svc.FindThreadsAsync(identity);
        Assert.Equal(2, found.Count);
        Assert.All(found, s => Assert.Equal("user1", s.UserId));
    }

    [Fact]
    public async Task FindThreads_NoMatch_ReturnsEmpty()
    {
        await _svc.CreateThreadAsync(MakeIdentity("user1", "/ws1"));

        var found = await _svc.FindThreadsAsync(MakeIdentity("user1", "/ws2"));
        Assert.Empty(found);
    }

    [Fact]
    public async Task FindThreads_ArchivedExcludedByDefault_ButIncludedWhenRequested()
    {
        var identity = MakeIdentity("user1", "/ws/alpha");
        var thread = await _svc.CreateThreadAsync(identity);
        await _svc.ArchiveThreadAsync(thread.Id);

        var defaultResults = await _svc.FindThreadsAsync(identity);
        var includeArchivedResults = await _svc.FindThreadsAsync(identity, includeArchived: true);

        Assert.Empty(defaultResults);
        Assert.Single(includeArchivedResults);
        Assert.Equal(ThreadStatus.Archived, includeArchivedResults[0].Status);
    }

    [Fact]
    public async Task FindThreads_SubAgentThreadsHiddenByDefault()
    {
        var identity = MakeIdentity("user1", "/ws/alpha");
        var parent = await _svc.CreateThreadAsync(identity);
        var childIdentity = new SessionIdentity
        {
            ChannelName = SubAgentThreadOrigin.ChannelName,
            UserId = identity.UserId,
            WorkspacePath = identity.WorkspacePath,
            ChannelContext = parent.Id
        };

        var child = await _svc.CreateThreadAsync(
            childIdentity,
            threadId: "child-thread",
            displayName: "Worker",
            source: ThreadSource.ForSubAgent(new SubAgentThreadSource
            {
                ParentThreadId = parent.Id,
                ParentTurnId = "turn_1",
                RootThreadId = parent.Id,
                Depth = 1,
                AgentNickname = "Worker",
                AgentRole = "worker"
            }));

        var defaultResults = await _svc.FindThreadsAsync(identity);
        var includeSubAgentsResults = await _svc.FindThreadsAsync(identity, includeSubAgents: true);

        Assert.Contains(defaultResults, s => s.Id == parent.Id);
        Assert.DoesNotContain(defaultResults, s => s.Id == child.Id);
        Assert.Contains(includeSubAgentsResults, s => s.Id == child.Id);
    }

    [Fact]
    public async Task ThreadSpawnEdges_ListOpenChildrenAndCanClose()
    {
        var parent = await _svc.CreateThreadAsync(MakeIdentity());
        var child = await _svc.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = SubAgentThreadOrigin.ChannelName,
            UserId = parent.UserId,
            WorkspacePath = parent.WorkspacePath,
            ChannelContext = parent.Id
        });
        await _svc.UpsertThreadSpawnEdgeAsync(new ThreadSpawnEdge
        {
            ParentThreadId = parent.Id,
            ChildThreadId = child.Id,
            ParentTurnId = "turn_1",
            Depth = 1,
            AgentPath = "/root/worker",
            TaskName = "worker",
            AgentNickname = "Worker",
            AgentRole = "worker",
            SupportsSendMessage = true,
            SupportsFollowupTask = true,
            SupportsClose = true,
            Status = ThreadSpawnEdgeStatus.Open
        });

        var open = await _svc.ListSubAgentChildrenAsync(parent.Id);
        await _svc.SetThreadSpawnEdgeStatusAsync(parent.Id, child.Id, ThreadSpawnEdgeStatus.Closed);
        var visibleAfterClose = await _svc.ListSubAgentChildrenAsync(parent.Id);
        var allAfterClose = await _svc.ListSubAgentChildrenAsync(parent.Id, includeClosed: true);

        var edge = Assert.Single(open);
        Assert.Equal(child.Id, edge.ChildThreadId);
        Assert.Equal("/root/worker", edge.AgentPath);
        Assert.Equal("worker", edge.TaskName);
        Assert.True(edge.SupportsSendMessage);
        Assert.True(edge.SupportsFollowupTask);
        Assert.Empty(visibleAfterClose);
        Assert.Equal(ThreadSpawnEdgeStatus.Closed, Assert.Single(allAfterClose).Status);
    }

    [Fact]
    public async Task ArchiveThread_ArchivesSubAgentDescendantsAndHidesThemFromActiveLists()
    {
        var identity = MakeIdentity();
        var parent = await _svc.CreateThreadAsync(identity);
        var child = await CreateSubAgentAsync(parent, "child-thread");
        var grandchild = await CreateSubAgentAsync(child, "grandchild-thread");

        await _svc.ArchiveThreadAsync(parent.Id);

        Assert.Equal(ThreadStatus.Archived, (await _store.LoadThreadAsync(parent.Id))!.Status);
        Assert.Equal(ThreadStatus.Archived, (await _store.LoadThreadAsync(child.Id))!.Status);
        Assert.Equal(ThreadStatus.Archived, (await _store.LoadThreadAsync(grandchild.Id))!.Status);

        var active = await _svc.FindThreadsAsync(identity, includeSubAgents: true);
        Assert.DoesNotContain(active, s => s.Id == parent.Id);
        Assert.DoesNotContain(active, s => s.Id == child.Id);
        Assert.DoesNotContain(active, s => s.Id == grandchild.Id);
    }

    [Fact]
    public async Task UnarchiveThread_RestoresSubAgentDescendants()
    {
        var identity = MakeIdentity();
        var parent = await _svc.CreateThreadAsync(identity);
        var child = await CreateSubAgentAsync(parent, "child-thread");
        var grandchild = await CreateSubAgentAsync(child, "grandchild-thread");
        await _svc.ArchiveThreadAsync(parent.Id);

        await _svc.UnarchiveThreadAsync(parent.Id);

        Assert.Equal(ThreadStatus.Active, (await _store.LoadThreadAsync(parent.Id))!.Status);
        Assert.Equal(ThreadStatus.Active, (await _store.LoadThreadAsync(child.Id))!.Status);
        Assert.Equal(ThreadStatus.Active, (await _store.LoadThreadAsync(grandchild.Id))!.Status);

        var active = await _svc.FindThreadsAsync(identity, includeSubAgents: true);
        Assert.Contains(active, s => s.Id == parent.Id);
        Assert.Contains(active, s => s.Id == child.Id);
        Assert.Contains(active, s => s.Id == grandchild.Id);
    }

    [Fact]
    public async Task UnarchiveThread_DoesNotRestoreClosedSubAgentDescendants()
    {
        var identity = MakeIdentity();
        var parent = await _svc.CreateThreadAsync(identity);
        var closedChild = await CreateSubAgentAsync(parent, "closed-child-thread");
        var closedGrandchild = await CreateSubAgentAsync(closedChild, "closed-grandchild-thread");
        var openChild = await CreateSubAgentAsync(parent, "open-child-thread");

        await _svc.SetThreadSpawnEdgeStatusAsync(parent.Id, closedChild.Id, ThreadSpawnEdgeStatus.Closed);
        await _svc.ArchiveSubAgentTreeForCloseAsync(closedChild.Id);
        await _svc.ArchiveThreadAsync(parent.Id);

        await _svc.UnarchiveThreadAsync(parent.Id);

        Assert.Equal(ThreadStatus.Active, (await _store.LoadThreadAsync(parent.Id))!.Status);
        Assert.Equal(ThreadStatus.Active, (await _store.LoadThreadAsync(openChild.Id))!.Status);
        Assert.Equal(ThreadStatus.Archived, (await _store.LoadThreadAsync(closedChild.Id))!.Status);
        Assert.Equal(ThreadStatus.Archived, (await _store.LoadThreadAsync(closedGrandchild.Id))!.Status);
    }

    [Fact]
    public async Task DeleteThreadPermanentlyAsync_DeletesSubAgentDescendantsAndEdges()
    {
        var parent = await _svc.CreateThreadAsync(MakeIdentity());
        var child = await CreateSubAgentAsync(parent, "child-thread");
        var grandchild = await CreateSubAgentAsync(child, "grandchild-thread");
        await _svc.ArchiveThreadAsync(parent.Id);

        await _svc.DeleteThreadPermanentlyAsync(parent.Id);

        Assert.Null(await _store.LoadThreadAsync(parent.Id));
        Assert.Null(await _store.LoadThreadAsync(child.Id));
        Assert.Null(await _store.LoadThreadAsync(grandchild.Id));
        Assert.Empty(await _store.ListSubAgentChildrenAsync(parent.Id, includeClosed: true));
        Assert.Empty(await _store.ListSubAgentChildrenAsync(child.Id, includeClosed: true));
    }

    [Fact]
    public async Task DirectSubAgentArchiveOrDelete_Throws()
    {
        var parent = await _svc.CreateThreadAsync(MakeIdentity());
        var child = await CreateSubAgentAsync(parent, "child-thread");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.ArchiveThreadAsync(child.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.DeleteThreadPermanentlyAsync(child.Id));

        Assert.NotNull(await _store.LoadThreadAsync(parent.Id));
        Assert.NotNull(await _store.LoadThreadAsync(child.Id));
    }

    [Fact]
    public async Task FindThreads_HidesActiveSubAgentWhenParentIsArchived()
    {
        var identity = MakeIdentity();
        var parent = await _svc.CreateThreadAsync(identity);
        var child = await CreateSubAgentAsync(parent, "child-thread");
        parent.Status = ThreadStatus.Archived;
        await _store.SaveThreadAsync(parent);

        var active = await _svc.FindThreadsAsync(identity, includeSubAgents: true);

        Assert.DoesNotContain(active, s => s.Id == parent.Id);
        Assert.DoesNotContain(active, s => s.Id == child.Id);
    }

    [Fact]
    public async Task FindThreads_ChannelContextIsolation_DifferentContextsReturnSeparateThreads()
    {
        var ws = Path.Combine(Path.GetTempPath(), "dotcraft-session-lifecycle-context-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ws);
        try
        {
            var privateId = new SessionIdentity
            {
                ChannelName = "qq",
                UserId = "42",
                ChannelContext = "user:42",
                WorkspacePath = ws
            };
            var groupId = new SessionIdentity
            {
                ChannelName = "qq",
                UserId = "42",
                ChannelContext = "group:99",
                WorkspacePath = ws
            };

            var privateThread = await _svc.CreateThreadAsync(privateId);
            var groupThread = await _svc.CreateThreadAsync(groupId);

            var privateResults = await _svc.FindThreadsAsync(privateId);
            var groupResults = await _svc.FindThreadsAsync(groupId);

            Assert.Single(privateResults);
            Assert.Equal(privateThread.Id, privateResults[0].Id);

            Assert.Single(groupResults);
            Assert.Equal(groupThread.Id, groupResults[0].Id);
        }
        finally
        {
            try { Directory.Delete(ws, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public async Task FindThreads_CrossChannelOrigins_IncludesOtherChannelContexts()
    {
        var ws = "/ws/desktop";
        var desktop = new SessionIdentity
        {
            ChannelName = "dotcraft-desktop",
            UserId = "local",
            WorkspacePath = ws,
            ChannelContext = "workspace:" + ws
        };
        var cli = new SessionIdentity
        {
            ChannelName = "cli",
            UserId = "local",
            WorkspacePath = ws,
            ChannelContext = null
        };
        await _svc.CreateThreadAsync(desktop);
        await _svc.CreateThreadAsync(cli);

        var desktopOnly = await _svc.FindThreadsAsync(desktop);
        Assert.Single(desktopOnly);
        Assert.Equal("dotcraft-desktop", desktopOnly[0].OriginChannel);

        var merged = await _svc.FindThreadsAsync(desktop, crossChannelOrigins: ["cli"]);
        Assert.Equal(2, merged.Count);
    }

    [Fact]
    public async Task FindThreads_CrossChannelOrigins_IncludesCronWithSyntheticUserId()
    {
        var ws = "/ws/cron_cross";
        var desktop = new SessionIdentity
        {
            ChannelName = "dotcraft-desktop",
            UserId = "local",
            WorkspacePath = ws,
            ChannelContext = "workspace:" + ws
        };
        var cronIdentity = new SessionIdentity
        {
            ChannelName = "cron",
            UserId = "cron:d9f53704",
            WorkspacePath = ws,
            ChannelContext = null
        };
        await _svc.CreateThreadAsync(desktop);
        var cronThread = await _svc.CreateThreadAsync(cronIdentity);

        var desktopOnly = await _svc.FindThreadsAsync(desktop);
        Assert.Single(desktopOnly);

        var merged = await _svc.FindThreadsAsync(desktop, crossChannelOrigins: ["cron"]);
        Assert.Equal(2, merged.Count);
        Assert.Contains(merged, s => s.Id == cronThread.Id && s.OriginChannel == "cron");
    }

    [Fact]
    public async Task FindThreads_WorkspaceScope_IncludesEveryOriginButNotOtherWorkspaces()
    {
        var workspace = "/ws/workspace_scope";
        var desktop = new SessionIdentity
        {
            ChannelName = "dotcraft-desktop",
            UserId = "local",
            WorkspacePath = workspace,
            ChannelContext = "workspace:" + workspace
        };
        var identities = new[]
        {
            desktop,
            new SessionIdentity { ChannelName = "oratorio", UserId = "operator", WorkspacePath = workspace, ChannelContext = "oratorio:bridge" },
            new SessionIdentity { ChannelName = "cron", UserId = "cron:job", WorkspacePath = workspace },
            new SessionIdentity { ChannelName = "heartbeat", UserId = "heartbeat:run", WorkspacePath = workspace },
            new SessionIdentity { ChannelName = "future-channel", UserId = "future:user", WorkspacePath = workspace, ChannelContext = "future:context" }
        };

        foreach (var identity in identities)
            await _svc.CreateThreadAsync(identity);
        await _svc.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "oratorio",
            UserId = "operator",
            WorkspacePath = "/ws/other"
        });

        var identityScoped = await _svc.FindThreadsAsync(desktop);
        var workspaceScoped = await _svc.FindThreadsAsync(
            desktop,
            crossChannelOrigins: ["not-used"],
            scope: ThreadDiscoveryScope.Workspace);

        Assert.Single(identityScoped);
        Assert.Equal(identities.Length, workspaceScoped.Count);
        Assert.Equal(
            identities.Select(identity => identity.ChannelName).Order(),
            workspaceScoped.Select(summary => summary.OriginChannel).Order());
    }

    // -------------------------------------------------------------------------
    // GetThread
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetThread_ReturnsThread()
    {
        var thread = await _svc.CreateThreadAsync(MakeIdentity());
        var retrieved = await _svc.GetThreadAsync(thread.Id);
        Assert.Equal(thread.Id, retrieved.Id);
    }

    [Fact]
    public async Task GetThread_NonExistent_Throws()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _svc.GetThreadAsync("nonexistent_id"));
    }

    // -------------------------------------------------------------------------
    // SetThreadMode
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SetThreadMode_UpdatesConfig()
    {
        var thread = await _svc.CreateThreadAsync(MakeIdentity());
        await _svc.SetThreadModeAsync(thread.Id, "plan");

        var loaded = await _store.LoadThreadAsync(thread.Id);
        Assert.NotNull(loaded);
        Assert.Equal("plan", loaded.Configuration?.Mode);
    }

    // -------------------------------------------------------------------------
    // UpdateThreadConfiguration
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpdateThreadConfiguration_PersistsMcpServers()
    {
        var thread = await _svc.CreateThreadAsync(MakeIdentity());

        var config = new ThreadConfiguration
        {
            Mode = "agent",
            McpServers =
            [
                new McpServerConfig { Name = "test-mcp", Transport = "stdio", Command = "echo" }
            ]
        };
        await _svc.UpdateThreadConfigurationAsync(thread.Id, config);

        var loaded = await _store.LoadThreadAsync(thread.Id);
        Assert.NotNull(loaded?.Configuration?.McpServers);
        Assert.Single(loaded!.Configuration!.McpServers);
        Assert.Equal("test-mcp", loaded.Configuration.McpServers[0].Name);
    }

    [Fact]
    public async Task CreateThread_WithConfig_PersistsConfiguration()
    {
        var config = new ThreadConfiguration
        {
            Mode = "plan",
            AgentProfileId = "team-reviewer",
            AgentProfileSource = "workspace",
            AgentProfileFingerprint = "sha256:abc",
            ToolPolicy = new ThreadToolPolicy
            {
                Allow = ["ReadFile"],
                Deny = ["WriteFile"]
            },
            McpServers =
            [
                new McpServerConfig { Name = "srv1", Transport = "http", Url = "http://localhost:3000" }
            ]
        };
        var thread = await _svc.CreateThreadAsync(MakeIdentity(), config);

        var loaded = await _store.LoadThreadAsync(thread.Id);
        Assert.NotNull(loaded?.Configuration);
        Assert.Equal("plan", loaded!.Configuration!.Mode);
        Assert.NotNull(loaded.Configuration.McpServers);
        Assert.Single(loaded.Configuration.McpServers);
        Assert.Equal("srv1", loaded.Configuration.McpServers[0].Name);
        Assert.Equal("team-reviewer", loaded.Configuration.AgentProfileId);
        Assert.Equal("workspace", loaded.Configuration.AgentProfileSource);
        Assert.Equal("sha256:abc", loaded.Configuration.AgentProfileFingerprint);
        Assert.Equal(["ReadFile"], loaded.Configuration.ToolPolicy!.Allow!);
        Assert.Equal(["WriteFile"], loaded.Configuration.ToolPolicy.Deny!);
    }

    [Fact]
    public async Task UpdateThreadConfiguration_NullMcpServers_ClearsExisting()
    {
        var config = new ThreadConfiguration
        {
            Mode = "agent",
            McpServers =
            [
                new McpServerConfig { Name = "old-mcp", Transport = "stdio", Command = "echo" }
            ]
        };
        var thread = await _svc.CreateThreadAsync(MakeIdentity(), config);

        var updatedConfig = new ThreadConfiguration { Mode = "agent", McpServers = null };
        await _svc.UpdateThreadConfigurationAsync(thread.Id, updatedConfig);

        var loaded = await _store.LoadThreadAsync(thread.Id);
        Assert.Null(loaded?.Configuration?.McpServers);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static SessionIdentity MakeIdentity(string? userId = "user1", string? workspace = "/workspace") =>
        new() { ChannelName = "test", UserId = userId, WorkspacePath = workspace ?? "/workspace" };

    private async Task<SessionThread> CreateSubAgentAsync(SessionThread parent, string threadId)
    {
        var rootThreadId = parent.Source.SubAgent?.RootThreadId;
        if (string.IsNullOrWhiteSpace(rootThreadId))
            rootThreadId = parent.Id;
        var depth = parent.Source.SubAgent?.Depth + 1 ?? 1;
        var child = await _svc.CreateThreadAsync(
            new SessionIdentity
            {
                ChannelName = SubAgentThreadOrigin.ChannelName,
                UserId = parent.UserId,
                WorkspacePath = parent.WorkspacePath,
                ChannelContext = parent.Id
            },
            threadId: threadId,
            displayName: threadId,
            source: ThreadSource.ForSubAgent(new SubAgentThreadSource
            {
                ParentThreadId = parent.Id,
                RootThreadId = rootThreadId,
                Depth = depth,
                AgentNickname = threadId
            }));
        await _svc.UpsertThreadSpawnEdgeAsync(new ThreadSpawnEdge
        {
            ParentThreadId = parent.Id,
            ChildThreadId = child.Id,
            Depth = depth,
            AgentNickname = threadId,
            Status = ThreadSpawnEdgeStatus.Open
        });
        return child;
    }
}

/// <summary>
/// A lightweight ISessionService implementation backed by ThreadStore, used for lifecycle testing.
/// Does not implement SubmitInputAsync (throws NotSupportedException).
/// </summary>
internal sealed class FakeSessionService : ISessionService, ISubAgentThreadLifecycleService
{
    private readonly ThreadStore _store;
    private readonly Dictionary<string, SessionThread> _threads = new();

    public FakeSessionService(ThreadStore store) => _store = store;

    /// <inheritdoc />
    public Action<SessionThread>? ThreadCreatedForBroadcast { get; set; }

    /// <inheritdoc />
    public Action<string>? ThreadDeletedForBroadcast { get; set; }

    /// <inheritdoc />
    public Action<SessionThread>? ThreadRenamedForBroadcast { get; set; }

    /// <inheritdoc />
    public Action<SessionThread>? ThreadUpdatedForBroadcast { get; set; }

    /// <inheritdoc />
    public Action<string, ThreadStatus, ThreadStatus>? ThreadStatusChangedForBroadcast { get; set; }

    /// <inheritdoc />
    public Action<string, SessionThreadRuntimeSignal>? ThreadRuntimeSignalForBroadcast { get; set; }

    /// <inheritdoc />
    public ContextUsageSnapshot? TryGetContextUsageSnapshot(string threadId) => null;

    public async Task<SessionThread> CreateThreadAsync(
        SessionIdentity identity,
        ThreadConfiguration? config = null,
        HistoryMode historyMode = HistoryMode.Server,
        string? threadId = null,
        string? displayName = null,
        CancellationToken ct = default,
        ThreadSource? source = null)
    {
        var thread = new SessionThread
        {
            Id = threadId ?? SessionIdGenerator.NewThreadId(),
            WorkspacePath = identity.WorkspacePath,
            UserId = identity.UserId,
            OriginChannel = identity.ChannelName,
            Status = ThreadStatus.Active,
            HistoryMode = historyMode,
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
            Configuration = config,
            DisplayName = displayName,
            Source = source ?? ThreadSource.User()
        };

        if (identity.ChannelContext != null)
        {
            thread.ChannelContext = identity.ChannelContext;
            thread.Metadata["channelContext"] = identity.ChannelContext;
        }

        _threads[thread.Id] = thread;
        await _store.SaveThreadAsync(thread, ct);
        ThreadCreatedForBroadcast?.Invoke(thread);
        return thread;
    }

    public async Task<SessionThread> ResumeThreadAsync(string threadId, CancellationToken ct = default)
    {
        var thread = await GetOrLoadAsync(threadId, ct);
        if (thread.Status == ThreadStatus.Archived)
            throw new InvalidOperationException($"Thread '{threadId}' is archived.");
        if (thread.Status != ThreadStatus.Active)
        {
            thread.Status = ThreadStatus.Active;
            thread.LastActiveAt = DateTimeOffset.UtcNow;
            await _store.SaveThreadAsync(thread, ct);
        }
        return thread;
    }

    public async Task<ThreadResetResult> ResetConversationAsync(
        SessionIdentity identity,
        ThreadConfiguration? config = null,
        HistoryMode historyMode = HistoryMode.Server,
        string? displayName = null,
        CancellationToken ct = default)
    {
        var existing = await FindThreadsAsync(identity, includeArchived: false, crossChannelOrigins: null, ct);
        var archived = new List<string>();
        foreach (var summary in existing.Where(s => s.Status is ThreadStatus.Active or ThreadStatus.Paused))
        {
            await ArchiveThreadAsync(summary.Id, ct);
            archived.Add(summary.Id);
        }

        var thread = await CreateThreadAsync(identity, config, historyMode, displayName: displayName, ct: ct);
        return new ThreadResetResult { Thread = thread, ArchivedThreadIds = archived, CreatedLazily = true };
    }

    public async Task PauseThreadAsync(string threadId, CancellationToken ct = default)
    {
        var thread = await GetOrLoadAsync(threadId, ct);
        if (thread.Status == ThreadStatus.Paused) return;
        thread.Status = ThreadStatus.Paused;
        await _store.SaveThreadAsync(thread, ct);
    }

    public async Task ArchiveThreadAsync(string threadId, CancellationToken ct = default)
    {
        var root = await GetOrLoadAsync(threadId, ct);
        ThrowIfDirectSubAgentLifecycleOperation(root, "archive");
        foreach (var id in await CollectSubAgentSubtreeIdsAsync(root.Id, includeClosed: true, ct))
        {
            var thread = await GetOrLoadAsync(id, ct);
            if (thread.Status == ThreadStatus.Archived) continue;
            var previous = thread.Status;
            thread.Status = ThreadStatus.Archived;
            await _store.SaveThreadAsync(thread, ct);
            ThreadStatusChangedForBroadcast?.Invoke(thread.Id, previous, thread.Status);
        }
    }

    public async Task UnarchiveThreadAsync(string threadId, CancellationToken ct = default)
    {
        var root = await GetOrLoadAsync(threadId, ct);
        ThrowIfDirectSubAgentLifecycleOperation(root, "unarchive");
        foreach (var id in await CollectSubAgentSubtreeIdsAsync(root.Id, includeClosed: false, ct))
        {
            var thread = await GetOrLoadAsync(id, ct);
            if (thread.Status == ThreadStatus.Active) continue;
            var previous = thread.Status;
            thread.Status = ThreadStatus.Active;
            thread.LastActiveAt = DateTimeOffset.UtcNow;
            await _store.SaveThreadAsync(thread, ct);
            ThreadStatusChangedForBroadcast?.Invoke(thread.Id, previous, thread.Status);
        }
    }

    public async Task RenameThreadAsync(string threadId, string displayName, CancellationToken ct = default)
    {
        var thread = await GetOrLoadAsync(threadId, ct);
        var previous = thread.DisplayName;
        thread.DisplayName = displayName;
        await _store.SaveThreadAsync(thread, ct);
        if (previous != displayName)
            ThreadRenamedForBroadcast?.Invoke(thread);
    }

    public Task<int> CountWorkspaceThreadsAsync(string workspacePath, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public async Task<IReadOnlyList<ThreadSummary>> FindThreadsAsync(
        SessionIdentity identity,
        bool includeArchived = false,
        IReadOnlyList<string>? crossChannelOrigins = null,
        CancellationToken ct = default,
        bool includeSubAgents = false,
        ThreadDiscoveryScope scope = ThreadDiscoveryScope.Identity)
    {
        var index = await _store.LoadIndexAsync(ct);
        var byId = index.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        var hasCross = crossChannelOrigins is { Count: > 0 };
        return index
            .Where(s =>
            {
                if (!string.Equals(s.WorkspacePath, identity.WorkspacePath, StringComparison.OrdinalIgnoreCase))
                    return false;
                if (!(includeArchived || s.Status != ThreadStatus.Archived))
                    return false;
                if (!includeSubAgents && (string.Equals(s.Source.Kind, ThreadSourceKinds.SubAgent, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s.OriginChannel, SubAgentThreadOrigin.ChannelName, StringComparison.OrdinalIgnoreCase)))
                    return false;
                if (includeSubAgents
                    && IsSubAgentSummary(s)
                    && IsHiddenByArchivedParent(s, byId, includeArchived))
                    return false;
                if (scope == ThreadDiscoveryScope.Workspace)
                    return true;
                if (includeSubAgents
                    && IsSubAgentSummary(s)
                    && (identity.UserId == null || s.UserId == identity.UserId))
                    return true;

                var identityMatch =
                    (identity.UserId == null || s.UserId == identity.UserId)
                    && (identity.ChannelContext == null
                        ? s.ChannelContext == null
                        : s.ChannelContext == identity.ChannelContext);

                if (identityMatch)
                    return true;

                if (!hasCross)
                    return false;

                foreach (var o in crossChannelOrigins!)
                {
                    if (string.Equals(o, s.OriginChannel, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                return false;
            })
            .OrderByDescending(s => s.LastActiveAt)
            .ToList();
    }

    public Task UpsertThreadSpawnEdgeAsync(ThreadSpawnEdge edge, CancellationToken ct = default) =>
        _store.UpsertThreadSpawnEdgeAsync(edge, ct);

    public Task SetThreadSpawnEdgeStatusAsync(
        string parentThreadId,
        string childThreadId,
        string status,
        CancellationToken ct = default) =>
        _store.SetThreadSpawnEdgeStatusAsync(parentThreadId, childThreadId, status, ct);

    public Task<IReadOnlyList<ThreadSpawnEdge>> ListSubAgentChildrenAsync(
        string parentThreadId,
        bool includeClosed = false,
        CancellationToken ct = default) =>
        _store.ListSubAgentChildrenAsync(parentThreadId, includeClosed, ct);

    public async Task<SessionThread> GetThreadAsync(string threadId, CancellationToken ct = default) =>
        await GetOrLoadAsync(threadId, ct);

    public Task<SessionThread> EnsureThreadLoadedAsync(string threadId, CancellationToken ct = default) =>
        GetThreadAsync(threadId, ct);

    public IAsyncEnumerable<SessionEvent> SubmitInputAsync(
        string threadId, IList<AIContent> content, SenderContext? sender = null,
        ChatMessage[]? messages = null, CancellationToken ct = default,
        SessionInputSnapshot? inputSnapshot = null) =>
        throw new NotSupportedException("Use FakeSessionService for lifecycle tests only.");

    public IAsyncEnumerable<SessionEvent> SubscribeThreadAsync(
        string threadId,
        bool replayRecent = false,
        CancellationToken ct = default) =>
        EmptyEvents();

    public Task ResolveApprovalAsync(
        string threadId,
        string turnId,
        string requestId,
        SessionApprovalDecision decision,
        CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task ResolveUserInputRequestAsync(
        string threadId,
        string turnId,
        string requestId,
        RequestUserInputResponse response,
        CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task CancelTurnAsync(string threadId, string turnId, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task CleanBackgroundTerminalsAsync(string threadId, CancellationToken ct = default) =>
        Task.CompletedTask;

    public async Task<SessionThread> RollbackThreadAsync(string threadId, int numTurns, CancellationToken ct = default)
    {
        var thread = await GetOrLoadAsync(threadId, ct);
        thread.Turns.RemoveRange(thread.Turns.Count - numTurns, numTurns);
        thread.LastActiveAt = DateTimeOffset.UtcNow;
        await _store.RollbackThreadAsync(thread, numTurns, ct);
        return thread;
    }

    public async Task<QueuedTurnInput> EnqueueTurnInputAsync(
        string threadId,
        IList<AIContent> content,
        SenderContext? sender = null,
        CancellationToken ct = default,
        SessionInputSnapshot? inputSnapshot = null)
    {
        var thread = await GetOrLoadAsync(threadId, ct);
        var parts = inputSnapshot?.NativeInputParts?.ToList() ?? content.Select(c => c.ToWireInputPart()).ToList();
        var queued = new QueuedTurnInput
        {
            Id = SessionIdGenerator.NewQueuedInputId(),
            ThreadId = threadId,
            NativeInputParts = parts,
            MaterializedInputParts = inputSnapshot?.MaterializedInputParts?.ToList() ?? parts,
            DisplayText = inputSnapshot?.DisplayText ?? SessionWireMapper.BuildDisplayText(parts),
            Sender = sender,
            CreatedAt = DateTimeOffset.UtcNow,
            SentAsGoal = inputSnapshot?.SentAsGoal
        };
        thread.QueuedInputs.Add(queued);
        thread.LastActiveAt = DateTimeOffset.UtcNow;
        await _store.SaveThreadAsync(thread, ct);
        return queued;
    }

    public async Task<IReadOnlyList<QueuedTurnInput>> RemoveQueuedTurnInputAsync(
        string threadId,
        string queuedInputId,
        CancellationToken ct = default)
    {
        var thread = await GetOrLoadAsync(threadId, ct);
        thread.QueuedInputs.RemoveAll(q => string.Equals(q.Id, queuedInputId, StringComparison.Ordinal));
        thread.LastActiveAt = DateTimeOffset.UtcNow;
        await _store.SaveThreadAsync(thread, ct);
        return thread.QueuedInputs.ToList();
    }

    public Task<IReadOnlyList<QueuedTurnInput>> ReorderQueuedTurnInputsAsync(
        string threadId,
        IReadOnlyList<string> orderedQueuedInputIds,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Use FakeSessionService for lifecycle tests only.");

    public Task<IReadOnlyList<QueuedTurnInput>> UpdateQueuedTurnInputAsync(
        string threadId,
        string queuedInputId,
        string expectedTurnId,
        string status,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Use FakeSessionService for lifecycle tests only.");

    public async Task SetThreadModeAsync(string threadId, string mode, CancellationToken ct = default)
    {
        var thread = await GetOrLoadAsync(threadId, ct);
        thread.Configuration ??= new ThreadConfiguration();
        thread.Configuration.Mode = mode;
        await _store.SaveThreadAsync(thread, ct);
    }

    public async Task UpdateThreadConfigurationAsync(string threadId, ThreadConfiguration config, CancellationToken ct = default)
    {
        var thread = await GetOrLoadAsync(threadId, ct);
        thread.Configuration = config;
        await _store.SaveThreadAsync(thread, ct);
    }

    public async Task DeleteThreadPermanentlyAsync(string threadId, CancellationToken ct = default)
    {
        var root = await GetOrLoadAsync(threadId, ct);
        ThrowIfDirectSubAgentLifecycleOperation(root, "delete");
        foreach (var id in (await CollectSubAgentSubtreeIdsAsync(threadId, includeClosed: true, ct)).Reverse())
        {
            _threads.Remove(id);
            _store.DeleteThread(id);
            ThreadDeletedForBroadcast?.Invoke(id);
        }
    }

    private async Task<SessionThread> GetOrLoadAsync(string threadId, CancellationToken ct)
    {
        if (_threads.TryGetValue(threadId, out var t)) return t;
        var loaded = await _store.LoadThreadAsync(threadId, ct)
            ?? throw new KeyNotFoundException($"Thread '{threadId}' not found.");
        _threads[threadId] = loaded;
        return loaded;
    }

    public async Task ArchiveSubAgentTreeForCloseAsync(string childThreadId, CancellationToken ct = default)
    {
        var root = await GetOrLoadAsync(childThreadId, ct);
        if (!IsSubAgentThread(root))
            throw new InvalidOperationException($"Thread '{childThreadId}' is not a SubAgent child thread.");

        foreach (var id in await CollectSubAgentSubtreeIdsAsync(root.Id, includeClosed: true, ct))
        {
            var thread = await GetOrLoadAsync(id, ct);
            if (thread.Status == ThreadStatus.Archived) continue;
            var previous = thread.Status;
            thread.Status = ThreadStatus.Archived;
            await _store.SaveThreadAsync(thread, ct);
            ThreadStatusChangedForBroadcast?.Invoke(thread.Id, previous, thread.Status);
        }
    }

    private async Task<IReadOnlyList<string>> CollectSubAgentSubtreeIdsAsync(
        string rootThreadId,
        bool includeClosed,
        CancellationToken ct)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        async Task VisitAsync(string id)
        {
            if (!seen.Add(id)) return;
            result.Add(id);
            var children = await _store.ListSubAgentChildrenAsync(id, includeClosed, ct);
            foreach (var child in children)
                await VisitAsync(child.ChildThreadId);
        }

        await VisitAsync(rootThreadId);
        return result;
    }

    private static void ThrowIfDirectSubAgentLifecycleOperation(SessionThread thread, string operation)
    {
        if (!IsSubAgentThread(thread)) return;
        var parentId = thread.Source.SubAgent?.ParentThreadId?.Trim();
        if (string.IsNullOrWhiteSpace(parentId))
            parentId = thread.ChannelContext?.Trim();
        if (!string.IsNullOrWhiteSpace(parentId))
            throw new InvalidOperationException(
                $"SubAgent child thread '{thread.Id}' cannot be {operation}d directly; manage its parent thread '{parentId}' instead.");
    }

    private static bool IsSubAgentThread(SessionThread thread) =>
        string.Equals(thread.Source.Kind, ThreadSourceKinds.SubAgent, StringComparison.OrdinalIgnoreCase)
        || string.Equals(thread.OriginChannel, SubAgentThreadOrigin.ChannelName, StringComparison.OrdinalIgnoreCase);

    private static bool IsSubAgentSummary(ThreadSummary summary) =>
        string.Equals(summary.Source.Kind, ThreadSourceKinds.SubAgent, StringComparison.OrdinalIgnoreCase)
        || string.Equals(summary.OriginChannel, SubAgentThreadOrigin.ChannelName, StringComparison.OrdinalIgnoreCase);

    private static bool IsHiddenByArchivedParent(
        ThreadSummary summary,
        IReadOnlyDictionary<string, ThreadSummary> byId,
        bool includeArchived)
    {
        if (includeArchived) return false;
        var parentId = summary.Source.SubAgent?.ParentThreadId?.Trim();
        if (string.IsNullOrWhiteSpace(parentId))
            parentId = summary.ChannelContext?.Trim();
        return !string.IsNullOrWhiteSpace(parentId)
            && byId.TryGetValue(parentId, out var parent)
            && parent.Status == ThreadStatus.Archived;
    }

    private static async IAsyncEnumerable<SessionEvent> EmptyEvents()
    {
        yield break;
    }
}
