using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

/// <summary>
/// Tests for the bidirectional approval flow (spec Section 7).
/// Verifies:
/// - Server sends item/approval/request to the client and awaits a response
/// - Client response is parsed into SessionApprovalDecision and resolved
/// - Timeout falls back to the default approval decision (Fix 6)
/// - approvalSupport=false applies the default policy without asking the client (Fix 6)
/// </summary>
public sealed class AppServerApprovalFlowTests : IDisposable
{
    private readonly AppServerTestHarness _h = new(
        defaultApprovalDecision: SessionApprovalDecision.AcceptOnce);

    public AppServerApprovalFlowTests()
    {
        _h.InitializeAsync(approvalSupport: true).GetAwaiter().GetResult();
    }

    public void Dispose() => _h.Dispose();

    // -------------------------------------------------------------------------
    // Happy path: client accepts the approval request
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ApprovalFlow_ClientAccepts_ResolveWithAcceptOnce()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        _h.Service.EnqueueSubmitEvents(
            thread.Id, AppServerTestHarness.BuildApprovalEventSequence(thread.Id));

        // Default auto-accept handler is set in InMemoryTransport
        var msg = _h.BuildRequest(AppServerMethods.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Do something that needs approval" } }
        });
        await _h.ExecuteRequestAsync(msg);

        // Wait for all messages to arrive: response, turn/started, approval request, approval/resolved, turn/completed
        var all = await _h.Transport.WaitAndDrainAsync(5, TimeSpan.FromSeconds(10));

        // Find the approval request (a server-initiated request with method = item/approval/request)
        var approvalRequest = all.FirstOrDefault(d =>
            d.RootElement.TryGetProperty("method", out var m) &&
            m.GetString() == AppServerMethods.ItemApprovalRequest);
        Assert.NotNull(approvalRequest);

        // Verify the approval was resolved with AcceptOnce
        Assert.Single(_h.Service.ResolvedApprovals);
        Assert.Equal(SessionApprovalDecision.AcceptOnce, _h.Service.ResolvedApprovals[0].decision);
    }

    [Fact]
    public async Task ApprovalFlow_ClientDeclines_ResolveWithReject()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        _h.Service.EnqueueSubmitEvents(
            thread.Id, AppServerTestHarness.BuildApprovalEventSequence(thread.Id));

        // Override: client declines
        _h.Transport.ApprovalHandler = (method, @params) =>
            InMemoryTransport.BuildClientResponse(999, new { decision = "decline" });

        var msg = _h.BuildRequest(AppServerMethods.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Dangerous operation" } }
        });
        await _h.ExecuteRequestAsync(msg);

        await _h.Transport.WaitAndDrainAsync(5, TimeSpan.FromSeconds(10));

        Assert.Single(_h.Service.ResolvedApprovals);
        Assert.Equal(SessionApprovalDecision.Reject, _h.Service.ResolvedApprovals[0].decision);
    }

    [Fact]
    public async Task ApprovalFlow_ClientAcceptsForSession_ResolveWithAcceptForSession()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        _h.Service.EnqueueSubmitEvents(
            thread.Id, AppServerTestHarness.BuildApprovalEventSequence(thread.Id));

        _h.Transport.ApprovalHandler = (method, @params) =>
            InMemoryTransport.BuildClientResponse(999, new { decision = "acceptForSession" });

        var msg = _h.BuildRequest(AppServerMethods.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Accept for the session" } }
        });
        await _h.ExecuteRequestAsync(msg);

        await _h.Transport.WaitAndDrainAsync(5, TimeSpan.FromSeconds(10));

        Assert.Single(_h.Service.ResolvedApprovals);
        Assert.Equal(SessionApprovalDecision.AcceptForSession, _h.Service.ResolvedApprovals[0].decision);
    }

    [Fact]
    public async Task ApprovalFlow_ClientAcceptsAlways_ResolveWithAcceptAlways()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        _h.Service.EnqueueSubmitEvents(
            thread.Id, AppServerTestHarness.BuildApprovalEventSequence(thread.Id));

        _h.Transport.ApprovalHandler = (method, @params) =>
            InMemoryTransport.BuildClientResponse(999, new { decision = "acceptAlways" });

        var msg = _h.BuildRequest(AppServerMethods.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Accept permanently" } }
        });
        await _h.ExecuteRequestAsync(msg);

        await _h.Transport.WaitAndDrainAsync(5, TimeSpan.FromSeconds(10));

        Assert.Single(_h.Service.ResolvedApprovals);
        Assert.Equal(SessionApprovalDecision.AcceptAlways, _h.Service.ResolvedApprovals[0].decision);
    }

    // -------------------------------------------------------------------------
    // Fix 6: approvalSupport=false uses workspace default policy (not hard-reject)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ApprovalFlow_ClientDoesNotSupportApproval_UsesDefaultPolicy()
    {
        // Create a harness with default = AcceptOnce, but approvalSupport = false
        using var harness = new AppServerTestHarness(
            defaultApprovalDecision: SessionApprovalDecision.AcceptOnce);
        await harness.InitializeAsync(approvalSupport: false);

        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        harness.Service.EnqueueSubmitEvents(
            thread.Id, AppServerTestHarness.BuildApprovalEventSequence(thread.Id));

        var msg = harness.BuildRequest(AppServerMethods.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Needs approval" } }
        });
        await harness.ExecuteRequestAsync(msg);

        await harness.Transport.WaitAndDrainAsync(4, TimeSpan.FromSeconds(10));

        // With approvalSupport=false, no approval request was sent to client,
        // and the decision was AcceptOnce (the workspace default, not Reject)
        Assert.Single(harness.Service.ResolvedApprovals);
        Assert.Equal(SessionApprovalDecision.AcceptOnce, harness.Service.ResolvedApprovals[0].decision);
    }

    [Fact]
    public async Task ApprovalFlow_ClientDoesNotSupportApproval_NoApprovalRequestSentToClient()
    {
        using var harness = new AppServerTestHarness(
            defaultApprovalDecision: SessionApprovalDecision.Reject);
        await harness.InitializeAsync(approvalSupport: false);

        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        harness.Service.EnqueueSubmitEvents(
            thread.Id, AppServerTestHarness.BuildApprovalEventSequence(thread.Id));

        var msg = harness.BuildRequest(AppServerMethods.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Needs approval" } }
        });
        await harness.ExecuteRequestAsync(msg);

        var all = await harness.Transport.WaitAndDrainAsync(4, TimeSpan.FromSeconds(10));

        // No item/approval/request should have been written to the transport
        var approvalRequests = all.Where(d =>
            d.RootElement.TryGetProperty("method", out var m) &&
            m.GetString() == AppServerMethods.ItemApprovalRequest).ToList();

        Assert.Empty(approvalRequests);
    }

    // -------------------------------------------------------------------------
    // Approval request params include all required fields
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ApprovalFlow_Request_IncludesRequiredParams()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        _h.Service.EnqueueSubmitEvents(
            thread.Id, AppServerTestHarness.BuildApprovalEventSequence(thread.Id, "turn_001", "req_123"));

        var msg = _h.BuildRequest(AppServerMethods.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Approval test" } }
        });
        await _h.ExecuteRequestAsync(msg);

        var all = await _h.Transport.WaitAndDrainAsync(5, TimeSpan.FromSeconds(10));

        var approvalRequest = all.First(d =>
            d.RootElement.TryGetProperty("method", out var m) &&
            m.GetString() == AppServerMethods.ItemApprovalRequest);

        var @params = approvalRequest.RootElement.GetProperty("params");
        Assert.Equal("req_123", @params.GetProperty("requestId").GetString());
        Assert.Equal("shell", @params.GetProperty("approvalType").GetString());
        Assert.Equal(thread.Id, @params.GetProperty("threadId").GetString());
    }
}
