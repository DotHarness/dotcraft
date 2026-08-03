using DotCraft.Protocol.AppServer;
using DotCraft.AppServer;
using DotCraft.Sessions;
using Xunit;

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
        await harness.InitializeAsync(optOutMethods: [DotCraft.Protocol.AppServer.AppServerMethodNames.TurnCompleted]);

        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        harness.Service.EnqueueSubmitEvents(
            thread.Id, AppServerTestHarness.BuildTurnEventSequence(thread.Id));

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
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

        Assert.DoesNotContain(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnCompleted, methods);
        Assert.Contains(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStarted, methods);
        Assert.Contains(DotCraft.Protocol.AppServer.AppServerMethodNames.AgentMessageDelta, methods);
    }

    [Fact]
    public async Task OptOut_ItemDelta_SuppressesDeltaNotifications()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync(optOutMethods:
        [
            DotCraft.Protocol.AppServer.AppServerMethodNames.AgentMessageDelta,
            DotCraft.Protocol.AppServer.AppServerMethodNames.ReasoningDelta
        ]);

        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        harness.Service.EnqueueSubmitEvents(
            thread.Id, AppServerTestHarness.BuildTurnEventSequence(thread.Id));

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
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

        Assert.DoesNotContain(DotCraft.Protocol.AppServer.AppServerMethodNames.AgentMessageDelta, methods);
        Assert.DoesNotContain(DotCraft.Protocol.AppServer.AppServerMethodNames.ReasoningDelta, methods);
    }

    [Fact]
    public async Task OptOut_EmptyList_AllNotificationsDelivered()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync(optOutMethods: []);

        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        harness.Service.EnqueueSubmitEvents(
            thread.Id, AppServerTestHarness.BuildTurnEventSequence(thread.Id));

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
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

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Hello" } }
        });
        await harness.ExecuteRequestAsync(msg);

        var all = await harness.Transport.WaitAndDrainAsync(5, TimeSpan.FromSeconds(10));

        var methods = all
            .Select(d => d.RootElement.TryGetProperty("method", out var m) ? m.GetString() : null)
            .ToList();

        Assert.DoesNotContain(DotCraft.Protocol.AppServer.AppServerMethodNames.AgentMessageDelta, methods);
        Assert.DoesNotContain(DotCraft.Protocol.AppServer.AppServerMethodNames.ReasoningDelta, methods);
    }

    [Fact]
    public async Task StreamingEnabled_AgentMessageDeltas_AreDelivered()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync(streamingSupport: true);

        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        harness.Service.EnqueueSubmitEvents(
            thread.Id, AppServerTestHarness.BuildTurnEventSequence(thread.Id));

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Hello" } }
        });
        await harness.ExecuteRequestAsync(msg);

        var all = await harness.Transport.WaitAndDrainAsync(6, TimeSpan.FromSeconds(10));

        var methods = all
            .Select(d => d.RootElement.TryGetProperty("method", out var m) ? m.GetString() : null)
            .ToList();

        Assert.Contains(DotCraft.Protocol.AppServer.AppServerMethodNames.AgentMessageDelta, methods);
    }

    // -------------------------------------------------------------------------
    // isClientReady gate: notifications are suppressed before initialized
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Notifications_BeforeInitializedNotif_AreSuppressed()
    {
        using var harness = new AppServerTestHarness();

        // Only send initialize, NOT initialized notification
        var initMsg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.Initialize, new
        {
            clientInfo = new { name = "test-client", version = "0.0.1" }
        });
        var result = await harness.Handler.HandleRequestAsync(initMsg, default);
        if (result != null)
            await harness.Transport.WriteMessageAsync(
                AppServerRequestHandler.BuildResponse(initMsg.Id, result));
        harness.Transport.TryReadSent(); // drain init response

        // Attempt to send a thread/list before initialized — should get "not ready" error
        var listMsg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadList, new
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
            optOutMethods: [DotCraft.Protocol.AppServer.AppServerMethodNames.ItemStarted]);

        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        harness.Service.EnqueueSubmitEvents(
            thread.Id, AppServerTestHarness.BuildTurnEventSequence(thread.Id));

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
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

        Assert.DoesNotContain(DotCraft.Protocol.AppServer.AppServerMethodNames.AgentMessageDelta, methods);
        Assert.DoesNotContain(DotCraft.Protocol.AppServer.AppServerMethodNames.ItemStarted, methods);
        Assert.Contains(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnCompleted, methods);
    }
}
