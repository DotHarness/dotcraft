using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Memory;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

/// <summary>
/// Tests for thread/* methods (spec Section 4).
/// Verifies response shapes and the post-response notifications emitted
/// after thread/start (→ thread/started), thread/resume (→ thread/resumed),
/// thread/pause and thread/archive (→ thread/statusChanged).
/// </summary>
public sealed class AppServerThreadLifecycleTests : IDisposable
{
    private readonly AppServerTestHarness _h = new();

    public AppServerThreadLifecycleTests()
    {
        // All thread tests need a ready connection
        _h.InitializeAsync(interactiveToolUi: true).GetAwaiter().GetResult();
    }

    public void Dispose() => _h.Dispose();

    // -------------------------------------------------------------------------
    // thread/start (spec Section 4.1)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ThreadStart_ReturnsThreadInResult()
    {
        var msg = _h.BuildRequest(AppServerMethods.ThreadStart, new
        {
            identity = new { channelName = "appserver", userId = "test_user", workspacePath = _h.Identity.WorkspacePath }
        });
        await _h.ExecuteRequestAsync(msg);

        // thread/start sends response inline; read it from transport
        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var thread = response.RootElement.GetProperty("result").GetProperty("thread");
        Assert.StartsWith("thread_", thread.GetProperty("id").GetString()!);
        Assert.Equal("active", thread.GetProperty("status").GetString());
    }

    [Fact]
    public async Task ItemWidgetState_PersistsClearsAndSurfacesOnThreadRead()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        var turn = new SessionTurn
        {
            Id = "turn_1",
            ThreadId = thread.Id,
            Status = TurnStatus.Completed,
            StartedAt = DateTimeOffset.UtcNow
        };
        turn.Items.Add(new SessionItem
        {
            Id = "item_1",
            TurnId = "turn_1",
            Type = ItemType.DynamicToolCall,
            Status = ItemStatus.Completed,
            Payload = new DynamicToolCallPayload
            {
                CallId = "call_widget",
                ToolName = "ShowCard",
                Namespace = "sample",
                Success = true
            },
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        });
        thread.Turns.Add(turn);
        await _h.Service.SeedThreadAsync(thread);

        await _h.ExecuteRequestAsync(_h.BuildRequest(AppServerMethods.ItemWidgetStateSet, new
        {
            threadId = thread.Id,
            callId = "call_widget",
            widgetState = new { tab = 2, scroll = 120 }
        }));
        using (var setResp = await _h.Transport.ReadNextSentAsync())
        {
            AppServerTestHarness.AssertIsSuccessResponse(setResp);
            Assert.False(setResp.RootElement.GetProperty("result").GetProperty("cleared").GetBoolean());
        }

        await _h.ExecuteRequestAsync(_h.BuildRequest(AppServerMethods.ThreadRead, new { threadId = thread.Id, includeTurns = true }));
        using (var readResp = await _h.Transport.ReadNextSentAsync())
        {
            AppServerTestHarness.AssertIsSuccessResponse(readResp);
            var item = readResp.RootElement.GetProperty("result").GetProperty("thread")
                .GetProperty("turns")[0].GetProperty("items")[0];
            Assert.Equal(2, item.GetProperty("payload").GetProperty("widgetState").GetProperty("tab").GetInt32());
        }

        await _h.ExecuteRequestAsync(_h.BuildRequest(AppServerMethods.ItemWidgetStateSet, new
        {
            threadId = thread.Id,
            callId = "call_widget"
        }));
        using (var clearResp = await _h.Transport.ReadNextSentAsync())
        {
            AppServerTestHarness.AssertIsSuccessResponse(clearResp);
            Assert.True(clearResp.RootElement.GetProperty("result").GetProperty("cleared").GetBoolean());
        }

        Assert.Empty(_h.Service.GetItemWidgetStates(thread.Id));
    }

    [Fact]
    public async Task ItemWidgetState_RejectsOversizedState()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        await _h.Service.SeedThreadAsync(thread);

        await _h.ExecuteRequestAsync(_h.BuildRequest(AppServerMethods.ItemWidgetStateSet, new
        {
            threadId = thread.Id,
            callId = "call_widget",
            widgetState = new { blob = new string('x', 9000) }
        }));

        using var resp = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(resp, AppServerErrors.InvalidParamsCode);
    }

    [Fact]
    public async Task ThreadStart_OmitsWorkspacePath_NormalizesToHostWorkspace()
    {
        var msg = _h.BuildRequest(AppServerMethods.ThreadStart, new
        {
            identity = new { channelName = "appserver", userId = "test_user_no_ws" }
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var thread = response.RootElement.GetProperty("result").GetProperty("thread");
        Assert.Equal(_h.Identity.WorkspacePath, thread.GetProperty("workspacePath").GetString());
    }

    [Fact]
    public async Task ThreadStart_WithRuntimeAdditionalContext_BindsContextAndRefreshesAgent()
    {
        var runtimeContextProvider = new WireRuntimeAdditionalContextProvider();
        using var h = new AppServerTestHarness(wireRuntimeAdditionalContextProvider: runtimeContextProvider);
        await h.InitializeAsync();

        var msg = h.BuildRequest(AppServerMethods.ThreadStart, new
        {
            identity = new { channelName = "appserver", userId = "test_user", workspacePath = h.Identity.WorkspacePath },
            additionalContext = new Dictionary<string, RuntimeAdditionalContextEntry>
            {
                ["desktop.threadCoordination"] = new()
                {
                    Kind = RuntimeAdditionalContextKinds.Application,
                    Value = "Search for thread tools before background thread management."
                }
            }
        });
        await h.ExecuteRequestAsync(msg);

        var response = await h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var threadId = response.RootElement.GetProperty("result").GetProperty("thread").GetProperty("id").GetString()!;

        Assert.Contains(threadId, h.Service.RefreshedThreadAgents);
        var section = runtimeContextProvider.GetSystemPromptSection(new ThreadSystemPromptContext(threadId, h.Identity.WorkspacePath));
        Assert.NotNull(section);
        Assert.Contains("# Runtime Additional Context", section, StringComparison.Ordinal);
        Assert.Contains("desktop.threadCoordination", section, StringComparison.Ordinal);
        Assert.Contains("<app-context>", section, StringComparison.Ordinal);
        Assert.Contains("Search for thread tools", section, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThreadStart_WithUnsupportedReasoning_ReturnsInvalidParams()
    {
        using var h = new AppServerTestHarness(
            appConfigMonitor: new AppConfigMonitor(
                AppConfigTestFactory.CreateAnthropic(model: "claude-mythos-preview")));
        await h.InitializeAsync();

        var msg = h.BuildRequest(AppServerMethods.ThreadStart, new
        {
            identity = new { channelName = "appserver", userId = "test_user", workspacePath = h.Identity.WorkspacePath },
            config = new
            {
                reasoning = new { enabled = false, effort = "high", output = "full" }
            }
        });
        await h.ExecuteRequestAsync(msg);

        var response = await h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.InvalidParamsCode);
    }

    [Fact]
    public async Task ThreadStart_EmitsThreadStartedNotification()
    {
        var msg = _h.BuildRequest(AppServerMethods.ThreadStart, new
        {
            identity = new { channelName = "appserver", userId = "test_user", workspacePath = _h.Identity.WorkspacePath }
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();  // response
        var notification = await _h.Transport.ReadNextSentAsync(); // notification

        AppServerTestHarness.AssertIsNotification(notification, AppServerMethods.ThreadStarted);
        Assert.StartsWith("thread_", notification.RootElement
            .GetProperty("params").GetProperty("thread")
            .GetProperty("id").GetString()!);
    }

    [Fact]
    public async Task ThreadStart_ResponseBeforeNotification_Ordering()
    {
        var msg = _h.BuildRequest(AppServerMethods.ThreadStart, new
        {
            identity = new { channelName = "appserver", userId = "test_user", workspacePath = _h.Identity.WorkspacePath }
        });
        await _h.ExecuteRequestAsync(msg);

        var first = await _h.Transport.ReadNextSentAsync();
        var second = await _h.Transport.ReadNextSentAsync();

        // First message must be the response (has 'result'), second must be the notification (has 'method')
        Assert.True(first.RootElement.TryGetProperty("result", out _),
            "Response (with 'result') must arrive before the notification");
        Assert.True(second.RootElement.TryGetProperty("method", out _),
            "Notification (with 'method') must arrive after the response");
    }

    [Fact]
    public async Task WorktreeCreateAndStart_ReturnsThreadAndEmitsStarted()
    {
        InitializeGitWorkspace(_h.Identity.WorkspacePath);
        var msg = _h.BuildRequest(AppServerMethods.WorktreeCreateAndStart, new
        {
            identity = new { channelName = "appserver", userId = "test_user", workspacePath = _h.Identity.WorkspacePath },
            branchName = "dotcraft/wire-start",
            copyDirtyChanges = false
        });

        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var result = response.RootElement.GetProperty("result");
        var thread = result.GetProperty("thread");
        var worktree = result.GetProperty("worktree");
        Assert.Equal(worktree.GetProperty("path").GetString(), thread.GetProperty("effectiveWorkspacePath").GetString());
        Assert.Equal("dotcraft/wire-start", worktree.GetProperty("branchName").GetString());

        var notification = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsNotification(notification, AppServerMethods.ThreadStarted);
        Assert.Equal(thread.GetProperty("id").GetString(), notification.RootElement.GetProperty("params").GetProperty("thread").GetProperty("id").GetString());
    }

    [Fact]
    public async Task ThreadWorktreeHandoff_ReturnsThreadAndEmitsUpdated()
    {
        InitializeGitWorkspace(_h.Identity.WorkspacePath);
        var start = _h.BuildRequest(AppServerMethods.ThreadStart, new
        {
            identity = new { channelName = "appserver", userId = "test_user", workspacePath = _h.Identity.WorkspacePath }
        });
        await _h.ExecuteRequestAsync(start);
        var startResponse = await _h.Transport.ReadNextSentAsync();
        _ = await _h.Transport.ReadNextSentAsync();
        var threadId = startResponse.RootElement.GetProperty("result").GetProperty("thread").GetProperty("id").GetString()!;

        var handoff = _h.BuildRequest(AppServerMethods.ThreadWorktreeHandoff, new
        {
            threadId,
            mode = "worktree",
            branchName = "dotcraft/wire-handoff",
            copyDirtyChanges = false
        });
        await _h.ExecuteRequestAsync(handoff);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var result = response.RootElement.GetProperty("result");
        Assert.Equal("worktree", result.GetProperty("mode").GetString());
        var thread = result.GetProperty("thread");
        Assert.Equal(threadId, thread.GetProperty("id").GetString());
        Assert.Equal(result.GetProperty("worktree").GetProperty("path").GetString(), thread.GetProperty("effectiveWorkspacePath").GetString());

        var notification = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsNotification(notification, AppServerMethods.ThreadUpdated);
        Assert.Equal(threadId, notification.RootElement.GetProperty("params").GetProperty("thread").GetProperty("id").GetString());
    }

    /// <summary>
    /// When <see cref="AppServerRequestContext.CurrentTransport"/> matches the client transport,
    /// a broadcast hook must skip that transport (mirrors <c>AppServerHost.BroadcastThreadStarted</c>)
    /// so the initiator does not receive a duplicate <c>thread/started</c> from broadcast + handler.
    /// </summary>
    [Fact]
    public async Task ThreadStart_BroadcastHookSkipsCurrentTransportWhenContextSet()
    {
        var previousTransport = AppServerRequestContext.CurrentTransport;
        var previousMethod = AppServerRequestContext.CurrentMethod;
        AppServerRequestContext.CurrentTransport = _h.Transport;
        AppServerRequestContext.CurrentMethod = AppServerMethods.ThreadStart;
        try
        {
            _h.Service.ThreadCreatedForBroadcast = thread =>
            {
                var skip = string.Equals(AppServerRequestContext.CurrentMethod, AppServerMethods.ThreadStart, StringComparison.Ordinal)
                    ? AppServerRequestContext.CurrentTransport
                    : null;
                if (skip != null && ReferenceEquals(_h.Transport, skip))
                    return;
                _h.Transport.WriteMessageAsync(new
                {
                    jsonrpc = "2.0",
                    method = AppServerMethods.ThreadStarted,
                    @params = new { thread = thread.ToWire() }
                }, default).GetAwaiter().GetResult();
            };

            var msg = _h.BuildRequest(AppServerMethods.ThreadStart, new
            {
                identity = new { channelName = "appserver", userId = "test_user", workspacePath = _h.Identity.WorkspacePath }
            });
            await _h.ExecuteRequestAsync(msg);

            await _h.Transport.ReadNextSentAsync();
            await _h.Transport.ReadNextSentAsync();
            Assert.Null(_h.Transport.TryReadSent());
        }
        finally
        {
            _h.Service.ThreadCreatedForBroadcast = null;
            AppServerRequestContext.CurrentTransport = previousTransport;
            AppServerRequestContext.CurrentMethod = previousMethod;
        }
    }

    [Fact]
    public async Task NonThreadStart_BroadcastHookIncludesCurrentTransportWhenThreadCreated()
    {
        var previousTransport = AppServerRequestContext.CurrentTransport;
        var previousMethod = AppServerRequestContext.CurrentMethod;
        AppServerRequestContext.CurrentTransport = _h.Transport;
        AppServerRequestContext.CurrentMethod = "teams/mission/create";
        try
        {
            _h.Service.ThreadCreatedForBroadcast = thread =>
            {
                var skip = string.Equals(AppServerRequestContext.CurrentMethod, AppServerMethods.ThreadStart, StringComparison.Ordinal)
                    ? AppServerRequestContext.CurrentTransport
                    : null;
                if (skip != null && ReferenceEquals(_h.Transport, skip))
                    return;
                _h.Transport.WriteMessageAsync(new
                {
                    jsonrpc = "2.0",
                    method = AppServerMethods.ThreadStarted,
                    @params = new { thread = thread.ToWire() }
                }, default).GetAwaiter().GetResult();
            };

            var thread = await _h.Service.CreateThreadAsync(new SessionIdentity
            {
                ChannelName = "teams",
                UserId = "dotcraft-teams",
                WorkspacePath = _h.Identity.WorkspacePath,
                ChannelContext = "mission_1:leader"
            });

            using var notification = await _h.Transport.ReadNextSentAsync();
            AppServerTestHarness.AssertIsNotification(notification, AppServerMethods.ThreadStarted);
            Assert.Equal(thread.Id, notification.RootElement
                .GetProperty("params").GetProperty("thread")
                .GetProperty("id").GetString());
        }
        finally
        {
            _h.Service.ThreadCreatedForBroadcast = null;
            AppServerRequestContext.CurrentTransport = previousTransport;
            AppServerRequestContext.CurrentMethod = previousMethod;
        }
    }

    [Fact]
    public async Task ThreadStart_WithHistoryModeClient_ThreadHasClientHistoryMode()
    {
        var msg = _h.BuildRequest(AppServerMethods.ThreadStart, new
        {
            identity = new { channelName = "appserver", userId = "test_user", workspacePath = _h.Identity.WorkspacePath },
            historyMode = "client"
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        await _h.Transport.ReadNextSentAsync(); // drain notification
        var thread = response.RootElement.GetProperty("result").GetProperty("thread");
        Assert.Equal("client", thread.GetProperty("historyMode").GetString());
    }

    [Fact]
    public async Task ThreadStart_WithSpawnedFromThreadId_RecordsNonSubagentOrigin()
    {
        var msg = _h.BuildRequest(AppServerMethods.ThreadStart, new
        {
            identity = new { channelName = "appserver", userId = "test_user", workspacePath = _h.Identity.WorkspacePath },
            spawnedFromThreadId = "thread_parent"
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        await _h.Transport.ReadNextSentAsync(); // drain notification
        var source = response.RootElement.GetProperty("result").GetProperty("thread").GetProperty("source");
        // Recorded as the originating thread, but kept as an ordinary (non-subagent) thread.
        Assert.Equal("thread_parent", source.GetProperty("spawnedFromThreadId").GetString());
        Assert.Equal(ThreadSourceKinds.User, source.GetProperty("kind").GetString());
    }

    // -------------------------------------------------------------------------
    // thread/fork
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Initialize_AdvertisesThreadForkCapability()
    {
        using var harness = new AppServerTestHarness();
        var init = await harness.InitializeAsync();

        var capabilities = init.RootElement.GetProperty("result").GetProperty("capabilities");
        Assert.True(capabilities.GetProperty("threadFork").GetBoolean());
        Assert.True(capabilities.GetProperty("gitWorktrees").GetBoolean());
    }

    [Fact]
    public async Task ThreadFork_ReturnsForkedThreadAndThreadStartedNotification()
    {
        var source = await _h.Service.CreateThreadAsync(_h.Identity, displayName: "Source");
        AddCompletedTurn(source, "turn_001", "first");
        AddCompletedTurn(source, "turn_002", "second");

        var msg = _h.BuildRequest(AppServerMethods.ThreadFork, new
        {
            threadId = source.Id,
            displayName = "Branch"
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        var notification = await _h.Transport.ReadNextSentAsync();

        AppServerTestHarness.AssertIsSuccessResponse(response);
        var thread = response.RootElement.GetProperty("result").GetProperty("thread");
        var forkId = thread.GetProperty("id").GetString()!;
        Assert.NotEqual(source.Id, forkId);
        Assert.Equal(forkId, thread.GetProperty("sessionId").GetString());
        Assert.Equal(source.Id, thread.GetProperty("forkedFromId").GetString());
        Assert.Equal("Branch", thread.GetProperty("displayName").GetString());
        Assert.Equal(_h.Identity.WorkspacePath, thread.GetProperty("effectiveWorkspacePath").GetString());
        Assert.False(thread.GetProperty("ephemeral").GetBoolean());
        Assert.Contains(".craft", thread.GetProperty("path").GetString(), StringComparison.OrdinalIgnoreCase);
        var turns = thread.GetProperty("turns").EnumerateArray().ToList();
        Assert.Equal(2, turns.Count);
        Assert.Equal(forkId, turns[0].GetProperty("threadId").GetString());
        AssertForkBoundaryNotice(turns[^1], source.Id);

        AppServerTestHarness.AssertIsNotification(notification, AppServerMethods.ThreadStarted);
        var notifiedThread = notification.RootElement.GetProperty("params").GetProperty("thread");
        Assert.Equal(forkId, notifiedThread.GetProperty("id").GetString());
        Assert.Equal(source.Id, notifiedThread.GetProperty("forkedFromId").GetString());
        Assert.False(notifiedThread.TryGetProperty("turns", out _));
    }

    [Fact]
    public async Task ThreadFork_DefaultDisplayNameUsesSourceDisplayName()
    {
        var source = await _h.Service.CreateThreadAsync(_h.Identity, displayName: "Research worktree handoff");
        AddCompletedTurn(source, "turn_001", "first");

        var msg = _h.BuildRequest(AppServerMethods.ThreadFork, new
        {
            threadId = source.Id
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        _ = await _h.Transport.ReadNextSentAsync();

        AppServerTestHarness.AssertIsSuccessResponse(response);
        var thread = response.RootElement.GetProperty("result").GetProperty("thread");
        Assert.Equal("Research worktree handoff", thread.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task ThreadFork_EphemeralExcludeTurns_OmitsPathAndTurns()
    {
        var source = await _h.Service.CreateThreadAsync(_h.Identity);
        AddCompletedTurn(source, "turn_001", "first");

        var msg = _h.BuildRequest(AppServerMethods.ThreadFork, new
        {
            threadId = source.Id,
            ephemeral = true,
            excludeTurns = true
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        _ = await _h.Transport.ReadNextSentAsync();

        AppServerTestHarness.AssertIsSuccessResponse(response);
        var thread = response.RootElement.GetProperty("result").GetProperty("thread");
        Assert.True(thread.GetProperty("ephemeral").GetBoolean());
        Assert.False(thread.TryGetProperty("path", out _));
        Assert.False(thread.TryGetProperty("turns", out _));
    }

    [Fact]
    public async Task SubAgentChildrenList_ReturnsPathFields()
    {
        var parent = await _h.Service.CreateThreadAsync(_h.Identity);
        var child = await _h.Service.CreateThreadAsync(
            new SessionIdentity
            {
                ChannelName = SubAgentThreadOrigin.ChannelName,
                UserId = parent.UserId,
                WorkspacePath = parent.WorkspacePath,
                ChannelContext = parent.Id
            },
            threadId: "thread_child_path",
            displayName: "Worker",
            source: ThreadSource.ForSubAgent(new SubAgentThreadSource
            {
                ParentThreadId = parent.Id,
                ParentTurnId = "turn_1",
                RootThreadId = parent.Id,
                Depth = 1,
                AgentPath = "/root/worker",
                TaskName = "worker",
                AgentNickname = "Worker",
                SupportsSendMessage = true,
                SupportsFollowupTask = true,
                SupportsClose = true
            }));
        await _h.Service.UpsertThreadSpawnEdgeAsync(new ThreadSpawnEdge
        {
            ParentThreadId = parent.Id,
            ChildThreadId = child.Id,
            ParentTurnId = "turn_1",
            Depth = 1,
            AgentPath = "/root/worker",
            TaskName = "worker",
            AgentNickname = "Worker",
            AgentRole = "worker",
            ProfileName = "native",
            RuntimeType = "native",
            SupportsSendMessage = true,
            SupportsFollowupTask = true,
            SupportsClose = true,
            Status = ThreadSpawnEdgeStatus.Open
        });

        var msg = _h.BuildRequest(AppServerMethods.SubAgentChildrenList, new
        {
            parentThreadId = parent.Id,
            includeThreads = true
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();

        AppServerTestHarness.AssertIsSuccessResponse(response);
        var wire = Assert.Single(response.RootElement.GetProperty("result").GetProperty("data").EnumerateArray());
        var edge = wire.GetProperty("edge");
        Assert.Equal("/root/worker", edge.GetProperty("agentPath").GetString());
        Assert.Equal("worker", edge.GetProperty("taskName").GetString());
        Assert.True(edge.GetProperty("supportsSendMessage").GetBoolean());
        Assert.True(edge.GetProperty("supportsFollowupTask").GetBoolean());
        Assert.Equal("Worker", wire.GetProperty("thread").GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task SubAgentSendMessage_UsesAgentPathAndWritesMailbox()
    {
        var (parent, _) = await CreatePathSubAgentAsync();

        var msg = _h.BuildRequest(AppServerMethods.SubAgentSendMessage, new
        {
            parentThreadId = parent.Id,
            target = "/root/worker",
            message = "please inspect tests"
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var result = response.RootElement.GetProperty("result");
        Assert.Equal("sent", result.GetProperty("status").GetString());
        Assert.Equal("/root/worker", result.GetProperty("agentPath").GetString());
        Assert.False(result.TryGetProperty("childThreadId", out _));
        var pending = await _h.Service.ListPendingSubAgentMailboxAsync(parent.Id, "/root/worker");
        var entry = Assert.Single(pending);
        Assert.Equal("please inspect tests", entry.Message);
    }

    [Fact]
    public async Task SubAgentFollowupTask_UsesCoordinatorForExternalChild()
    {
        var runtime = new FakeSubAgentRuntime(CliOneshotRuntime.RuntimeTypeName, "followed");
        using var harness = new AppServerTestHarness(
            subAgentCoordinatorFactory: thread => CreateCoordinator(thread.WorkspacePath, runtime));
        await harness.InitializeAsync();
        var (parent, child) = await CreatePathSubAgentAsync(
            harness,
            runtimeType: CliOneshotRuntime.RuntimeTypeName,
            profileName: "cli-run");
        await harness.Service.AddSubAgentMailboxEntryAsync(new SubAgentMailboxEntry
        {
            Id = $"mailbox_{Guid.NewGuid():N}",
            RootThreadId = parent.Id,
            SenderAgentPath = AgentPath.Root,
            TargetAgentPath = "/root/worker",
            Message = "mailbox note",
            Status = SubAgentMailboxStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        });

        var msg = harness.BuildRequest(AppServerMethods.SubAgentFollowupTask, new
        {
            parentThreadId = parent.Id,
            target = "/root/worker",
            message = "continue work"
        });
        await harness.ExecuteRequestAsync(msg);

        var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var result = response.RootElement.GetProperty("result");
        Assert.Equal("running", result.GetProperty("status").GetString());
        Assert.Equal("/root/worker", result.GetProperty("agentPath").GetString());
        var waited = await SubAgentSessionControl.WaitAgentAsync(
            harness.Service,
            child.Id,
            timeoutSeconds: 5,
            CancellationToken.None);
        var pending = await harness.Service.ListPendingSubAgentMailboxAsync(parent.Id, "/root/worker");

        Assert.Equal("followed", waited.Message);
        Assert.Empty(pending);
        Assert.Contains("mailbox note", runtime.LastRequest?.Task, StringComparison.Ordinal);
        Assert.Contains("continue work", runtime.LastRequest?.Task, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubAgentFollowupTask_WhenExternalChildRunning_QueuesTask()
    {
        var runtime = new FakeSubAgentRuntime(CliOneshotRuntime.RuntimeTypeName, "followed");
        using var harness = new AppServerTestHarness(
            subAgentCoordinatorFactory: thread => CreateCoordinator(thread.WorkspacePath, runtime));
        await harness.InitializeAsync();
        var (parent, child) = await CreatePathSubAgentAsync(
            harness,
            runtimeType: CliOneshotRuntime.RuntimeTypeName,
            profileName: "cli-run");
        await harness.Service.StartSubAgentSyntheticTurnAsync(
            child.Id,
            [new TextContent("active work")],
            CliOneshotRuntime.RuntimeTypeName,
            "cli-run");
        await harness.Service.AddSubAgentMailboxEntryAsync(new SubAgentMailboxEntry
        {
            Id = $"mailbox_{Guid.NewGuid():N}",
            RootThreadId = parent.Id,
            SenderAgentPath = AgentPath.Root,
            TargetAgentPath = "/root/worker",
            Message = "mailbox note",
            Status = SubAgentMailboxStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        });

        var msg = harness.BuildRequest(AppServerMethods.SubAgentFollowupTask, new
        {
            parentThreadId = parent.Id,
            target = "/root/worker",
            message = "continue work"
        });
        await harness.ExecuteRequestAsync(msg);

        var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var result = response.RootElement.GetProperty("result");
        Assert.Equal("queued", result.GetProperty("status").GetString());
        Assert.Equal("/root/worker", result.GetProperty("agentPath").GetString());
        Assert.False(result.TryGetProperty("childThreadId", out _));
        child = await harness.Service.GetThreadAsync(child.Id);
        var queued = Assert.Single(child.QueuedInputs);
        Assert.Equal("subagentFollowupTask", queued.TriggerKind);
        Assert.Equal("/root/worker", queued.TriggerRefId);
        Assert.Contains("mailbox note", Assert.Single(queued.MaterializedInputParts).Text, StringComparison.Ordinal);
        Assert.Contains("continue work", Assert.Single(queued.MaterializedInputParts).Text, StringComparison.Ordinal);
        Assert.Empty(await harness.Service.ListPendingSubAgentMailboxAsync(parent.Id, "/root/worker"));
        Assert.Null(runtime.LastRequest);
    }

    [Fact]
    public async Task SubAgentFollowupTask_WhenNativeChildRunningAndDeliveryModeSteer_PromotesTaskToGuidance()
    {
        var (parent, child) = await CreatePathSubAgentAsync();
        await _h.Service.StartSubAgentSyntheticTurnAsync(
            child.Id,
            [new TextContent("active work")],
            NativeSubAgentRuntime.RuntimeTypeName,
            SubAgentCoordinator.DefaultProfileName);
        await _h.Service.AddSubAgentMailboxEntryAsync(new SubAgentMailboxEntry
        {
            Id = $"mailbox_{Guid.NewGuid():N}",
            RootThreadId = parent.Id,
            SenderAgentPath = AgentPath.Root,
            TargetAgentPath = "/root/worker",
            Message = "mailbox note",
            Status = SubAgentMailboxStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        });

        var msg = _h.BuildRequest(AppServerMethods.SubAgentFollowupTask, new
        {
            parentThreadId = parent.Id,
            target = "/root/worker",
            message = "continue work",
            deliveryMode = "steer"
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var result = response.RootElement.GetProperty("result");
        Assert.Equal("guidancePending", result.GetProperty("status").GetString());
        Assert.Equal("/root/worker", result.GetProperty("agentPath").GetString());
        Assert.False(result.TryGetProperty("childThreadId", out _));
        child = await _h.Service.GetThreadAsync(child.Id);
        var queued = Assert.Single(child.QueuedInputs);
        Assert.Equal("guidancePending", queued.Status);
        Assert.Equal("turn_001", queued.ReadyAfterTurnId);
        Assert.Equal("subagentFollowupTask", queued.TriggerKind);
        Assert.Equal("/root/worker", queued.TriggerRefId);
        Assert.Contains("mailbox note", Assert.Single(queued.MaterializedInputParts).Text, StringComparison.Ordinal);
        Assert.Contains("continue work", Assert.Single(queued.MaterializedInputParts).Text, StringComparison.Ordinal);
        Assert.Empty(await _h.Service.ListPendingSubAgentMailboxAsync(parent.Id, "/root/worker"));
    }

    [Fact]
    public async Task SubAgentFollowupTask_WhenExternalChildRunningAndDeliveryModeSteer_ReturnsInvalidParams()
    {
        var runtime = new FakeSubAgentRuntime(CliOneshotRuntime.RuntimeTypeName, "followed");
        using var harness = new AppServerTestHarness(
            subAgentCoordinatorFactory: thread => CreateCoordinator(thread.WorkspacePath, runtime));
        await harness.InitializeAsync();
        var (parent, child) = await CreatePathSubAgentAsync(
            harness,
            runtimeType: CliOneshotRuntime.RuntimeTypeName,
            profileName: "cli-run");
        await harness.Service.StartSubAgentSyntheticTurnAsync(
            child.Id,
            [new TextContent("active work")],
            CliOneshotRuntime.RuntimeTypeName,
            "cli-run");
        await harness.Service.AddSubAgentMailboxEntryAsync(new SubAgentMailboxEntry
        {
            Id = $"mailbox_{Guid.NewGuid():N}",
            RootThreadId = parent.Id,
            SenderAgentPath = AgentPath.Root,
            TargetAgentPath = "/root/worker",
            Message = "mailbox note",
            Status = SubAgentMailboxStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        });

        var msg = harness.BuildRequest(AppServerMethods.SubAgentFollowupTask, new
        {
            parentThreadId = parent.Id,
            target = "/root/worker",
            message = "continue work",
            deliveryMode = "steer"
        });
        await harness.ExecuteRequestAsync(msg);

        var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.InvalidParamsCode);
        child = await harness.Service.GetThreadAsync(child.Id);
        Assert.Empty(child.QueuedInputs);
        var pending = await harness.Service.ListPendingSubAgentMailboxAsync(parent.Id, "/root/worker");
        Assert.Single(pending);
        Assert.Null(runtime.LastRequest);
    }

    [Fact]
    public async Task SubAgentClose_UsesAgentPathAndClosesEdge()
    {
        var (parent, child) = await CreatePathSubAgentAsync();

        var msg = _h.BuildRequest(AppServerMethods.SubAgentClose, new
        {
            parentThreadId = parent.Id,
            target = "/root/worker"
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var result = response.RootElement.GetProperty("result");
        Assert.Equal(ThreadSpawnEdgeStatus.Closed, result.GetProperty("status").GetString());
        Assert.Equal("/root/worker", result.GetProperty("agentPath").GetString());
        Assert.False(result.TryGetProperty("childThreadId", out _));
        var edge = Assert.Single(await _h.Service.ListSubAgentChildrenAsync(parent.Id, includeClosed: true));
        Assert.Equal(child.Id, edge.ChildThreadId);
        Assert.Equal(ThreadSpawnEdgeStatus.Closed, edge.Status);
        Assert.Empty(await _h.Service.ListSubAgentChildrenAsync(parent.Id));
        Assert.Equal(ThreadStatus.Archived, (await _h.Service.GetThreadAsync(child.Id)).Status);
    }

    [Fact]
    public async Task WorktreeCreateAndFork_ReturnsWorktreeThreadAndStatus()
    {
        InitializeGitWorkspace(_h.Identity.WorkspacePath);
        var source = await _h.Service.CreateThreadAsync(_h.Identity, displayName: "Source");
        AddCompletedTurn(source, "turn_001", "first");

        var msg = _h.BuildRequest(AppServerMethods.WorktreeCreateAndFork, new
        {
            sourceThreadId = source.Id,
            branchName = "dotcraft/appserver-worktree",
            displayName = "Worktree Branch"
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        var notification = await _h.Transport.ReadNextSentAsync();

        AppServerTestHarness.AssertIsSuccessResponse(response);
        var result = response.RootElement.GetProperty("result");
        var thread = result.GetProperty("thread");
        var worktree = result.GetProperty("worktree");
        var forkId = thread.GetProperty("id").GetString()!;
        var worktreePath = worktree.GetProperty("path").GetString()!;

        Assert.NotEqual(source.Id, forkId);
        Assert.Equal(source.Id, thread.GetProperty("forkedFromId").GetString());
        Assert.Equal(_h.Identity.WorkspacePath, thread.GetProperty("workspacePath").GetString());
        Assert.Equal(worktreePath, thread.GetProperty("effectiveWorkspacePath").GetString());
        Assert.Equal(worktreePath, thread.GetProperty("configuration").GetProperty("executionWorkspaceOverride").GetString());
        Assert.Equal(worktreePath, thread.GetProperty("worktree").GetProperty("path").GetString());
        Assert.Equal("dotcraft/appserver-worktree", worktree.GetProperty("branchName").GetString());
        Assert.True(File.Exists(Path.Combine(worktreePath, ".git")) || Directory.Exists(Path.Combine(worktreePath, ".git")));
        var turns = thread.GetProperty("turns").EnumerateArray().ToList();
        var turn = Assert.Single(turns);
        AssertForkBoundaryNotice(turn, source.Id);

        AppServerTestHarness.AssertIsNotification(notification, AppServerMethods.ThreadStarted);
        var notifiedThread = notification.RootElement.GetProperty("params").GetProperty("thread");
        Assert.Equal(forkId, notifiedThread.GetProperty("id").GetString());
        Assert.Equal(worktreePath, notifiedThread.GetProperty("effectiveWorkspacePath").GetString());
        Assert.False(notifiedThread.TryGetProperty("turns", out _));

        var listMsg = _h.BuildRequest(AppServerMethods.WorktreeList, new
        {
            identity = new { channelName = "appserver", userId = "test_user", workspacePath = _h.Identity.WorkspacePath }
        });
        await _h.ExecuteRequestAsync(listMsg);
        var listResponse = await _h.Transport.ReadNextSentAsync();
        var listed = Assert.Single(listResponse.RootElement.GetProperty("result").GetProperty("data").EnumerateArray());
        Assert.Equal(forkId, listed.GetProperty("threadId").GetString());

        var statusMsg = _h.BuildRequest(AppServerMethods.WorktreeStatus, new { threadId = forkId });
        await _h.ExecuteRequestAsync(statusMsg);
        var statusResponse = await _h.Transport.ReadNextSentAsync();
        var status = statusResponse.RootElement.GetProperty("result").GetProperty("status");
        Assert.True(status.GetProperty("exists").GetBoolean());
        Assert.True(status.GetProperty("isGitWorktree").GetBoolean());
        Assert.Equal("dotcraft/appserver-worktree", status.GetProperty("branchName").GetString());
    }

    // -------------------------------------------------------------------------
    // thread/resume (spec Section 4.2)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ThreadResume_ReturnsThread_EmitsResumedNotification()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        var msg = _h.BuildRequest(AppServerMethods.ThreadResume, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        var notification = await _h.Transport.ReadNextSentAsync();

        AppServerTestHarness.AssertIsSuccessResponse(response);
        Assert.Equal(thread.Id, response.RootElement
            .GetProperty("result").GetProperty("thread").GetProperty("id").GetString());

        AppServerTestHarness.AssertIsNotification(notification, AppServerMethods.ThreadResumed);
    }

    [Fact]
    public async Task ThreadResume_WithDynamicTools_BindsToolsAndRefreshesAgent()
    {
        var dynamicToolProxy = new WireDynamicToolProxy();
        using var harness = new AppServerTestHarness(wireDynamicToolProxy: dynamicToolProxy);
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);

        var msg = harness.BuildRequest(AppServerMethods.ThreadResume, new
        {
            threadId = thread.Id,
            dynamicTools = new[] { CreateReviewToolSpec() }
        });
        await harness.ExecuteRequestAsync(msg);

        var response = await harness.Transport.ReadNextSentAsync();
        var notification = await harness.Transport.ReadNextSentAsync();

        AppServerTestHarness.AssertIsSuccessResponse(response);
        AppServerTestHarness.AssertIsNotification(notification, AppServerMethods.ThreadResumed);
        Assert.Contains(thread.Id, harness.Service.RefreshedThreadAgents);
        var tool = Assert.Single(dynamicToolProxy.CreateToolsForThread(thread, EmptyReservedNames()));
        Assert.Equal("SubmitReviewDraft", tool.Name);
    }

    [Fact]
    public async Task ThreadResume_WithoutRuntimeAdditionalContext_KeepsExistingContext()
    {
        var runtimeContextProvider = new WireRuntimeAdditionalContextProvider();
        using var harness = new AppServerTestHarness(wireRuntimeAdditionalContextProvider: runtimeContextProvider);
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        runtimeContextProvider.BindThread(
            thread.Id,
            harness.Transport,
            harness.Connection,
            new Dictionary<string, RuntimeAdditionalContextEntry>
            {
                ["desktop.threadCoordination"] = new()
                {
                    Kind = RuntimeAdditionalContextKinds.Application,
                    Value = "existing context"
                }
            });

        var msg = harness.BuildRequest(AppServerMethods.ThreadResume, new { threadId = thread.Id });
        await harness.ExecuteRequestAsync(msg);

        var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var section = runtimeContextProvider.GetSystemPromptSection(new ThreadSystemPromptContext(thread.Id, harness.Identity.WorkspacePath));
        Assert.Contains("existing context", section);
    }

    [Fact]
    public async Task ThreadResume_WithEmptyRuntimeAdditionalContext_ClearsExistingContext()
    {
        var runtimeContextProvider = new WireRuntimeAdditionalContextProvider();
        using var harness = new AppServerTestHarness(wireRuntimeAdditionalContextProvider: runtimeContextProvider);
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        runtimeContextProvider.BindThread(
            thread.Id,
            harness.Transport,
            harness.Connection,
            new Dictionary<string, RuntimeAdditionalContextEntry>
            {
                ["desktop.threadCoordination"] = new()
                {
                    Kind = RuntimeAdditionalContextKinds.Application,
                    Value = "old context"
                }
            });

        var msg = harness.BuildRequest(AppServerMethods.ThreadResume, new
        {
            threadId = thread.Id,
            additionalContext = new Dictionary<string, RuntimeAdditionalContextEntry>()
        });
        await harness.ExecuteRequestAsync(msg);

        var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        Assert.Contains(thread.Id, harness.Service.RefreshedThreadAgents);
        Assert.Null(runtimeContextProvider.GetSystemPromptSection(new ThreadSystemPromptContext(thread.Id, harness.Identity.WorkspacePath)));
    }

    [Fact]
    public async Task ThreadResume_WithInvalidDynamicTools_ReturnsInvalidParamsWithoutRebind()
    {
        var dynamicToolProxy = new WireDynamicToolProxy();
        using var harness = new AppServerTestHarness(wireDynamicToolProxy: dynamicToolProxy);
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        var invalidSpec = CreateReviewToolSpec();
        invalidSpec.Description = "";

        var msg = harness.BuildRequest(AppServerMethods.ThreadResume, new
        {
            threadId = thread.Id,
            dynamicTools = new[] { invalidSpec }
        });
        await harness.ExecuteRequestAsync(msg);

        var response = await harness.Transport.ReadNextSentAsync();

        AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.InvalidParamsCode);
        Assert.Empty(harness.Service.RefreshedThreadAgents);
        Assert.Empty(dynamicToolProxy.CreateToolsForThread(thread, EmptyReservedNames()));
    }

    // -------------------------------------------------------------------------
    // thread/pause (spec Section 4)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ThreadPause_EmitsStatusChangedNotification()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        var msg = _h.BuildRequest(AppServerMethods.ThreadPause, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        var notification = await _h.Transport.ReadNextSentAsync();

        AppServerTestHarness.AssertIsSuccessResponse(response);
        AppServerTestHarness.AssertIsNotification(notification, AppServerMethods.ThreadStatusChanged);
        Assert.Equal("paused",
            notification.RootElement.GetProperty("params").GetProperty("newStatus").GetString());
    }

    [Fact]
    public async Task ThreadPause_NotificationIncludesPreviousStatus()
    {
        // Gap B: previousStatus must be present in thread/statusChanged notification
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        Assert.Equal(ThreadStatus.Active, thread.Status);

        var msg = _h.BuildRequest(AppServerMethods.ThreadPause, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(msg);

        await _h.Transport.ReadNextSentAsync(); // response
        var notification = await _h.Transport.ReadNextSentAsync();

        var @params = notification.RootElement.GetProperty("params");
        Assert.Equal("active", @params.GetProperty("previousStatus").GetString());
        Assert.Equal("paused", @params.GetProperty("newStatus").GetString());
    }

    [Fact]
    public async Task ThreadPause_WhenSubscribed_SendsOnlyResponse_NoDuplicateNotification()
    {
        // Gap C: if the connection has an active subscription to the thread, the handler
        // must not send an inline notification (the broker/dispatcher path handles it).
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        // Subscribe to the thread first
        var subscribeMsg = _h.BuildRequest(AppServerMethods.ThreadSubscribe, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(subscribeMsg);
        await _h.Transport.ReadNextSentAsync(); // drain subscribe response

        // Now pause — should produce exactly one message (the response), not two
        var pauseMsg = _h.BuildRequest(AppServerMethods.ThreadPause, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(pauseMsg);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);

        // Verify no additional message was sent (no duplicate notification)
        await Task.Delay(20); // small delay to let any fire-and-forget tasks settle
        var extra = _h.Transport.TryReadSent();
        Assert.Null(extra);
    }

    [Fact]
    public async Task ThreadPause_AlreadyPaused_SendsOnlyResponse_NoNotification()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        await _h.Service.PauseThreadAsync(thread.Id); // pre-pause

        var msg = _h.BuildRequest(AppServerMethods.ThreadPause, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);

        await Task.Delay(20);
        Assert.Null(_h.Transport.TryReadSent());
    }

    // -------------------------------------------------------------------------
    // thread/archive
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ThreadArchive_EmitsStatusChangedNotification()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        var msg = _h.BuildRequest(AppServerMethods.ThreadArchive, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        var notification = await _h.Transport.ReadNextSentAsync();

        AppServerTestHarness.AssertIsSuccessResponse(response);
        AppServerTestHarness.AssertIsNotification(notification, AppServerMethods.ThreadStatusChanged);
        Assert.Equal("archived",
            notification.RootElement.GetProperty("params").GetProperty("newStatus").GetString());
    }

    [Fact]
    public async Task ThreadArchive_NotificationIncludesPreviousStatus()
    {
        // Gap B: previousStatus must be present in thread/statusChanged notification
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        var msg = _h.BuildRequest(AppServerMethods.ThreadArchive, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(msg);

        await _h.Transport.ReadNextSentAsync(); // response
        var notification = await _h.Transport.ReadNextSentAsync();

        var @params = notification.RootElement.GetProperty("params");
        Assert.Equal("active", @params.GetProperty("previousStatus").GetString());
        Assert.Equal("archived", @params.GetProperty("newStatus").GetString());
    }

    [Fact]
    public async Task ThreadArchive_WhenSubscribed_SendsOnlyResponse_NoDuplicateNotification()
    {
        // Gap C: subscribed connection should not receive a duplicate statusChanged
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        var subscribeMsg = _h.BuildRequest(AppServerMethods.ThreadSubscribe, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(subscribeMsg);
        await _h.Transport.ReadNextSentAsync(); // drain subscribe response

        var archiveMsg = _h.BuildRequest(AppServerMethods.ThreadArchive, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(archiveMsg);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);

        await Task.Delay(20);
        Assert.Null(_h.Transport.TryReadSent());
    }

    [Fact]
    public async Task ThreadUnarchive_EmitsStatusChangedNotification()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        await _h.Service.ArchiveThreadAsync(thread.Id);

        var msg = _h.BuildRequest(AppServerMethods.ThreadUnarchive, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        var notification = await _h.Transport.ReadNextSentAsync();

        AppServerTestHarness.AssertIsSuccessResponse(response);
        AppServerTestHarness.AssertIsNotification(notification, AppServerMethods.ThreadStatusChanged);
        Assert.Equal("active",
            notification.RootElement.GetProperty("params").GetProperty("newStatus").GetString());
    }

    [Fact]
    public async Task ThreadUnarchive_NotificationIncludesPreviousStatus()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        await _h.Service.ArchiveThreadAsync(thread.Id);

        var msg = _h.BuildRequest(AppServerMethods.ThreadUnarchive, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(msg);

        await _h.Transport.ReadNextSentAsync(); // response
        var notification = await _h.Transport.ReadNextSentAsync();

        var @params = notification.RootElement.GetProperty("params");
        Assert.Equal("archived", @params.GetProperty("previousStatus").GetString());
        Assert.Equal("active", @params.GetProperty("newStatus").GetString());
    }

    [Fact]
    public async Task ThreadUnarchive_WhenSubscribed_SendsOnlyResponse_NoDuplicateNotification()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        await _h.Service.ArchiveThreadAsync(thread.Id);

        var subscribeMsg = _h.BuildRequest(AppServerMethods.ThreadSubscribe, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(subscribeMsg);
        await _h.Transport.ReadNextSentAsync(); // drain subscribe response

        var unarchiveMsg = _h.BuildRequest(AppServerMethods.ThreadUnarchive, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(unarchiveMsg);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);

        await Task.Delay(20);
        Assert.Null(_h.Transport.TryReadSent());
    }

    [Fact]
    public async Task ThreadUnarchive_AlreadyActive_SendsOnlyResponse_NoNotification()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        var msg = _h.BuildRequest(AppServerMethods.ThreadUnarchive, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);

        await Task.Delay(20);
        Assert.Null(_h.Transport.TryReadSent());
    }

    // -------------------------------------------------------------------------
    // thread/resume — resumedBy (Gap D)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ThreadResume_NotificationIncludesClientNameAsResumedBy()
    {
        // Gap D: resumedBy must use the client's declared name from initialize, not hardcoded "appserver"
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        var msg = _h.BuildRequest(AppServerMethods.ThreadResume, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(msg);

        await _h.Transport.ReadNextSentAsync(); // response
        var notification = await _h.Transport.ReadNextSentAsync();

        AppServerTestHarness.AssertIsNotification(notification, AppServerMethods.ThreadResumed);
        // The harness initializes with clientInfo.name = "test-client"
        Assert.Equal("test-client",
            notification.RootElement.GetProperty("params").GetProperty("resumedBy").GetString());
    }

    [Fact]
    public async Task ThreadResume_WhenSubscribed_SendsOnlyResponse_NoDuplicateNotification()
    {
        // Gap C: subscribed connection should not receive a duplicate thread/resumed notification
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        var subscribeMsg = _h.BuildRequest(AppServerMethods.ThreadSubscribe, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(subscribeMsg);
        await _h.Transport.ReadNextSentAsync(); // drain subscribe response

        var resumeMsg = _h.BuildRequest(AppServerMethods.ThreadResume, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(resumeMsg);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);

        await Task.Delay(20);
        Assert.Null(_h.Transport.TryReadSent());
    }

    // -------------------------------------------------------------------------
    // thread/list
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ThreadList_ReturnsCreatedThreads()
    {
        await _h.Service.CreateThreadAsync(_h.Identity);
        await _h.Service.CreateThreadAsync(_h.Identity);

        var msg = _h.BuildRequest(AppServerMethods.ThreadList, new
        {
            identity = new
            {
                channelName = _h.Identity.ChannelName,
                userId = _h.Identity.UserId,
                workspacePath = _h.Identity.WorkspacePath
            }
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(doc);
        var data = doc.RootElement.GetProperty("result").GetProperty("data");
        Assert.Equal(2, data.GetArrayLength());
    }

    [Fact]
    public async Task ThreadList_WithLimitAndCursor_ReturnsPages()
    {
        await _h.Service.CreateThreadAsync(_h.Identity, displayName: "First");
        await _h.Service.CreateThreadAsync(_h.Identity, displayName: "Second");
        await _h.Service.CreateThreadAsync(_h.Identity, displayName: "Third");

        var firstMsg = _h.BuildRequest(AppServerMethods.ThreadList, new
        {
            identity = new
            {
                channelName = _h.Identity.ChannelName,
                userId = _h.Identity.UserId,
                workspacePath = _h.Identity.WorkspacePath
            },
            limit = 2
        });
        await _h.ExecuteRequestAsync(firstMsg);

        var firstDoc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(firstDoc);
        var firstResult = firstDoc.RootElement.GetProperty("result");
        var firstData = firstResult.GetProperty("data");
        Assert.Equal(2, firstData.GetArrayLength());
        Assert.Equal(3, firstResult.GetProperty("totalMatched").GetInt32());
        var cursor = firstResult.GetProperty("nextCursor").GetString();
        Assert.False(string.IsNullOrWhiteSpace(cursor));

        var secondMsg = _h.BuildRequest(AppServerMethods.ThreadList, new
        {
            identity = new
            {
                channelName = _h.Identity.ChannelName,
                userId = _h.Identity.UserId,
                workspacePath = _h.Identity.WorkspacePath
            },
            limit = 2,
            cursor
        });
        await _h.ExecuteRequestAsync(secondMsg);

        var secondDoc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(secondDoc);
        var secondResult = secondDoc.RootElement.GetProperty("result");
        var secondData = secondResult.GetProperty("data");
        var onlySecondPageId = Assert.Single(secondData.EnumerateArray()).GetProperty("id").GetString();
        Assert.DoesNotContain(firstData.EnumerateArray(), item => item.GetProperty("id").GetString() == onlySecondPageId);
        Assert.False(secondResult.TryGetProperty("nextCursor", out _));
    }

    [Fact]
    public async Task ThreadList_QueryFiltersBeforePagination()
    {
        await _h.Service.CreateThreadAsync(_h.Identity, displayName: "Renderer Search");
        await _h.Service.CreateThreadAsync(_h.Identity, displayName: "Backend Task");

        var msg = _h.BuildRequest(AppServerMethods.ThreadList, new
        {
            identity = new
            {
                channelName = _h.Identity.ChannelName,
                userId = _h.Identity.UserId,
                workspacePath = _h.Identity.WorkspacePath
            },
            query = "renderer",
            limit = 10
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(doc);
        var result = doc.RootElement.GetProperty("result");
        var only = Assert.Single(result.GetProperty("data").EnumerateArray());
        Assert.Equal("Renderer Search", only.GetProperty("displayName").GetString());
        Assert.Equal(1, result.GetProperty("totalMatched").GetInt32());
    }

    [Fact]
    public async Task ThreadList_InvalidCursor_ReturnsInvalidParams()
    {
        var msg = _h.BuildRequest(AppServerMethods.ThreadList, new
        {
            identity = new
            {
                channelName = _h.Identity.ChannelName,
                userId = _h.Identity.UserId,
                workspacePath = _h.Identity.WorkspacePath
            },
            limit = 1,
            cursor = "not-a-valid-cursor"
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(doc, AppServerErrors.InvalidParamsCode);
    }

    [Fact]
    public async Task ThreadList_ExcludesInternalThreadsByDefault()
    {
        await _h.Service.CreateThreadAsync(_h.Identity);
        var internalThread = await _h.Service.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = WelcomeSuggestionConstants.ChannelName,
            UserId = WelcomeSuggestionConstants.InternalUserId,
            WorkspacePath = _h.Identity.WorkspacePath,
            ChannelContext = _h.Identity.ChannelContext
        });
        internalThread.Metadata[ThreadVisibility.InternalMetadataKey] = WelcomeSuggestionConstants.InternalMetadataValue;

        var msg = _h.BuildRequest(AppServerMethods.ThreadList, new
        {
            identity = new
            {
                channelName = _h.Identity.ChannelName,
                userId = _h.Identity.UserId,
                workspacePath = _h.Identity.WorkspacePath
            },
            crossChannelOrigins = new[] { WelcomeSuggestionConstants.ChannelName }
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(doc);
        var data = doc.RootElement.GetProperty("result").GetProperty("data");
        Assert.Equal(1, data.GetArrayLength());
        Assert.DoesNotContain(data.EnumerateArray(), item => item.GetProperty("id").GetString() == internalThread.Id);
    }

    [Fact]
    public async Task ThreadList_EmptyWorkspace_ReturnsEmpty()
    {
        var msg = _h.BuildRequest(AppServerMethods.ThreadList, new
        {
            identity = new
            {
                channelName = "appserver",
                userId = "nobody",
                workspacePath = _h.Identity.WorkspacePath
            }
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(doc);
        Assert.Equal(0, doc.RootElement.GetProperty("result").GetProperty("data").GetArrayLength());
    }

    [Fact]
    public async Task ThreadList_HydratesActiveMaintenanceRuntime()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        _h.Service.RuntimeSnapshotHandler = t => ThreadSummaryRuntime.FromThread(t, "compacting");

        var msg = _h.BuildRequest(AppServerMethods.ThreadList, new
        {
            identity = new
            {
                channelName = _h.Identity.ChannelName,
                userId = _h.Identity.UserId,
                workspacePath = _h.Identity.WorkspacePath
            }
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(doc);
        var data = doc.RootElement.GetProperty("result").GetProperty("data");
        var returned = Assert.Single(data.EnumerateArray(), item => item.GetProperty("id").GetString() == thread.Id);
        var runtime = returned.GetProperty("runtime");
        Assert.True(runtime.GetProperty("busy").GetBoolean());
        Assert.Equal("compacting", runtime.GetProperty("maintenanceKind").GetString());
    }

    // -------------------------------------------------------------------------
    // thread/read
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ThreadRead_ReturnsThreadById()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        var msg = _h.BuildRequest(AppServerMethods.ThreadRead, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(doc);
        Assert.Equal(thread.Id,
            doc.RootElement.GetProperty("result").GetProperty("thread").GetProperty("id").GetString());
    }

    [Fact]
    public async Task ThreadRead_ReturnsPersistedPlanForThread()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        var planStore = new PlanStore(_h.Identity.WorkspacePath);
        await planStore.SaveStructuredPlanAsync(thread.Id, new StructuredPlan
        {
            Title = "Hydrated Plan",
            Overview = "Restored on thread switch",
            Content = "# Hydrated Plan\n\n## Test Plan\n\nRun focused checks.",
            Todos =
            [
                new PlanTodo
                {
                    Id = "restore-plan",
                    Content = "Restore persisted plan in Desktop",
                    Priority = PlanTodoPriority.High,
                    Status = PlanTodoStatus.InProgress
                }
            ],
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        var msg = _h.BuildRequest(AppServerMethods.ThreadRead, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(doc);
        var plan = doc.RootElement.GetProperty("result").GetProperty("thread").GetProperty("plan");
        Assert.Equal("Hydrated Plan", plan.GetProperty("title").GetString());
        Assert.Equal("Restored on thread switch", plan.GetProperty("overview").GetString());
        var todo = Assert.Single(plan.GetProperty("todos").EnumerateArray());
        Assert.Equal("restore-plan", todo.GetProperty("id").GetString());
        Assert.Equal("in_progress", todo.GetProperty("status").GetString());
    }

    [Fact]
    public async Task ThreadRead_HydratesActiveMaintenanceRuntime()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        _h.Service.RuntimeSnapshotHandler = t => ThreadSummaryRuntime.FromThread(t, "consolidating");

        var msg = _h.BuildRequest(AppServerMethods.ThreadRead, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(doc);
        var runtime = doc.RootElement
            .GetProperty("result")
            .GetProperty("thread")
            .GetProperty("runtime");
        Assert.True(runtime.GetProperty("busy").GetBoolean());
        Assert.Equal("consolidating", runtime.GetProperty("maintenanceKind").GetString());
    }

    [Fact]
    public async Task ThreadRead_WithTurnLimitAndCursor_ReturnsRecentThenOlderPages()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        AddCompletedTurn(thread, "turn_001", "first");
        AddCompletedTurn(thread, "turn_002", "second");
        AddCompletedTurn(thread, "turn_003", "third");
        AddCompletedTurn(thread, "turn_004", "fourth");
        AddCompletedTurn(thread, "turn_005", "fifth");

        var firstMsg = _h.BuildRequest(AppServerMethods.ThreadRead, new
        {
            threadId = thread.Id,
            includeTurns = true,
            turnLimit = 2
        });
        await _h.ExecuteRequestAsync(firstMsg);

        var firstDoc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(firstDoc);
        var firstResult = firstDoc.RootElement.GetProperty("result");
        var firstTurns = firstResult.GetProperty("thread").GetProperty("turns");
        Assert.Equal(["turn_004", "turn_005"], firstTurns.EnumerateArray().Select(t => t.GetProperty("id").GetString()).ToArray());
        var firstPage = firstResult.GetProperty("turnPage");
        Assert.Equal(5, firstPage.GetProperty("totalTurns").GetInt32());
        Assert.Equal(4, firstPage.GetProperty("startOrdinal").GetInt32());
        Assert.Equal(5, firstPage.GetProperty("endOrdinal").GetInt32());
        Assert.True(firstPage.GetProperty("hasMore").GetBoolean());
        var cursor = firstPage.GetProperty("nextCursor").GetString();

        var secondMsg = _h.BuildRequest(AppServerMethods.ThreadRead, new
        {
            threadId = thread.Id,
            includeTurns = true,
            turnLimit = 2,
            cursor
        });
        await _h.ExecuteRequestAsync(secondMsg);

        var secondDoc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(secondDoc);
        var secondResult = secondDoc.RootElement.GetProperty("result");
        var secondTurns = secondResult.GetProperty("thread").GetProperty("turns");
        Assert.Equal(["turn_002", "turn_003"], secondTurns.EnumerateArray().Select(t => t.GetProperty("id").GetString()).ToArray());
        Assert.Equal(2, secondResult.GetProperty("turnPage").GetProperty("startOrdinal").GetInt32());
        Assert.Equal(3, secondResult.GetProperty("turnPage").GetProperty("endOrdinal").GetInt32());
    }

    [Fact]
    public async Task ThreadRead_WithPagedTurns_StillReturnsQueuedInputs()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        AddCompletedTurn(thread, "turn_001", "first");
        thread.QueuedInputs.Add(new QueuedTurnInput
        {
            Id = "queued_001",
            ThreadId = thread.Id,
            DisplayText = "queued follow-up",
            Status = "queued",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var msg = _h.BuildRequest(AppServerMethods.ThreadRead, new
        {
            threadId = thread.Id,
            includeTurns = true,
            turnLimit = 1
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(doc);
        var queued = doc.RootElement.GetProperty("result").GetProperty("thread").GetProperty("queuedInputs");
        var only = Assert.Single(queued.EnumerateArray());
        Assert.Equal("queued follow-up", only.GetProperty("displayText").GetString());
    }

    // -------------------------------------------------------------------------
    // thread/rollback
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ThreadRollback_ReturnsThreadWithRemainingTurns()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        AddCompletedTurn(thread, "turn_001", "first");
        AddCompletedTurn(thread, "turn_002", "second");

        var msg = _h.BuildRequest(AppServerMethods.ThreadRollback, new { threadId = thread.Id, numTurns = 1 });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(doc);
        var returned = doc.RootElement.GetProperty("result").GetProperty("thread");
        Assert.Equal(thread.Id, returned.GetProperty("id").GetString());
        var turns = returned.GetProperty("turns");
        var remaining = Assert.Single(turns.EnumerateArray());
        Assert.Equal("turn_001", remaining.GetProperty("id").GetString());
        Assert.Equal("first", remaining.GetProperty("items")[0].GetProperty("payload").GetProperty("text").GetString());
    }

    [Fact]
    public async Task ThreadRollback_ThenThreadReadIncludeTurns_ReturnsSameTurns()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        AddCompletedTurn(thread, "turn_001", "first");
        AddCompletedTurn(thread, "turn_002", "second");

        var rollbackMsg = _h.BuildRequest(AppServerMethods.ThreadRollback, new { threadId = thread.Id, numTurns = 1 });
        await _h.ExecuteRequestAsync(rollbackMsg);
        var rollbackDoc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(rollbackDoc);

        var readMsg = _h.BuildRequest(AppServerMethods.ThreadRead, new { threadId = thread.Id, includeTurns = true });
        await _h.ExecuteRequestAsync(readMsg);
        var readDoc = await _h.Transport.ReadNextSentAsync();

        AppServerTestHarness.AssertIsSuccessResponse(readDoc);
        var rollbackTurns = rollbackDoc.RootElement.GetProperty("result").GetProperty("thread").GetProperty("turns");
        var readTurns = readDoc.RootElement.GetProperty("result").GetProperty("thread").GetProperty("turns");
        Assert.Equal(rollbackTurns.GetRawText(), readTurns.GetRawText());
    }

    [Fact]
    public async Task ThreadRollback_WithZeroTurns_ReturnsInvalidParams()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        var msg = _h.BuildRequest(AppServerMethods.ThreadRollback, new { threadId = thread.Id, numTurns = 0 });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(doc, AppServerErrors.InvalidParamsCode);
    }

    // -------------------------------------------------------------------------
    // thread/delete
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ThreadDelete_RemovesThread()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        var msg = _h.BuildRequest(AppServerMethods.ThreadDelete, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(doc);
    }

    private static void AddCompletedTurn(SessionThread thread, string turnId, string text)
    {
        var now = DateTimeOffset.UtcNow.AddSeconds(thread.Turns.Count);
        var turn = new SessionTurn
        {
            Id = turnId,
            ThreadId = thread.Id,
            Status = TurnStatus.Completed,
            StartedAt = now,
            CompletedAt = now.AddMilliseconds(1)
        };
        var userItem = new SessionItem
        {
            Id = $"{turnId}_user",
            TurnId = turnId,
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            CreatedAt = now,
            CompletedAt = now,
            Payload = new UserMessagePayload { Text = text }
        };
        turn.Input = userItem;
        turn.Items.Add(userItem);
        thread.Turns.Add(turn);
    }

    private async Task<(SessionThread Parent, SessionThread Child)> CreatePathSubAgentAsync(
        AppServerTestHarness? harness = null,
        string runtimeType = NativeSubAgentRuntime.RuntimeTypeName,
        string profileName = SubAgentCoordinator.DefaultProfileName)
    {
        var h = harness ?? _h;
        var parent = await h.Service.CreateThreadAsync(h.Identity);
        var child = await h.Service.CreateThreadAsync(
            new SessionIdentity
            {
                ChannelName = SubAgentThreadOrigin.ChannelName,
                UserId = parent.UserId,
                WorkspacePath = parent.WorkspacePath,
                ChannelContext = parent.Id
            },
            threadId: $"thread_child_{Guid.NewGuid():N}",
            displayName: "Worker",
            source: ThreadSource.ForSubAgent(new SubAgentThreadSource
            {
                ParentThreadId = parent.Id,
                ParentTurnId = "turn_1",
                RootThreadId = parent.Id,
                Depth = 1,
                AgentPath = "/root/worker",
                TaskName = "worker",
                AgentNickname = "Worker",
                ProfileName = profileName,
                RuntimeType = runtimeType,
                SupportsSendMessage = true,
                SupportsFollowupTask = true,
                SupportsClose = true
            }));
        await h.Service.UpsertThreadSpawnEdgeAsync(new ThreadSpawnEdge
        {
            ParentThreadId = parent.Id,
            ChildThreadId = child.Id,
            ParentTurnId = "turn_1",
            Depth = 1,
            AgentPath = "/root/worker",
            TaskName = "worker",
            AgentNickname = "Worker",
            AgentRole = "worker",
            ProfileName = profileName,
            RuntimeType = runtimeType,
            SupportsSendMessage = true,
            SupportsFollowupTask = true,
            SupportsClose = true,
            Status = ThreadSpawnEdgeStatus.Open
        });

        return (parent, child);
    }

    private static SubAgentCoordinator CreateCoordinator(string workspacePath, FakeSubAgentRuntime runtime) =>
        new(
            workspacePath,
            [runtime],
            [
                new SubAgentProfile
                {
                    Name = "cli-run",
                    Runtime = CliOneshotRuntime.RuntimeTypeName,
                    WorkingDirectoryMode = "workspace",
                    Bin = "test-cli",
                    InputMode = "arg",
                    OutputFormat = "text"
                }
            ]);

    private sealed class FakeSubAgentRuntime(string runtimeType, string resultText) : ISubAgentRuntime
    {
        public string RuntimeType { get; } = runtimeType;

        public SubAgentTaskRequest? LastRequest { get; private set; }

        public Task<SubAgentSessionHandle> CreateSessionAsync(
            SubAgentProfile profile,
            SubAgentLaunchContext context,
            CancellationToken cancellationToken)
            => Task.FromResult(new SubAgentSessionHandle(RuntimeType, profile.Name));

        public Task<DotCraft.Agents.SubAgentRunResult> RunAsync(
            SubAgentSessionHandle session,
            SubAgentTaskRequest request,
            ISubAgentEventSink sink,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new DotCraft.Agents.SubAgentRunResult { Text = resultText });
        }

        public Task CancelAsync(SubAgentSessionHandle session, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DisposeSessionAsync(SubAgentSessionHandle session, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static void AssertForkBoundaryNotice(JsonElement turn, string sourceThreadId)
    {
        var items = turn.GetProperty("items").EnumerateArray().ToList();
        var marker = Assert.Single(items, item => item.GetProperty("type").GetString() == "systemNotice");
        var payload = marker.GetProperty("payload");
        Assert.Equal("forked", payload.GetProperty("kind").GetString());
        Assert.Equal(sourceThreadId, payload.GetProperty("sourceThreadId").GetString());
    }

    private static void InitializeGitWorkspace(string workspacePath)
    {
        RunGit(workspacePath, "init");
        RunGit(workspacePath, "config", "user.email", "test@example.com");
        RunGit(workspacePath, "config", "user.name", "Test User");
        File.WriteAllText(Path.Combine(workspacePath, ".gitignore"), ".craft/" + Environment.NewLine);
        File.WriteAllText(Path.Combine(workspacePath, "README.md"), "initial" + Environment.NewLine);
        RunGit(workspacePath, "add", ".gitignore", "README.md");
        RunGit(workspacePath, "commit", "-m", "init");
    }

    private static void RunGit(string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git setup command.");
        process.StandardInput.Close();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"git {string.Join(" ", args)} timed out.");
        }

        var stderr = process.StandardError.ReadToEnd();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(" ", args)} failed: {stderr}");
    }

    private static DynamicToolSpec CreateReviewToolSpec()
        => new()
        {
            Namespace = "oratorio",
            Name = "SubmitReviewDraft",
            Description = "Submit a structured code review draft",
            InputSchema = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["body"] = new JsonObject { ["type"] = "string" }
                },
                ["required"] = new JsonArray("body")
            }
        };

    private static IReadOnlySet<string> EmptyReservedNames()
        => new HashSet<string>(StringComparer.Ordinal);
}
