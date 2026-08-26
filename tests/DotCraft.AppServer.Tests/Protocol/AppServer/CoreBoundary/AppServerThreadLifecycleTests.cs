using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Context;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using DotCraft.AppServer;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using DynamicToolCallPayload = DotCraft.Sessions.DynamicToolCallPayload;
using QueuedTurnInput = DotCraft.Sessions.QueuedTurnInput;
using SessionIdentity = DotCraft.Sessions.SessionIdentity;
using SessionItem = DotCraft.Sessions.SessionItem;
using SessionThread = DotCraft.Sessions.SessionThread;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using PlanTodo = DotCraft.Memory.PlanTodo;
using SubAgentThreadSource = DotCraft.Sessions.SubAgentThreadSource;
using ThreadOriginPresentationSnapshot = DotCraft.Sessions.ThreadOriginPresentationSnapshot;
using ThreadSource = DotCraft.Sessions.ThreadSource;
using ThreadSpawnEdge = DotCraft.Sessions.ThreadSpawnEdge;
using UserMessagePayload = DotCraft.Sessions.UserMessagePayload;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

/// <summary>
/// Tests for thread/* methods (spec Section 4).
/// Verifies response shapes and the post-response notifications emitted
/// after thread/start (→ thread/started), thread/resume (→ thread/resumed),
/// thread/pause and thread/archive (→ thread/statusChanged).
/// </summary>
public sealed class AppServerThreadLifecycleTests : IDisposable
{
    private readonly CoreAppServerTestHarness _h = new();

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
        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, new
        {
            identity = new { channelName = "appserver", userId = "test_user", workspacePath = _h.Identity.WorkspacePath }
        });
        await _h.ExecuteRequestAsync(msg);

        // thread/start sends response inline; read it from transport
        var response = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(response);
        var result = response.RootElement.GetProperty("result");
        Assert.Empty(result.GetProperty("instructionSources").EnumerateArray());
        var thread = result.GetProperty("thread");
        Assert.StartsWith("thread_", thread.GetProperty("id").GetString()!);
        Assert.Equal("active", thread.GetProperty("status").GetString());
    }

    [Fact]
    public async Task ThreadStart_ReturnsInstructionSources()
    {
        var instructionPath = Path.GetFullPath(Path.Combine(_h.Identity.WorkspacePath, "AGENTS.md"));
        _h.Service.InstructionSourcesHandler = (_, _) =>
            Task.FromResult<IReadOnlyList<string>>([instructionPath]);
        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, new
        {
            identity = new
            {
                channelName = "appserver",
                userId = "test_user",
                workspacePath = _h.Identity.WorkspacePath
            }
        });

        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(response);
        Assert.Equal(
            [instructionPath],
            response.RootElement.GetProperty("result").GetProperty("instructionSources")
                .EnumerateArray()
                .Select(static source => source.GetString()!)
                .ToArray());
    }

    [Fact]
    public async Task ThreadStart_AppliesAndReturnsRuntimeWorkspaceRoots()
    {
        var primary = Path.GetFullPath(_h.Identity.WorkspacePath);
        var secondary = Path.GetFullPath(Path.Combine(primary, "..", "secondary"));
        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, new
        {
            identity = new { channelName = "appserver", userId = "test_user", workspacePath = primary },
            cwd = primary,
            runtimeWorkspaceRoots = new[] { primary, secondary, primary }
        });

        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(response);
        var thread = response.RootElement.GetProperty("result").GetProperty("thread");
        Assert.Equal(primary, thread.GetProperty("cwd").GetString());
        Assert.Equal(
            [primary, secondary],
            thread.GetProperty("runtimeWorkspaceRoots")
                .EnumerateArray()
                .Select(item => item.GetString()!)
                .ToArray());
    }

    [Fact]
    public async Task ItemWidgetState_DoesNotSurfaceOnRuntimeDynamicItems()
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

        await _h.ExecuteRequestAsync(_h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ItemWidgetStateSet, new
        {
            threadId = thread.Id,
            callId = "call_widget",
            widgetState = new { tab = 2, scroll = 120 }
        }));
        using (var setResp = await _h.Transport.ReadNextSentAsync())
        {
            CoreAppServerTestHarness.AssertIsSuccessResponse(setResp);
            Assert.False(setResp.RootElement.GetProperty("result").GetProperty("cleared").GetBoolean());
        }

        await _h.ExecuteRequestAsync(_h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadItemsList, new { threadId = thread.Id }));
        using (var readResp = await _h.Transport.ReadNextSentAsync())
        {
            CoreAppServerTestHarness.AssertIsSuccessResponse(readResp);
            var item = readResp.RootElement.GetProperty("result").GetProperty("data")[0]
                .GetProperty("item");
            Assert.False(item.GetProperty("payload").TryGetProperty("widgetState", out _));
        }

        await _h.ExecuteRequestAsync(_h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ItemWidgetStateSet, new
        {
            threadId = thread.Id,
            callId = "call_widget"
        }));
        using (var clearResp = await _h.Transport.ReadNextSentAsync())
        {
            CoreAppServerTestHarness.AssertIsSuccessResponse(clearResp);
            Assert.True(clearResp.RootElement.GetProperty("result").GetProperty("cleared").GetBoolean());
        }

        Assert.Empty(_h.Service.GetItemWidgetStates(thread.Id));
    }

    [Fact]
    public async Task ItemWidgetState_RejectsOversizedState()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        await _h.Service.SeedThreadAsync(thread);

        await _h.ExecuteRequestAsync(_h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ItemWidgetStateSet, new
        {
            threadId = thread.Id,
            callId = "call_widget",
            widgetState = new { blob = new string('x', 9000) }
        }));

        using var resp = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsErrorResponse(resp, AppServerErrors.InvalidParamsCode);
    }

    [Fact]
    public async Task ThreadStart_OmitsWorkspacePath_NormalizesToHostWorkspace()
    {
        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, new
        {
            identity = new { channelName = "appserver", userId = "test_user_no_ws" }
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(response);
        var thread = response.RootElement.GetProperty("result").GetProperty("thread");
        Assert.Equal(_h.Identity.WorkspacePath, thread.GetProperty("workspacePath").GetString());
    }

    [Fact]
    public async Task ThreadStart_WithRuntimeAdditionalContext_BindsContextAndRefreshesAgent()
    {
        var runtimeContextProvider = new WireRuntimeAdditionalContextProvider();
        using var h = new CoreAppServerTestHarness(wireRuntimeAdditionalContextProvider: runtimeContextProvider);
        await h.InitializeAsync();

        var msg = h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, new
        {
            identity = new { channelName = "appserver", userId = "test_user", workspacePath = h.Identity.WorkspacePath },
            additionalContext = new Dictionary<string, RuntimeAdditionalContextValue>
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
        CoreAppServerTestHarness.AssertIsSuccessResponse(response);
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
        using var h = new CoreAppServerTestHarness(
            appConfigMonitor: new AppConfigMonitor(
                AppConfigTestFactory.CreateAnthropic(model: "claude-mythos-preview")));
        await h.InitializeAsync();

        var msg = h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, new
        {
            identity = new { channelName = "appserver", userId = "test_user", workspacePath = h.Identity.WorkspacePath },
            config = new
            {
                reasoning = new { enabled = false, effort = "high", output = "full" }
            }
        });
        await h.ExecuteRequestAsync(msg);

        var response = await h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.InvalidParamsCode);
    }

    [Fact]
    public async Task ThreadStart_WithUnsupportedContextWindowMax_ReturnsInvalidParams()
    {
        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, new
        {
            identity = new { channelName = "appserver", userId = "test_user", workspacePath = _h.Identity.WorkspacePath },
            config = new
            {
                model = "manual-model-without-catalog-entry",
                contextWindow = new { mode = "max" }
            }
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.InvalidParamsCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(86401)]
    public async Task ThreadStart_WithInvalidApprovalTimeout_ReturnsInvalidParams(int seconds)
    {
        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, new
        {
            identity = new { channelName = "appserver", userId = "test_user", workspacePath = _h.Identity.WorkspacePath },
            config = new { approvalTimeoutSeconds = seconds }
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.InvalidParamsCode);
    }

    [Fact]
    public async Task ThreadStart_PersistsApprovalTimeoutOverride()
    {
        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, new
        {
            identity = new { channelName = "appserver", userId = "test_user", workspacePath = _h.Identity.WorkspacePath },
            config = new { approvalTimeoutSeconds = 1800 }
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(response);
        var config = response.RootElement.GetProperty("result").GetProperty("thread").GetProperty("configuration");
        Assert.Equal(1800, config.GetProperty("approvalTimeoutSeconds").GetInt32());
    }

    [Fact]
    public async Task ThreadConfigUpdate_WithSupportedContextWindowMax_BroadcastsThreadUpdated()
    {
        _h.Service.ThreadUpdatedForBroadcast = thread =>
        {
            _h.Transport.WriteMessageAsync(new
            {
                jsonrpc = "2.0",
                method = DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadUpdated,
                @params = new { thread = thread.ToWire() }
            }, default).GetAwaiter().GetResult();
        };

        var start = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, new
        {
            identity = new { channelName = "appserver", userId = "test_user", workspacePath = _h.Identity.WorkspacePath },
            config = new
            {
                model = "gpt-5.5"
            }
        });
        await _h.ExecuteRequestAsync(start);
        var startResponse = await _h.Transport.ReadNextSentAsync();
        _ = await _h.Transport.ReadNextSentAsync();
        var threadId = startResponse.RootElement.GetProperty("result").GetProperty("thread").GetProperty("id").GetString()!;

        try
        {
            var update = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadConfigUpdate, new
            {
                threadId,
                config = new
                {
                    mode = "agent",
                    model = "gpt-5.5",
                    contextWindow = new { mode = "max" }
                }
            });
            await _h.ExecuteRequestAsync(update);

            var sent = await _h.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
            Assert.Contains(sent, message => message.RootElement.TryGetProperty("result", out _));
            var notification = Assert.Single(sent, message =>
                message.RootElement.TryGetProperty("method", out var method)
                && method.GetString() == DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadUpdated);
            var thread = notification.RootElement.GetProperty("params").GetProperty("thread");
            Assert.Equal(threadId, thread.GetProperty("id").GetString());
            Assert.Equal("max", thread.GetProperty("configuration").GetProperty("contextWindow").GetProperty("mode").GetString());
        }
        finally
        {
            _h.Service.ThreadUpdatedForBroadcast = null;
        }
    }

    [Fact]
    public async Task ThreadStart_EmitsThreadStartedNotification()
    {
        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, new
        {
            identity = new { channelName = "appserver", userId = "test_user", workspacePath = _h.Identity.WorkspacePath }
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();  // response
        var notification = await _h.Transport.ReadNextSentAsync(); // notification

        CoreAppServerTestHarness.AssertIsNotification(notification, DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStarted);
        Assert.StartsWith("thread_", notification.RootElement
            .GetProperty("params").GetProperty("thread")
            .GetProperty("id").GetString()!);
    }

    [Fact]
    public async Task ThreadStart_OriginPresentationProvider_EnrichesResponseAndNotification()
    {
        using var harness = new CoreAppServerTestHarness(
            threadOriginPresentationProviders: [new TestOriginPresentationProvider()]);
        await harness.InitializeAsync();

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, new
        {
            identity = new
            {
                channelName = "teams",
                userId = "dotcraft-teams",
                channelContext = "mission_1:builder",
                workspacePath = harness.Identity.WorkspacePath
            }
        });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        using var notification = await harness.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(response);
        CoreAppServerTestHarness.AssertIsNotification(notification, DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStarted);

        AssertOriginPresentation(response.RootElement.GetProperty("result").GetProperty("thread"));
        AssertOriginPresentation(notification.RootElement.GetProperty("params").GetProperty("thread"));

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadList, new
        {
            identity = new
            {
                channelName = "teams",
                userId = "dotcraft-teams",
                channelContext = "mission_1:builder",
                workspacePath = harness.Identity.WorkspacePath
            }
        }));
        using var listResponse = await harness.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(listResponse);
        var listedThread = Assert.Single(
            listResponse.RootElement.GetProperty("result").GetProperty("data").EnumerateArray());
        AssertOriginPresentation(listedThread);
    }

    [Fact]
    public async Task ThreadStart_WithAgentBuilderTarget_ProjectsInternalMetadataAndListExcludesIt()
    {
        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, new
        {
            identity = new { channelName = "appserver", userId = "test_user", workspacePath = _h.Identity.WorkspacePath },
            config = new
            {
                agentBuilderTargetId = "draft-agent",
                agentBuilderTargetSource = "workspace"
            }
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        var notification = await _h.Transport.ReadNextSentAsync();

        CoreAppServerTestHarness.AssertIsSuccessResponse(response);
        var responseThread = response.RootElement.GetProperty("result").GetProperty("thread");
        Assert.Equal(
            ThreadVisibility.AgentBuilderInternalValue,
            responseThread.GetProperty("metadata").GetProperty(ThreadVisibility.InternalMetadataKey).GetString());

        CoreAppServerTestHarness.AssertIsNotification(notification, DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStarted);
        var notificationThread = notification.RootElement.GetProperty("params").GetProperty("thread");
        Assert.Equal(
            ThreadVisibility.AgentBuilderInternalValue,
            notificationThread.GetProperty("metadata").GetProperty(ThreadVisibility.InternalMetadataKey).GetString());

        var listMsg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadList, new
        {
            identity = new
            {
                channelName = _h.Identity.ChannelName,
                userId = _h.Identity.UserId,
                workspacePath = _h.Identity.WorkspacePath
            }
        });
        await _h.ExecuteRequestAsync(listMsg);

        var listResponse = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(listResponse);
        Assert.Equal(0, listResponse.RootElement.GetProperty("result").GetProperty("data").GetArrayLength());
    }

    [Fact]
    public async Task ThreadStart_WithAgentBuilderTarget_AllowsImmediateBuilderDraftUpdate()
    {
        var startMsg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, new
        {
            identity = new { channelName = "appserver", userId = "test_user", workspacePath = _h.Identity.WorkspacePath },
            config = new
            {
                agentBuilderTargetId = "draft-agent",
                agentBuilderTargetSource = "workspace"
            }
        });
        await _h.ExecuteRequestAsync(startMsg);

        var startResponse = await _h.Transport.ReadNextSentAsync();
        _ = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(startResponse);
        var threadId = startResponse.RootElement.GetProperty("result").GetProperty("thread").GetProperty("id").GetString()!;

        var draftMsg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.AgentProfileBuilderDraftUpdate, new
        {
            threadId,
            rawContent = "---\nname: draft-agent\n---\n\nDraft body.\n"
        });
        await _h.ExecuteRequestAsync(draftMsg);

        var draftResponse = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(draftResponse);
        var result = draftResponse.RootElement.GetProperty("result");
        Assert.Equal(threadId, result.GetProperty("threadId").GetString());
        Assert.Equal("draft-agent", result.GetProperty("targetId").GetString());
        Assert.Equal("workspace", result.GetProperty("targetSource").GetString());
    }

    [Fact]
    public async Task ThreadStart_ResponseBeforeNotification_Ordering()
    {
        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, new
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
        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.WorktreeCreateAndStart, new
        {
            identity = new { channelName = "appserver", userId = "test_user", workspacePath = _h.Identity.WorkspacePath },
            branchName = "dotcraft/wire-start",
            copyDirtyChanges = false
        });

        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(response);
        var result = response.RootElement.GetProperty("result");
        var thread = result.GetProperty("thread");
        var worktree = result.GetProperty("worktree");
        Assert.Equal(worktree.GetProperty("path").GetString(), thread.GetProperty("effectiveWorkspacePath").GetString());
        Assert.Equal("dotcraft/wire-start", worktree.GetProperty("branchName").GetString());

        var notification = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsNotification(notification, DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStarted);
        Assert.Equal(thread.GetProperty("id").GetString(), notification.RootElement.GetProperty("params").GetProperty("thread").GetProperty("id").GetString());
    }

    [Fact]
    public async Task ThreadWorktreeHandoff_ReturnsThreadAndEmitsUpdated()
    {
        InitializeGitWorkspace(_h.Identity.WorkspacePath);
        var start = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, new
        {
            identity = new { channelName = "appserver", userId = "test_user", workspacePath = _h.Identity.WorkspacePath }
        });
        await _h.ExecuteRequestAsync(start);
        var startResponse = await _h.Transport.ReadNextSentAsync();
        _ = await _h.Transport.ReadNextSentAsync();
        var threadId = startResponse.RootElement.GetProperty("result").GetProperty("thread").GetProperty("id").GetString()!;

        var handoff = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadWorktreeHandoff, new
        {
            threadId,
            mode = "worktree",
            branchName = "dotcraft/wire-handoff",
            copyDirtyChanges = false
        });
        await _h.ExecuteRequestAsync(handoff);

        var response = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(response);
        var result = response.RootElement.GetProperty("result");
        Assert.Equal("worktree", result.GetProperty("mode").GetString());
        var thread = result.GetProperty("thread");
        Assert.Equal(threadId, thread.GetProperty("id").GetString());
        Assert.Equal(result.GetProperty("worktree").GetProperty("path").GetString(), thread.GetProperty("effectiveWorkspacePath").GetString());

        var notification = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsNotification(notification, DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadUpdated);
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
        AppServerRequestContext.CurrentMethod = DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart;
        try
        {
            _h.Service.ThreadCreatedForBroadcast = thread =>
            {
                var skip = string.Equals(AppServerRequestContext.CurrentMethod, DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, StringComparison.Ordinal)
                    ? AppServerRequestContext.CurrentTransport
                    : null;
                if (skip != null && ReferenceEquals(_h.Transport, skip))
                    return;
                _h.Transport.WriteMessageAsync(new
                {
                    jsonrpc = "2.0",
                    method = DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStarted,
                    @params = new { thread = thread.ToWire() }
                }, default).GetAwaiter().GetResult();
            };

            var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, new
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
                var skip = string.Equals(AppServerRequestContext.CurrentMethod, DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, StringComparison.Ordinal)
                    ? AppServerRequestContext.CurrentTransport
                    : null;
                if (skip != null && ReferenceEquals(_h.Transport, skip))
                    return;
                _h.Transport.WriteMessageAsync(new
                {
                    jsonrpc = "2.0",
                    method = DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStarted,
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
            CoreAppServerTestHarness.AssertIsNotification(notification, DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStarted);
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
        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, new
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
        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, new
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
        using var harness = new CoreAppServerTestHarness();
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

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadFork, new
        {
            threadId = source.Id,
            displayName = "Branch"
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        var notification = await _h.Transport.ReadNextSentAsync();

        CoreAppServerTestHarness.AssertIsSuccessResponse(response);
        var result = response.RootElement.GetProperty("result");
        Assert.Empty(result.GetProperty("instructionSources").EnumerateArray());
        var thread = result.GetProperty("thread");
        var forkId = thread.GetProperty("id").GetString()!;
        Assert.NotEqual(source.Id, forkId);
        Assert.Equal(forkId, thread.GetProperty("sessionId").GetString());
        Assert.Equal(source.Id, thread.GetProperty("forkedFromId").GetString());
        Assert.Equal("Branch", thread.GetProperty("displayName").GetString());
        Assert.Equal(_h.Identity.WorkspacePath, thread.GetProperty("effectiveWorkspacePath").GetString());
        Assert.False(thread.GetProperty("ephemeral").GetBoolean());
        Assert.False(thread.TryGetProperty("path", out _));
        Assert.False(thread.TryGetProperty("turns", out _));

        CoreAppServerTestHarness.AssertIsNotification(notification, DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStarted);
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

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadFork, new
        {
            threadId = source.Id
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        _ = await _h.Transport.ReadNextSentAsync();

        CoreAppServerTestHarness.AssertIsSuccessResponse(response);
        var thread = response.RootElement.GetProperty("result").GetProperty("thread");
        Assert.Equal("Research worktree handoff", thread.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task ThreadFork_Ephemeral_OmitsPathAndTurns()
    {
        var source = await _h.Service.CreateThreadAsync(_h.Identity);
        AddCompletedTurn(source, "turn_001", "first");

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadFork, new
        {
            threadId = source.Id,
            ephemeral = true
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        _ = await _h.Transport.ReadNextSentAsync();

        CoreAppServerTestHarness.AssertIsSuccessResponse(response);
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

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentChildrenList, new
        {
            parentThreadId = parent.Id,
            includeThreads = true
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();

        CoreAppServerTestHarness.AssertIsSuccessResponse(response);
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

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentSendMessage, new
        {
            parentThreadId = parent.Id,
            target = "/root/worker",
            message = "please inspect tests"
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(response);
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
        using var harness = new CoreAppServerTestHarness(
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

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentFollowupTask, new
        {
            parentThreadId = parent.Id,
            target = "/root/worker",
            message = "continue work"
        });
        await harness.ExecuteRequestAsync(msg);

        var response = await harness.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(response);
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
        using var harness = new CoreAppServerTestHarness(
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

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentFollowupTask, new
        {
            parentThreadId = parent.Id,
            target = "/root/worker",
            message = "continue work"
        });
        await harness.ExecuteRequestAsync(msg);

        var response = await harness.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(response);
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

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentFollowupTask, new
        {
            parentThreadId = parent.Id,
            target = "/root/worker",
            message = "continue work",
            deliveryMode = "steer"
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(response);
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
        using var harness = new CoreAppServerTestHarness(
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

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentFollowupTask, new
        {
            parentThreadId = parent.Id,
            target = "/root/worker",
            message = "continue work",
            deliveryMode = "steer"
        });
        await harness.ExecuteRequestAsync(msg);

        var response = await harness.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.InvalidParamsCode);
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

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentClose, new
        {
            parentThreadId = parent.Id,
            target = "/root/worker"
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(response);
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

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.WorktreeCreateAndFork, new
        {
            sourceThreadId = source.Id,
            branchName = "dotcraft/appserver-worktree",
            displayName = "Worktree Branch"
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        var notification = await _h.Transport.ReadNextSentAsync();

        CoreAppServerTestHarness.AssertIsSuccessResponse(response);
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
        Assert.False(thread.TryGetProperty("turns", out _));

        CoreAppServerTestHarness.AssertIsNotification(notification, DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStarted);
        var notifiedThread = notification.RootElement.GetProperty("params").GetProperty("thread");
        Assert.Equal(forkId, notifiedThread.GetProperty("id").GetString());
        Assert.Equal(worktreePath, notifiedThread.GetProperty("effectiveWorkspacePath").GetString());
        Assert.False(notifiedThread.TryGetProperty("turns", out _));

        var listMsg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.WorktreeList, new
        {
            identity = new { channelName = "appserver", userId = "test_user", workspacePath = _h.Identity.WorkspacePath }
        });
        await _h.ExecuteRequestAsync(listMsg);
        var listResponse = await _h.Transport.ReadNextSentAsync();
        var listed = Assert.Single(listResponse.RootElement.GetProperty("result").GetProperty("data").EnumerateArray());
        Assert.Equal(forkId, listed.GetProperty("threadId").GetString());

        var statusMsg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.WorktreeStatus, new { threadId = forkId });
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

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadResume, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        var notification = await _h.Transport.ReadNextSentAsync();

        CoreAppServerTestHarness.AssertIsSuccessResponse(response);
        Assert.Empty(response.RootElement
            .GetProperty("result").GetProperty("instructionSources").EnumerateArray());
        Assert.Equal(thread.Id, response.RootElement
            .GetProperty("result").GetProperty("thread").GetProperty("id").GetString());

        CoreAppServerTestHarness.AssertIsNotification(notification, DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadResumed);
    }

    [Fact]
    public async Task ThreadResume_WithDynamicTools_BindsToolsAndRefreshesAgent()
    {
        var dynamicToolProxy = new WireDynamicToolProxy();
        using var harness = new CoreAppServerTestHarness(wireDynamicToolProxy: dynamicToolProxy);
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadResume, new
        {
            threadId = thread.Id,
            dynamicTools = new RuntimeDynamicToolDeclarationSpec[] { CreateReviewToolSpec() }
        });
        await harness.ExecuteRequestAsync(msg);

        var response = await harness.Transport.ReadNextSentAsync();
        var notification = await harness.Transport.ReadNextSentAsync();

        CoreAppServerTestHarness.AssertIsSuccessResponse(response);
        CoreAppServerTestHarness.AssertIsNotification(notification, DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadResumed);
        Assert.Contains(thread.Id, harness.Service.RefreshedThreadAgents);
        var registration = Assert.Single(await dynamicToolProxy.GetRegistrationsAsync(
            new ToolPlanningContext(
                thread.Id,
                null,
                thread.WorkspacePath,
                Path.Combine(thread.WorkspacePath, ".craft"),
                "default",
                null,
                [],
                1)));
        Assert.Equal("SubmitReviewDraft", registration.Definition.Name.Name);
    }

    [Fact]
    public async Task ThreadResume_WithoutRuntimeAdditionalContext_KeepsExistingContext()
    {
        var runtimeContextProvider = new WireRuntimeAdditionalContextProvider();
        using var harness = new CoreAppServerTestHarness(wireRuntimeAdditionalContextProvider: runtimeContextProvider);
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        runtimeContextProvider.BindThread(
            thread.Id,
            harness.Transport,
            harness.Connection,
            new Dictionary<string, RuntimeAdditionalContextValue>
            {
                ["desktop.threadCoordination"] = new()
                {
                    Kind = RuntimeAdditionalContextKinds.Application,
                    Value = "existing context"
                }
            });

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadResume, new { threadId = thread.Id });
        await harness.ExecuteRequestAsync(msg);

        var response = await harness.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(response);
        var section = runtimeContextProvider.GetSystemPromptSection(new ThreadSystemPromptContext(thread.Id, harness.Identity.WorkspacePath));
        Assert.Contains("existing context", section);
    }

    [Fact]
    public async Task ThreadResume_WithEmptyRuntimeAdditionalContext_ClearsExistingContext()
    {
        var runtimeContextProvider = new WireRuntimeAdditionalContextProvider();
        using var harness = new CoreAppServerTestHarness(wireRuntimeAdditionalContextProvider: runtimeContextProvider);
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        runtimeContextProvider.BindThread(
            thread.Id,
            harness.Transport,
            harness.Connection,
            new Dictionary<string, RuntimeAdditionalContextValue>
            {
                ["desktop.threadCoordination"] = new()
                {
                    Kind = RuntimeAdditionalContextKinds.Application,
                    Value = "old context"
                }
            });

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadResume, new
        {
            threadId = thread.Id,
            additionalContext = new Dictionary<string, RuntimeAdditionalContextValue>()
        });
        await harness.ExecuteRequestAsync(msg);

        var response = await harness.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(response);
        Assert.Contains(thread.Id, harness.Service.RefreshedThreadAgents);
        Assert.Null(runtimeContextProvider.GetSystemPromptSection(new ThreadSystemPromptContext(thread.Id, harness.Identity.WorkspacePath)));
    }

    [Fact]
    public async Task ThreadResume_WithInvalidDynamicTools_ReturnsInvalidParamsWithoutRebind()
    {
        var dynamicToolProxy = new WireDynamicToolProxy();
        using var harness = new CoreAppServerTestHarness(wireDynamicToolProxy: dynamicToolProxy);
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        var invalidSpec = CreateReviewToolSpec();
        invalidSpec.Description = "";

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadResume, new
        {
            threadId = thread.Id,
            dynamicTools = new RuntimeDynamicToolDeclarationSpec[] { invalidSpec }
        });
        await harness.ExecuteRequestAsync(msg);

        var response = await harness.Transport.ReadNextSentAsync();

        CoreAppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.InvalidParamsCode);
        Assert.Empty(harness.Service.RefreshedThreadAgents);
        Assert.Empty(await dynamicToolProxy.GetRegistrationsAsync(
            new ToolPlanningContext(
                thread.Id,
                null,
                thread.WorkspacePath,
                Path.Combine(thread.WorkspacePath, ".craft"),
                "default",
                null,
                [],
                1)));
    }

    // -------------------------------------------------------------------------
    // thread/pause (spec Section 4)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ThreadPause_EmitsStatusChangedNotification()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadPause, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        var notification = await _h.Transport.ReadNextSentAsync();

        CoreAppServerTestHarness.AssertIsSuccessResponse(response);
        CoreAppServerTestHarness.AssertIsNotification(notification, DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStatusChanged);
        Assert.Equal("paused",
            notification.RootElement.GetProperty("params").GetProperty("newStatus").GetString());
    }

    [Fact]
    public async Task ThreadPause_NotificationIncludesPreviousStatus()
    {
        // Gap B: previousStatus must be present in thread/statusChanged notification
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        Assert.Equal(ThreadStatus.Active, thread.Status);

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadPause, new { threadId = thread.Id });
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
        var subscribeMsg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadSubscribe, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(subscribeMsg);
        await _h.Transport.ReadNextSentAsync(); // drain subscribe response

        // Now pause — should produce exactly one message (the response), not two
        var pauseMsg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadPause, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(pauseMsg);

        var response = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(response);

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

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadPause, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(response);

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

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadArchive, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        var notification = await _h.Transport.ReadNextSentAsync();

        CoreAppServerTestHarness.AssertIsSuccessResponse(response);
        CoreAppServerTestHarness.AssertIsNotification(notification, DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStatusChanged);
        Assert.Equal("archived",
            notification.RootElement.GetProperty("params").GetProperty("newStatus").GetString());
    }

    [Fact]
    public async Task ThreadArchive_NotificationIncludesPreviousStatus()
    {
        // Gap B: previousStatus must be present in thread/statusChanged notification
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadArchive, new { threadId = thread.Id });
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

        var subscribeMsg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadSubscribe, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(subscribeMsg);
        await _h.Transport.ReadNextSentAsync(); // drain subscribe response

        var archiveMsg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadArchive, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(archiveMsg);

        var response = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(response);

        await Task.Delay(20);
        Assert.Null(_h.Transport.TryReadSent());
    }

    [Fact]
    public async Task ThreadUnarchive_EmitsStatusChangedNotification()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        await _h.Service.ArchiveThreadAsync(thread.Id);

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadUnarchive, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        var notification = await _h.Transport.ReadNextSentAsync();

        CoreAppServerTestHarness.AssertIsSuccessResponse(response);
        CoreAppServerTestHarness.AssertIsNotification(notification, DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStatusChanged);
        Assert.Equal("active",
            notification.RootElement.GetProperty("params").GetProperty("newStatus").GetString());
    }

    [Fact]
    public async Task ThreadUnarchive_NotificationIncludesPreviousStatus()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        await _h.Service.ArchiveThreadAsync(thread.Id);

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadUnarchive, new { threadId = thread.Id });
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

        var subscribeMsg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadSubscribe, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(subscribeMsg);
        await _h.Transport.ReadNextSentAsync(); // drain subscribe response

        var unarchiveMsg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadUnarchive, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(unarchiveMsg);

        var response = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(response);

        await Task.Delay(20);
        Assert.Null(_h.Transport.TryReadSent());
    }

    [Fact]
    public async Task ThreadUnarchive_AlreadyActive_SendsOnlyResponse_NoNotification()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadUnarchive, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(response);

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

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadResume, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(msg);

        await _h.Transport.ReadNextSentAsync(); // response
        var notification = await _h.Transport.ReadNextSentAsync();

        CoreAppServerTestHarness.AssertIsNotification(notification, DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadResumed);
        // The harness initializes with clientInfo.name = "test-client"
        Assert.Equal("test-client",
            notification.RootElement.GetProperty("params").GetProperty("resumedBy").GetString());
    }

    [Fact]
    public async Task ThreadResume_WhenSubscribed_SendsOnlyResponse_NoDuplicateNotification()
    {
        // Gap C: subscribed connection should not receive a duplicate thread/resumed notification
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        var subscribeMsg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadSubscribe, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(subscribeMsg);
        await _h.Transport.ReadNextSentAsync(); // drain subscribe response

        var resumeMsg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadResume, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(resumeMsg);

        var response = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(response);

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

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadList, new
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
        CoreAppServerTestHarness.AssertIsSuccessResponse(doc);
        var data = doc.RootElement.GetProperty("result").GetProperty("data");
        Assert.Equal(2, data.GetArrayLength());
    }

    [Fact]
    public async Task ThreadList_WithLimitAndCursor_ReturnsPages()
    {
        await _h.Service.CreateThreadAsync(_h.Identity, displayName: "First");
        await _h.Service.CreateThreadAsync(_h.Identity, displayName: "Second");
        await _h.Service.CreateThreadAsync(_h.Identity, displayName: "Third");

        var firstMsg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadList, new
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
        CoreAppServerTestHarness.AssertIsSuccessResponse(firstDoc);
        var firstResult = firstDoc.RootElement.GetProperty("result");
        var firstData = firstResult.GetProperty("data");
        Assert.Equal(2, firstData.GetArrayLength());
        Assert.Equal(3, firstResult.GetProperty("totalMatched").GetInt32());
        var cursor = firstResult.GetProperty("nextCursor").GetString();
        Assert.False(string.IsNullOrWhiteSpace(cursor));

        var secondMsg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadList, new
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
        CoreAppServerTestHarness.AssertIsSuccessResponse(secondDoc);
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

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadList, new
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
        CoreAppServerTestHarness.AssertIsSuccessResponse(doc);
        var result = doc.RootElement.GetProperty("result");
        var only = Assert.Single(result.GetProperty("data").EnumerateArray());
        Assert.Equal("Renderer Search", only.GetProperty("displayName").GetString());
        Assert.Equal(1, result.GetProperty("totalMatched").GetInt32());
    }

    [Fact]
    public async Task ThreadList_InvalidCursor_ReturnsInvalidParams()
    {
        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadList, new
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
        CoreAppServerTestHarness.AssertIsErrorResponse(doc, AppServerErrors.InvalidParamsCode);
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

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadList, new
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
        CoreAppServerTestHarness.AssertIsSuccessResponse(doc);
        var data = doc.RootElement.GetProperty("result").GetProperty("data");
        Assert.Equal(1, data.GetArrayLength());
        Assert.DoesNotContain(data.EnumerateArray(), item => item.GetProperty("id").GetString() == internalThread.Id);
    }

    [Fact]
    public async Task ThreadList_WorkspaceScope_IncludesAllOriginsAndStillExcludesInternalThreads()
    {
        var workspacePath = _h.Identity.WorkspacePath;
        var visibleIdentities = new[]
        {
            _h.Identity,
            new SessionIdentity { ChannelName = "oratorio", UserId = "operator", WorkspacePath = workspacePath, ChannelContext = "oratorio:bridge" },
            new SessionIdentity { ChannelName = "cron", UserId = "cron:job", WorkspacePath = workspacePath },
            new SessionIdentity { ChannelName = "heartbeat", UserId = "heartbeat:run", WorkspacePath = workspacePath },
            new SessionIdentity { ChannelName = "unknown-origin", UserId = "unknown:user", WorkspacePath = workspacePath }
        };
        foreach (var identity in visibleIdentities)
            await _h.Service.CreateThreadAsync(identity);
        await _h.Service.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "oratorio",
            UserId = "operator",
            WorkspacePath = workspacePath + "/child"
        });
        var internalThread = await _h.Service.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = WelcomeSuggestionConstants.ChannelName,
            UserId = WelcomeSuggestionConstants.InternalUserId,
            WorkspacePath = workspacePath
        });
        internalThread.Metadata[ThreadVisibility.InternalMetadataKey] = WelcomeSuggestionConstants.InternalMetadataValue;

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadList, new
        {
            identity = new
            {
                channelName = _h.Identity.ChannelName,
                userId = _h.Identity.UserId,
                workspacePath
            },
            scope = "workspace",
            crossChannelOrigins = new[] { "not-used" }
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(doc);
        var data = doc.RootElement.GetProperty("result").GetProperty("data");
        Assert.Equal(visibleIdentities.Length, data.GetArrayLength());
        Assert.Equal(
            visibleIdentities.Select(identity => identity.ChannelName).Order(),
            data.EnumerateArray().Select(item => item.GetProperty("originChannel").GetString()).Order());
    }

    [Fact]
    public async Task ThreadList_InvalidScope_ReturnsInvalidParams()
    {
        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadList, new
        {
            identity = new
            {
                channelName = _h.Identity.ChannelName,
                userId = _h.Identity.UserId,
                workspacePath = _h.Identity.WorkspacePath
            },
            scope = "global"
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsErrorResponse(doc, AppServerErrors.InvalidParamsCode);
    }

    [Fact]
    public async Task ThreadList_EmptyWorkspace_ReturnsEmpty()
    {
        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadList, new
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
        CoreAppServerTestHarness.AssertIsSuccessResponse(doc);
        Assert.Equal(0, doc.RootElement.GetProperty("result").GetProperty("data").GetArrayLength());
    }

    [Fact]
    public async Task ThreadList_HydratesActiveMaintenanceRuntime()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        _h.Service.RuntimeSnapshotHandler = t => ThreadSummaryRuntime.FromThread(t, "compacting");

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadList, new
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
        CoreAppServerTestHarness.AssertIsSuccessResponse(doc);
        var data = doc.RootElement.GetProperty("result").GetProperty("data");
        var returned = Assert.Single(data.EnumerateArray(), item => item.GetProperty("id").GetString() == thread.Id);
        var runtime = returned.GetProperty("runtime");
        Assert.True(runtime.GetProperty("busy").GetBoolean());
        Assert.Equal("compacting", runtime.GetProperty("maintenanceKind").GetString());
    }

    [Fact]
    public async Task ThreadList_HydratesCurrentTurnIdentityAndStartTime()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        var startedAt = DateTimeOffset.Parse("2026-08-24T00:00:00.000Z");
        thread.Turns.Add(new SessionTurn
        {
            Id = "turn_active",
            ThreadId = thread.Id,
            Status = TurnStatus.WaitingApproval,
            StartedAt = startedAt
        });
        await _h.Service.SeedThreadAsync(thread);

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadList, new
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
        CoreAppServerTestHarness.AssertIsSuccessResponse(doc);
        var returned = Assert.Single(
            doc.RootElement.GetProperty("result").GetProperty("data").EnumerateArray(),
            item => item.GetProperty("id").GetString() == thread.Id);
        var runtime = returned.GetProperty("runtime");
        Assert.Equal("turn_active", runtime.GetProperty("activeTurnId").GetString());
        Assert.Equal(startedAt, runtime.GetProperty("activeTurnStartedAt").GetDateTimeOffset());
    }

    // -------------------------------------------------------------------------
    // thread/read
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ThreadRead_ReturnsThreadById()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadRead, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(doc);
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

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadRead, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(doc);
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

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadRead, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(doc);
        var runtime = doc.RootElement
            .GetProperty("result")
            .GetProperty("thread")
            .GetProperty("runtime");
        Assert.True(runtime.GetProperty("busy").GetBoolean());
        Assert.Equal("consolidating", runtime.GetProperty("maintenanceKind").GetString());
    }

    [Fact]
    public async Task ThreadTurnsList_WithCursor_ReturnsRecentThenOlderPages()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        AddCompletedTurn(thread, "turn_001", "first");
        AddCompletedTurn(thread, "turn_002", "second");
        AddCompletedTurn(thread, "turn_003", "third");
        AddCompletedTurn(thread, "turn_004", "fourth");
        AddCompletedTurn(thread, "turn_005", "fifth");
        await _h.Service.SeedThreadAsync(thread);

        var firstMsg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadTurnsList, new
        {
            threadId = thread.Id,
            limit = 2,
            sortDirection = "descending"
        });
        await _h.ExecuteRequestAsync(firstMsg);

        var firstDoc = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(firstDoc);
        var firstResult = firstDoc.RootElement.GetProperty("result");
        var firstTurns = firstResult.GetProperty("data");
        Assert.Equal(["turn_005", "turn_004"], firstTurns.EnumerateArray().Select(t => t.GetProperty("id").GetString()!).ToArray());
        Assert.All(firstTurns.EnumerateArray(), turn => Assert.False(turn.TryGetProperty("items", out _)));
        var cursor = firstResult.GetProperty("nextCursor").GetString();

        var secondMsg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadTurnsList, new
        {
            threadId = thread.Id,
            limit = 2,
            cursor,
            sortDirection = "descending"
        });
        await _h.ExecuteRequestAsync(secondMsg);

        var secondDoc = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(secondDoc);
        var secondTurns = secondDoc.RootElement.GetProperty("result").GetProperty("data");
        Assert.Equal(["turn_003", "turn_002"], secondTurns.EnumerateArray().Select(t => t.GetProperty("id").GetString()!).ToArray());
    }

    [Fact]
    public async Task ThreadHistoryCursor_RejectsScopeAndDirectionMismatch()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        AddCompletedTurn(thread, "turn_001", "first");
        AddCompletedTurn(thread, "turn_002", "second");
        await _h.Service.SeedThreadAsync(thread);

        await _h.ExecuteRequestAsync(_h.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadTurnsList,
            new { threadId = thread.Id, limit = 1, sortDirection = "descending" }));
        var page = await _h.Transport.ReadNextSentAsync();
        var cursor = page.RootElement.GetProperty("result").GetProperty("nextCursor").GetString();

        await _h.ExecuteRequestAsync(_h.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadItemsList,
            new { threadId = thread.Id, cursor, sortDirection = "descending" }));
        var scopeMismatch = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsErrorResponse(scopeMismatch, AppServerErrors.InvalidParamsCode);

        await _h.ExecuteRequestAsync(_h.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadTurnsList,
            new { threadId = thread.Id, cursor, sortDirection = "ascending" }));
        var directionMismatch = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsErrorResponse(directionMismatch, AppServerErrors.InvalidParamsCode);
    }

    [Fact]
    public async Task ThreadItemsList_PagesOneLargeTurnWithoutDuplicates()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        AddCompletedTurn(thread, "turn_001", "first");
        var turn = Assert.Single(thread.Turns);
        turn.Items.AddRange(Enumerable.Range(2, 4).Select(index => new SessionItem
        {
            Id = $"item_{index:000}",
            TurnId = turn.Id,
            Type = ItemType.AgentMessage,
            Status = ItemStatus.Completed,
            CreatedAt = turn.StartedAt.AddMilliseconds(index),
            CompletedAt = turn.StartedAt.AddMilliseconds(index + 1),
            Payload = new AgentMessagePayload { Text = $"item {index}" }
        }));
        await _h.Service.SeedThreadAsync(thread);

        var ids = new List<string>();
        string? cursor = null;
        do
        {
            await _h.ExecuteRequestAsync(_h.BuildRequest(
                DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadItemsList,
                new { threadId = thread.Id, turnId = turn.Id, cursor, limit = 2, sortDirection = "ascending" }));
            var doc = await _h.Transport.ReadNextSentAsync();
            CoreAppServerTestHarness.AssertIsSuccessResponse(doc);
            var result = doc.RootElement.GetProperty("result");
            ids.AddRange(result.GetProperty("data").EnumerateArray()
                .Select(entry => entry.GetProperty("item").GetProperty("id").GetString()!));
            cursor = result.TryGetProperty("nextCursor", out var nextCursor)
                && nextCursor.ValueKind == JsonValueKind.String
                ? nextCursor.GetString()
                : null;
        } while (cursor != null);

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(turn.Items.Count, ids.Count);
    }

    [Fact]
    public async Task ThreadRead_HeaderStillReturnsQueuedInputs()
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
        await _h.Service.SeedThreadAsync(thread);

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadRead, new
        {
            threadId = thread.Id
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(doc);
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
        await _h.Service.SeedThreadAsync(thread);

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadRollback, new { threadId = thread.Id, numTurns = 1 });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(doc);
        var returned = doc.RootElement.GetProperty("result").GetProperty("thread");
        Assert.Equal(thread.Id, returned.GetProperty("id").GetString());
        Assert.False(returned.TryGetProperty("turns", out _));
    }

    [Fact]
    public async Task ThreadRollback_ThenHistoryPagesReturnRemainingTurns()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        AddCompletedTurn(thread, "turn_001", "first");
        AddCompletedTurn(thread, "turn_002", "second");
        await _h.Service.SeedThreadAsync(thread);

        var rollbackMsg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadRollback, new { threadId = thread.Id, numTurns = 1 });
        await _h.ExecuteRequestAsync(rollbackMsg);
        var rollbackDoc = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(rollbackDoc);

        var readMsg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadTurnsList, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(readMsg);
        var readDoc = await _h.Transport.ReadNextSentAsync();

        CoreAppServerTestHarness.AssertIsSuccessResponse(readDoc);
        Assert.False(rollbackDoc.RootElement.GetProperty("result").GetProperty("thread").TryGetProperty("turns", out _));
        var readTurns = readDoc.RootElement.GetProperty("result").GetProperty("data");
        Assert.Equal("turn_001", Assert.Single(readTurns.EnumerateArray()).GetProperty("id").GetString());
    }

    [Fact]
    public async Task ThreadRollback_WithZeroTurns_ReturnsInvalidParams()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadRollback, new { threadId = thread.Id, numTurns = 0 });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsErrorResponse(doc, AppServerErrors.InvalidParamsCode);
    }

    // -------------------------------------------------------------------------
    // thread/delete
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ThreadDelete_RemovesThread()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadDelete, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(doc);
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

    private static void AssertOriginPresentation(JsonElement thread)
    {
        var presentation = thread.GetProperty("originPresentation");
        Assert.Equal("agent-teams", presentation.GetProperty("sourceId").GetString());
        Assert.Equal("Builder", presentation.GetProperty("displayName").GetString());
        Assert.Equal("data:image/svg+xml;base64,dGVzdA==", presentation.GetProperty("icon").GetString());
        Assert.Equal("builder", presentation.GetProperty("subjectId").GetString());
        Assert.Equal("member", presentation.GetProperty("subjectKind").GetString());
    }

    private sealed class TestOriginPresentationProvider : IThreadOriginPresentationProvider
    {
        public ThreadOriginPresentationSnapshot? Resolve(ThreadOriginPresentationContext context)
        {
            if (!string.Equals(context.OriginChannel, "teams", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(context.ChannelContext, "mission_1:builder", StringComparison.Ordinal))
            {
                return null;
            }

            return new ThreadOriginPresentationSnapshot
            {
                SourceId = "agent-teams",
                DisplayName = "Builder",
                Icon = "data:image/svg+xml;base64,dGVzdA==",
                SubjectId = "builder",
                SubjectKind = "member"
            };
        }
    }

    private async Task<(SessionThread Parent, SessionThread Child)> CreatePathSubAgentAsync(
        CoreAppServerTestHarness? harness = null,
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

    private static RuntimeDynamicToolNamespaceSpec CreateReviewToolSpec()
        => new()
        {
            Name = "workflow",
            Description = "Workflow tools.",
            Tools =
            [
                new RuntimeDynamicToolFunctionSpec
                {
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
                }
            ]
        };

    private static IReadOnlySet<string> EmptyReservedNames()
        => new HashSet<string>(StringComparer.Ordinal);
}
