using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerApprovalFlowTests
{
    [Theory]
    [InlineData(null, SessionApprovalDecision.AcceptOnce)]
    [InlineData("decline", SessionApprovalDecision.Reject)]
    [InlineData("acceptForSession", SessionApprovalDecision.AcceptForSession)]
    public async Task ApprovalFlow_SendsTheRequestToTheClientAndResolvesWithItsDecision(
        string? clientDecision,
        SessionApprovalDecision expected)
    {
        using var harness = new AppServerTestHarness(defaultApprovalDecision: SessionApprovalDecision.AcceptOnce);
        await harness.InitializeAsync(approvalSupport: true);
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadResume, new { threadId = thread.Id }));
        using var restored = await harness.Transport.ReadNextSentAsync();
        var events = AppServerTestHarness.BuildApprovalEventSequence(thread.Id, "turn_001", "req_123");
        foreach (var evt in events)
            if (evt.ItemPayload?.AsApprovalRequest is { } approval)
                evt.ItemPayload.Payload = approval with { CallId = "actual-shell-call" };
        harness.Service.EnqueueSubmitEvents(thread.Id, events);
        if (clientDecision != null)
            harness.Transport.ApprovalHandler = (_, _) => InMemoryTransport.BuildClientResponse(999, new { decision = clientDecision });

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Do something that needs approval" } }
        }));

        var all = await harness.Transport.WaitAndDrainAsync(5, TimeSpan.FromSeconds(10));
        var approvalRequest = Assert.Single(all, d =>
            d.RootElement.TryGetProperty("method", out var m) &&
            m.GetString() == DotCraft.Protocol.AppServer.AppServerMethodNames.ApprovalRequest);
        var @params = approvalRequest.RootElement.GetProperty("params");
        Assert.Equal("req_123", @params.GetProperty("requestId").GetString());
        Assert.Equal("actual-shell-call", @params.GetProperty("callId").GetString());
        Assert.Equal("shell", @params.GetProperty("approvalType").GetString());
        Assert.Equal(thread.Id, @params.GetProperty("threadId").GetString());
        Assert.Equal(expected, Assert.Single(harness.Service.ResolvedApprovals).decision);
    }

    [Fact]
    public async Task ApprovalFlow_ClientWithoutApprovalSupport_IsNotAskedAndGetsTheDefaultPolicy()
    {
        using var harness = new AppServerTestHarness(defaultApprovalDecision: SessionApprovalDecision.AcceptOnce);
        await harness.InitializeAsync(approvalSupport: false);
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadResume, new { threadId = thread.Id }));
        using var restored = await harness.Transport.ReadNextSentAsync();
        harness.Service.EnqueueSubmitEvents(
            thread.Id, AppServerTestHarness.BuildApprovalEventSequence(thread.Id));

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Needs approval" } }
        }));

        var all = await harness.Transport.WaitAndDrainAsync(4, TimeSpan.FromSeconds(10));
        Assert.DoesNotContain(all, d =>
            d.RootElement.TryGetProperty("method", out var m) &&
            m.GetString() == DotCraft.Protocol.AppServer.AppServerMethodNames.ApprovalRequest);
        Assert.Equal(SessionApprovalDecision.AcceptOnce, Assert.Single(harness.Service.ResolvedApprovals).decision);
    }

    [Fact]
    public async Task ApprovalFlow_QueuedTurnStartedByAClientWithoutApprovalSupport_UsesDefaultPolicy()
    {
        using var harness = new AppServerTestHarness(defaultApprovalDecision: SessionApprovalDecision.AcceptOnce);
        await harness.InitializeAsync(approvalSupport: false);

        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadResume, new { threadId = thread.Id }));
        using var restored = await harness.Transport.ReadNextSentAsync();
        harness.Service.EnqueueSubmitEvents(
            thread.Id, AppServerTestHarness.BuildApprovalEventSequence(thread.Id));

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.TurnQueueStart,
            new { threadId = thread.Id }));

        var resolved = Assert.Single(harness.Service.ResolvedApprovals);
        Assert.Equal(SessionApprovalDecision.AcceptOnce, resolved.decision);
    }

    [Fact]
    public async Task ApprovalFlow_DenyPolicyWithoutApprovalSupport_ResolvesAsDecline()
    {
        using var harness = new AppServerTestHarness(defaultApprovalDecision: SessionApprovalDecision.AcceptOnce);
        await harness.InitializeAsync(approvalSupport: false);

        var thread = await harness.Service.CreateThreadAsync(
            harness.Identity,
            new ThreadConfiguration { ApprovalPolicy = ApprovalPolicy.Deny });
        harness.Service.EnqueueSubmitEvents(
            thread.Id, AppServerTestHarness.BuildApprovalEventSequence(thread.Id));

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Needs approval" } }
        }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        Assert.False(harness.Connection.HasSubscription(thread.Id));

        Assert.Equal(SessionApprovalDecision.Reject, Assert.Single(harness.Service.ResolvedApprovals).decision);
    }
}
