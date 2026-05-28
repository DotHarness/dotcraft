using DotCraft.Protocol.AppServer;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

/// <summary>
/// Tests for notification opt-out (spec Section 10) and streaming suppression (Fix 4).
/// Verifies:
/// - optOutNotificationMethods suppresses specific notification methods
/// - streamingSupport=false suppresses all item/*/delta notifications
/// - Other notifications are not affected by the opt-out list
/// </summary>
public sealed class AppServerNotificationOptOutTests : IDisposable
{
    public void Dispose() { }

    // -------------------------------------------------------------------------
    // optOutNotificationMethods filtering
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OptOut_TurnCompleted_SuppressesTurnCompletedNotification()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync(optOutMethods: [AppServerMethods.TurnCompleted]);

        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        harness.Service.EnqueueSubmitEvents(
            thread.Id, AppServerTestHarness.BuildTurnEventSequence(thread.Id));

        var msg = harness.BuildRequest(AppServerMethods.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Hello" } }
        });
        await harness.ExecuteRequestAsync(msg);

        // With TurnCompleted opted out: response, turn/started, item/started, item/delta, item/completed
        // (no turn/completed)
        var all = await harness.Transport.WaitAndDrainAsync(5, TimeSpan.FromSeconds(10));

        var methods = all
            .Select(d => d.RootElement.TryGetProperty("method", out var m) ? m.GetString() : null)
            .ToList();

        Assert.DoesNotContain(AppServerMethods.TurnCompleted, methods);
        Assert.Contains(AppServerMethods.TurnStarted, methods);
        Assert.Contains(AppServerMethods.ItemAgentMessageDelta, methods);
    }

    [Fact]
    public async Task OptOut_ItemDelta_SuppressesDeltaNotifications()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync(optOutMethods:
        [
            AppServerMethods.ItemAgentMessageDelta,
            AppServerMethods.ItemReasoningDelta
        ]);

        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        harness.Service.EnqueueSubmitEvents(
            thread.Id, AppServerTestHarness.BuildTurnEventSequence(thread.Id));

        var msg = harness.BuildRequest(AppServerMethods.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Hello" } }
        });
        await harness.ExecuteRequestAsync(msg);

        // Without deltas: response, turn/started, item/started, item/completed, turn/completed (5)
        var all = await harness.Transport.WaitAndDrainAsync(5, TimeSpan.FromSeconds(10));

        var methods = all
            .Select(d => d.RootElement.TryGetProperty("method", out var m) ? m.GetString() : null)
            .ToList();

        Assert.DoesNotContain(AppServerMethods.ItemAgentMessageDelta, methods);
        Assert.DoesNotContain(AppServerMethods.ItemReasoningDelta, methods);
    }

    [Fact]
    public async Task OptOut_EmptyList_AllNotificationsDelivered()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync(optOutMethods: []);

        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        harness.Service.EnqueueSubmitEvents(
            thread.Id, AppServerTestHarness.BuildTurnEventSequence(thread.Id));

        var msg = harness.BuildRequest(AppServerMethods.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Hello" } }
        });
        await harness.ExecuteRequestAsync(msg);

        // All 6 messages: response + 5 notifications
        var all = await harness.Transport.WaitAndDrainAsync(6, TimeSpan.FromSeconds(10));
        Assert.Equal(6, all.Count);
    }

    // -------------------------------------------------------------------------
    // streamingSupport=false filtering (Fix 4)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StreamingDisabled_AgentMessageDeltas_AreSuppressed()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync(streamingSupport: false);

        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        harness.Service.EnqueueSubmitEvents(
            thread.Id, AppServerTestHarness.BuildTurnEventSequence(thread.Id));

        var msg = harness.BuildRequest(AppServerMethods.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Hello" } }
        });
        await harness.ExecuteRequestAsync(msg);

        var all = await harness.Transport.WaitAndDrainAsync(5, TimeSpan.FromSeconds(10));

        var methods = all
            .Select(d => d.RootElement.TryGetProperty("method", out var m) ? m.GetString() : null)
            .ToList();

        Assert.DoesNotContain(AppServerMethods.ItemAgentMessageDelta, methods);
        Assert.DoesNotContain(AppServerMethods.ItemReasoningDelta, methods);
    }

    [Fact]
    public async Task StreamingEnabled_AgentMessageDeltas_AreDelivered()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync(streamingSupport: true);

        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        harness.Service.EnqueueSubmitEvents(
            thread.Id, AppServerTestHarness.BuildTurnEventSequence(thread.Id));

        var msg = harness.BuildRequest(AppServerMethods.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Hello" } }
        });
        await harness.ExecuteRequestAsync(msg);

        var all = await harness.Transport.WaitAndDrainAsync(6, TimeSpan.FromSeconds(10));

        var methods = all
            .Select(d => d.RootElement.TryGetProperty("method", out var m) ? m.GetString() : null)
            .ToList();

        Assert.Contains(AppServerMethods.ItemAgentMessageDelta, methods);
    }

    // -------------------------------------------------------------------------
    // isClientReady gate: notifications are suppressed before initialized
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Notifications_BeforeInitializedNotif_AreSuppressed()
    {
        using var harness = new AppServerTestHarness();

        // Only send initialize, NOT initialized notification
        var initMsg = harness.BuildRequest(AppServerMethods.Initialize, new
        {
            clientInfo = new { name = "test-client", version = "0.0.1" }
        });
        var result = await harness.Handler.HandleRequestAsync(initMsg, default);
        if (result != null)
            await harness.Transport.WriteMessageAsync(
                AppServerRequestHandler.BuildResponse(initMsg.Id, result));
        harness.Transport.TryReadSent(); // drain init response

        // Attempt to send a thread/list before initialized — should get "not ready" error
        var listMsg = harness.BuildRequest(AppServerMethods.ThreadList, new
        {
            identity = new { channelName = "test", workspacePath = harness.Identity.WorkspacePath }
        });
        await harness.ExecuteRequestAsync(listMsg);

        var doc = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(doc, AppServerErrors.InvalidRequestCode);
    }

    // -------------------------------------------------------------------------
    // opt-out + streamingSupport=false are additive
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OptOut_AndStreamingDisabled_BothApply()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync(
            streamingSupport: false,
            optOutMethods: [AppServerMethods.ItemStarted]);

        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        harness.Service.EnqueueSubmitEvents(
            thread.Id, AppServerTestHarness.BuildTurnEventSequence(thread.Id));

        var msg = harness.BuildRequest(AppServerMethods.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Hello" } }
        });
        await harness.ExecuteRequestAsync(msg);

        // Remaining: response, turn/started, item/completed, turn/completed (no deltas, no item/started)
        var all = await harness.Transport.WaitAndDrainAsync(4, TimeSpan.FromSeconds(10));

        var methods = all
            .Select(d => d.RootElement.TryGetProperty("method", out var m) ? m.GetString() : null)
            .ToList();

        Assert.DoesNotContain(AppServerMethods.ItemAgentMessageDelta, methods);
        Assert.DoesNotContain(AppServerMethods.ItemStarted, methods);
        Assert.Contains(AppServerMethods.TurnCompleted, methods);
    }
}
