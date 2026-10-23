using DotCraft.Security;
using DotCraft.Security.ShellCommands;
using DotCraft.Tests.Security.ShellCommands;
using DotCraft.Sessions;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using ApprovalRequestPayload = DotCraft.Sessions.ApprovalRequestPayload;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionApprovalServiceTests : IDisposable
{
    private const string TurnId = "turn_aprv_001";

    private readonly string _storeDir = Path.Combine(Path.GetTempPath(), $"dotcraft_test_{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_storeDir, recursive: true); }
        catch { }
    }

    private static (SessionApprovalService svc, SessionEventChannel channel, SessionTurn turn)
        MakeApprovalService(ApprovalStore? store = null)
    {
        var threadId = "thread_aprv_001";
        var channel = new SessionEventChannel(threadId, TurnId);
        var turn = new SessionTurn
        {
            Id = TurnId,
            ThreadId = threadId,
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        };
        var seq = 1;
        var svc = new SessionApprovalService(
            channel,
            turn,
            () => seq++,
            CancellationToken.None,
            () => { },
            store);
        return (svc, channel, turn);
    }

    private static (SessionApprovalService svc, SessionEventChannel channel, SessionTurn turn, List<SessionThreadRuntimeSignal> signals)
        MakeApprovalServiceWithSignals(CancellationToken turnCancellation = default)
    {
        var signals = new List<SessionThreadRuntimeSignal>();
        var threadId = "thread_aprv_001";
        var channel = new SessionEventChannel(threadId, TurnId);
        var turn = new SessionTurn
        {
            Id = TurnId,
            ThreadId = threadId,
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        };
        var seq = 1;
        var svc = new SessionApprovalService(
            channel,
            turn,
            () => seq++,
            turnCancellation,
            () => { },
            runtimeSignalForBroadcast: (_, signal, _) => signals.Add(signal));
        return (svc, channel, turn, signals);
    }

    [Theory]
    [InlineData(SessionApprovalDecision.AcceptOnce, true, false, false)]
    [InlineData(SessionApprovalDecision.AcceptForSession, true, true, false)]
    [InlineData(SessionApprovalDecision.AcceptAlways, true, true, true)]
    [InlineData(SessionApprovalDecision.Reject, false, false, false)]
    [InlineData(SessionApprovalDecision.CancelTurn, false, false, false)]
    public void Decision_ReportsApprovalSessionScopeAndPersistence(
        SessionApprovalDecision decision,
        bool approved,
        bool appliesToSession,
        bool persistent)
    {
        Assert.Equal(approved, decision.IsApproved());
        Assert.Equal(appliesToSession, decision.AppliesToSession());
        Assert.Equal(persistent, decision.IsPersistent());
    }

    [Theory]
    [InlineData("file", SessionApprovalDecision.AcceptOnce, true)]
    [InlineData("file", SessionApprovalDecision.Reject, false)]
    [InlineData("shell", SessionApprovalDecision.AcceptOnce, true)]
    [InlineData("remoteResource", SessionApprovalDecision.AcceptOnce, true)]
    [InlineData("remoteResource", SessionApprovalDecision.Reject, false)]
    public async Task Request_ReturnsTheResolvedDecisionForEachApprovalKind(
        string kind,
        SessionApprovalDecision decision,
        bool expected)
    {
        var (svc, channel, _) = MakeApprovalService();

        var requestTask = kind switch
        {
            "file" => svc.RequestFileApprovalAsync("write", "/tmp/test.txt"),
            "shell" => svc.RequestShellApprovalAsync(ShellApprovalRequests.For("rm -rf /tmp/data", "/tmp")),
            _ => svc.RequestResourceApprovalAsync("remoteResource", "create", "doc-123")
        };
        var payload = await GetApprovalRequestAsync(channel);

        Assert.NotNull(payload);
        Assert.Equal(kind, payload!.ApprovalType);
        if (kind == "remoteResource")
        {
            Assert.Equal("create", payload.Operation);
            Assert.Equal("doc-123", payload.Target);
        }

        Assert.True(svc.TryResolve(payload.RequestId, decision));
        Assert.Equal(expected, await requestTask);
    }

    [Fact]
    public async Task RequestAndResolve_TrackTurnStatusItemsAndRuntimeSignals()
    {
        var (svc, channel, turn, signals) = MakeApprovalServiceWithSignals();
        Assert.False(svc.HasPendingApproval);
        Assert.False(svc.TryResolve("nonexistent_req", SessionApprovalDecision.AcceptOnce));

        var requestTask = svc.RequestFileApprovalAsync("write", "/path");
        var requestId = await GetApprovalRequestIdAsync(channel);

        Assert.Equal(TurnStatus.WaitingApproval, turn.Status);
        Assert.True(svc.HasPendingApproval);
        Assert.Equal([SessionThreadRuntimeSignal.ApprovalRequested], signals);
        var request = Assert.Single(turn.Items, i => i.Type == ItemType.ApprovalRequest).AsApprovalRequest!;
        Assert.Equal(requestId, request.RequestId);
        Assert.Equal("file", request.ApprovalType);
        Assert.Equal("write", request.Operation);

        Assert.True(svc.TryResolve(requestId!, SessionApprovalDecision.AcceptOnce));
        Assert.True(await requestTask);

        Assert.Equal(TurnStatus.Running, turn.Status);
        Assert.False(svc.HasPendingApproval);
        Assert.Equal(
            [SessionThreadRuntimeSignal.ApprovalRequested, SessionThreadRuntimeSignal.ApprovalResolved],
            signals);
        var response = Assert.Single(turn.Items, i => i.Type == ItemType.ApprovalResponse).AsApprovalResponse!;
        Assert.True(response.Approved);
        Assert.Equal(requestId, response.RequestId);
        Assert.Equal(SessionApprovalDecision.AcceptOnce, response.Decision);
        Assert.False(svc.TryResolve(requestId!, SessionApprovalDecision.AcceptOnce));
    }

    [Fact]
    public async Task TurnCancellation_ResolvesPendingApprovalAsCancelled_AndIgnoresLateAnswer()
    {
        using var turnCancellation = new CancellationTokenSource();
        var (svc, channel, turn, signals) = MakeApprovalServiceWithSignals(turnCancellation.Token);

        var requestTask = svc.RequestFileApprovalAsync("write", "/file.txt");
        var requestId = await GetApprovalRequestIdAsync(channel);

        await turnCancellation.CancelAsync();

        Assert.False(await requestTask.WaitAsync(TimeSpan.FromSeconds(5)));
        var response = Assert.Single(turn.Items.Select(i => i.Payload).OfType<ApprovalResponsePayload>());
        Assert.Equal((requestId, SessionApprovalDecision.CancelTurn), (response.RequestId, response.Decision));
        Assert.Equal([SessionThreadRuntimeSignal.ApprovalRequested, SessionThreadRuntimeSignal.ApprovalResolved], signals);
        Assert.False(svc.TryResolve(requestId!, SessionApprovalDecision.AcceptOnce));
    }

    [Fact]
    public async Task Close_RejectsLaterRequestWithoutPrompting()
    {
        var (svc, _, turn) = MakeApprovalService();

        svc.Close();

        Assert.False(await svc.RequestFileApprovalAsync("write", "/file.txt"));
        Assert.Empty(turn.Items);
    }

    [Fact]
    public async Task TryResolve_WithParallelPendingApprovals_KeepsTurnWaitingUntilAllResolve()
    {
        var (svc, channel, turn) = MakeApprovalService();
        var firstRequest = svc.RequestFileApprovalAsync("write", "/path/a.txt");
        var secondRequest = svc.RequestFileApprovalAsync("write", "/path/b.txt");
        var thirdRequest = svc.RequestFileApprovalAsync("write", "/path/c.txt");

        var requestIds = await GetApprovalRequestIdsAsync(channel, 3);
        Assert.Equal(3, requestIds.Count);
        Assert.True(svc.HasPendingApproval);
        Assert.Equal(TurnStatus.WaitingApproval, turn.Status);

        Assert.True(svc.TryResolve(requestIds[0], SessionApprovalDecision.AcceptOnce));
        Assert.True(await firstRequest);
        Assert.True(svc.HasPendingApproval);
        Assert.Equal(TurnStatus.WaitingApproval, turn.Status);

        Assert.True(svc.TryResolve(requestIds[1], SessionApprovalDecision.Reject));
        Assert.False(await secondRequest);
        Assert.True(svc.HasPendingApproval);
        Assert.Equal(TurnStatus.WaitingApproval, turn.Status);

        Assert.True(svc.TryResolve(requestIds[2], SessionApprovalDecision.AcceptOnce));
        Assert.True(await thirdRequest);
        Assert.False(svc.HasPendingApproval);
        Assert.Equal(TurnStatus.Running, turn.Status);
    }

    [Fact]
    public async Task AcceptForSession_CachesMatchingApprovalScope()
    {
        var (svc, channel, _) = MakeApprovalService();
        var firstRequest = svc.RequestFileApprovalAsync("write", "/path/a.txt");

        var firstRequestId = await GetApprovalRequestIdAsync(channel);
        Assert.NotNull(firstRequestId);

        svc.TryResolve(firstRequestId!, SessionApprovalDecision.AcceptForSession);
        Assert.True(await firstRequest);

        Assert.True(await svc.RequestFileApprovalAsync("write", "/path/b.txt"));
    }

    [Fact]
    public async Task AcceptAlways_CachesForSessionAndPersistsFileAndShellApprovals()
    {
        var store = new ApprovalStore(_storeDir);
        var (svc, channel, _) = MakeApprovalService(store: store);
        var shell = ShellApprovalRequests.For("rm -rf /tmp/data", "/tmp");

        var fileRequest = svc.RequestFileApprovalAsync("write", "/path/important.txt");
        svc.TryResolve((await GetApprovalRequestIdAsync(channel))!, SessionApprovalDecision.AcceptAlways);
        Assert.True(await fileRequest);
        var shellRequest = svc.RequestShellApprovalAsync(shell);
        svc.TryResolve((await GetApprovalRequestIdAsync(channel))!, SessionApprovalDecision.AcceptAlways);
        Assert.True(await shellRequest);

        Assert.True(await svc.RequestFileApprovalAsync("write", "/path/other.txt"));
        Assert.True(store.IsFileOperationApproved("write", "/path/important.txt"));
        Assert.True(store.IsShellApproved(shell.ApprovalKey.Hash));
    }

    [Fact]
    public async Task AcceptAlways_GenericResource_PersistsExactSourceHashScope()
    {
        var store = new ApprovalStore(_storeDir);
        var (svc, channel, _) = MakeApprovalService(store: store);
        const string target = "/workspace/.craft/workflows/review.js#sha256:abc";
        var request = svc.RequestResourceApprovalAsync("workflow", "start", target);
        var requestId = await GetApprovalRequestIdAsync(channel);
        svc.TryResolve(requestId!, SessionApprovalDecision.AcceptAlways);
        Assert.True(await request);
        Assert.True(store.IsResourceOperationApproved("workflow", "start", target));
        Assert.False(store.IsResourceOperationApproved("workflow", "start", target.Replace("abc", "def")));

        var reloaded = new ApprovalStore(_storeDir);
        Assert.True(reloaded.IsResourceOperationApproved("workflow", "start", target));
    }

    [Fact]
    public async Task AcceptAlways_ClientOwnedResource_CoversThreadWithoutPersisting()
    {
        var store = new ApprovalStore(_storeDir);
        var (svc, channel, _) = MakeApprovalService(store: store);
        var approval = new ResourceApprovalRequest("computerUse", "use", @"C:\Apps\Notepad.exe")
        {
            TargetLabel = "Notepad",
            PersistAcceptAlways = false
        };

        var request = svc.RequestResourceApprovalAsync(approval);
        var payload = await GetApprovalRequestAsync(channel);

        Assert.NotNull(payload);
        Assert.Equal("Notepad", payload!.TargetLabel);
        svc.TryResolve(payload.RequestId, SessionApprovalDecision.AcceptAlways);
        Assert.True(await request);
        Assert.False(store.IsResourceOperationApproved("computerUse", "use", approval.Target));
        Assert.True(await svc.RequestResourceApprovalAsync(approval));

        var (nextTurn, nextChannel, _) = MakeApprovalService(store: store);
        var pending = nextTurn.RequestResourceApprovalAsync(approval);
        Assert.NotNull(await GetApprovalRequestIdAsync(nextChannel));
        Assert.False(pending.IsCompleted);
    }

    [Fact]
    public async Task AcceptForSession_IsSharedAcrossTurnsInSameThread()
    {
        var registry = new SessionApprovalScopeRegistry();
        var first = MakeForTurn("turn_one", registry);
        var request = first.Service.RequestResourceApprovalAsync("workflow", "start", "source#sha256:abc");
        var requestId = await GetApprovalRequestIdAsync(first.Channel);
        first.Service.TryResolve(requestId!, SessionApprovalDecision.AcceptForSession);
        Assert.True(await request);

        var second = MakeForTurn("turn_two", registry);
        Assert.True(await second.Service.RequestResourceApprovalAsync("workflow", "start", "source#sha256:abc"));
        Assert.Empty(second.Turn.Items);

        static (SessionApprovalService Service, SessionEventChannel Channel, SessionTurn Turn) MakeForTurn(
            string turnId,
            SessionApprovalScopeRegistry registry)
        {
            const string threadId = "thread_shared";
            var channel = new SessionEventChannel(threadId, turnId);
            var turn = new SessionTurn { Id = turnId, ThreadId = threadId, Status = TurnStatus.Running };
            var sequence = 1;
            return (new SessionApprovalService(channel, turn, () => sequence++, CancellationToken.None, () => { }, sessionScopes: registry), channel, turn);
        }
    }

    [Fact]
    public async Task PersistedApprovals_SkipThePromptOnlyForTheRecordedOperation()
    {
        var store = new ApprovalStore(_storeDir);
        store.RecordFileOperation("write", "/pre-approved.txt");
        store.RecordShellApproval(ShellApprovalRequests.For("npm install", "/workspace"));
        var (svc, channel, turn) = MakeApprovalService(store: store);

        Assert.True(await svc.RequestFileApprovalAsync("write", "/pre-approved.txt"));
        Assert.True(await svc.RequestShellApprovalAsync(ShellApprovalRequests.For("npm install", "/workspace")));
        Assert.DoesNotContain(turn.Items, i => i.Type == ItemType.ApprovalRequest);

        var requestTask = svc.RequestFileApprovalAsync("write", "/not-approved.txt");
        var requestId = await GetApprovalRequestIdAsync(channel);
        Assert.Equal(TurnStatus.WaitingApproval, turn.Status);
        svc.TryResolve(requestId!, SessionApprovalDecision.AcceptOnce);
        Assert.True(await requestTask);
    }

    [Fact]
    public async Task CancelTurn_Decision_InvokesCancellationCallback()
    {
        var threadId = "thread_aprv_001";
        var channel = new SessionEventChannel(threadId, TurnId);
        var turn = new SessionTurn
        {
            Id = TurnId,
            ThreadId = threadId,
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        };
        var seq = 1;
        var cancelled = false;
        var svc = new SessionApprovalService(
            channel,
            turn,
            () => seq++,
            CancellationToken.None,
            () => cancelled = true);

        var requestTask = svc.RequestShellApprovalAsync(ShellApprovalRequests.For("rm -rf /tmp/data", "/tmp"));
        var requestId = await GetApprovalRequestIdAsync(channel);

        svc.TryResolve(requestId!, SessionApprovalDecision.CancelTurn);

        Assert.False(await requestTask);
        Assert.True(cancelled);
    }

    [Fact]
    public async Task ScopedApprovalService_DelegatesToTheOverrideOnlyWhileItIsSet()
    {
        var calls = new List<string>();
        var inner = new CallbackApprovalService((_, _, path) => { calls.Add("inner:" + path); return Task.FromResult(true); });
        var overrideService = new CallbackApprovalService((_, _, path) => { calls.Add("override:" + path); return Task.FromResult(false); });
        var scoped = new SessionScopedApprovalService(inner);

        Assert.True(await scoped.RequestFileApprovalAsync("read", "/f1"));
        using (SessionScopedApprovalService.SetOverride(overrideService))
        {
            Assert.False(await scoped.RequestFileApprovalAsync("write", "/f2"));
        }
        Assert.True(await scoped.RequestFileApprovalAsync("write", "/f3"));

        Assert.Equal(["inner:/f1", "override:/f2", "inner:/f3"], calls);
    }

    private static async IAsyncEnumerable<SessionEvent> DrainWithTimeout(
        SessionEventChannel channel,
        int timeoutMs = 2000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        await foreach (var evt in channel.ReadAllAsync(cts.Token)
            .WithCancellation(cts.Token))
        {
            yield return evt;
        }
    }

    private static async Task<ApprovalRequestPayload?> GetApprovalRequestAsync(SessionEventChannel channel)
    {
        await foreach (var evt in DrainWithTimeout(channel))
        {
            if (evt.EventType == SessionEventType.ApprovalRequested)
                return evt.ItemPayload?.Payload as ApprovalRequestPayload;
        }

        return null;
    }

    private static async Task<string?> GetApprovalRequestIdAsync(SessionEventChannel channel) =>
        (await GetApprovalRequestAsync(channel))?.RequestId;

    private static async Task<List<string>> GetApprovalRequestIdsAsync(
        SessionEventChannel channel,
        int expectedCount)
    {
        var requestIds = new List<string>();
        await foreach (var evt in DrainWithTimeout(channel))
        {
            if (evt.EventType != SessionEventType.ApprovalRequested)
                continue;

            var requestId = (evt.ItemPayload?.Payload as ApprovalRequestPayload)?.RequestId;
            if (requestId != null)
                requestIds.Add(requestId);

            if (requestIds.Count == expectedCount)
                break;
        }

        return requestIds;
    }
}

internal sealed class CallbackApprovalService(
    Func<string, string, string, Task<bool>> callback) : IApprovalService
{
    public Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext? context = null) =>
        callback("file", operation, path);

    public Task<bool> RequestShellApprovalAsync(ShellApprovalRequest request, ApprovalContext? context = null) =>
        callback("shell", request.Command, request.WorkingDirectory);

    public Task<bool> RequestResourceApprovalAsync(string kind, string operation, string target, ApprovalContext? context = null) =>
        callback(kind, operation, target);
}
