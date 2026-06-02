using System.Text.Json.Nodes;
using DotCraft.Abstractions;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Memory;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;

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
        _h.InitializeAsync().GetAwaiter().GetResult();
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
