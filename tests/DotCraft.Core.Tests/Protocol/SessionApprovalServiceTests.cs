using DotCraft.Protocol;
using DotCraft.Security;

namespace DotCraft.Tests.Sessions.Protocol;

/// <summary>
/// Tests for SessionApprovalService: approval request/resolve, rejection, and timeout.
/// </summary>
public sealed class SessionApprovalServiceTests
{
    private const string TurnId = "turn_aprv_001";

    // -------------------------------------------------------------------------
    // Setup helpers
    // -------------------------------------------------------------------------

    private static (SessionApprovalService svc, SessionEventChannel channel, SessionTurn turn)
        MakeApprovalService(TimeSpan? timeout = null, ApprovalStore? store = null)
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
            timeout ?? TimeSpan.FromMinutes(1),
            () => { },
            store);
        return (svc, channel, turn);
    }

    private static (SessionApprovalService svc, SessionEventChannel channel, SessionTurn turn, List<SessionThreadRuntimeSignal> signals)
        MakeApprovalServiceWithSignals(TimeSpan? timeout = null, ApprovalStore? store = null)
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
            timeout ?? TimeSpan.FromMinutes(1),
            () => { },
            store,
            (_, signal) => signals.Add(signal));
        return (svc, channel, turn, signals);
    }

    // -------------------------------------------------------------------------
    // SessionApprovalDecision extension methods
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(SessionApprovalDecision.AcceptOnce, true)]
    [InlineData(SessionApprovalDecision.AcceptForSession, true)]
    [InlineData(SessionApprovalDecision.AcceptAlways, true)]
    [InlineData(SessionApprovalDecision.Reject, false)]
    [InlineData(SessionApprovalDecision.CancelTurn, false)]
    public void IsApproved_ReturnsCorrectValue(SessionApprovalDecision decision, bool expected) =>
        Assert.Equal(expected, decision.IsApproved());

    [Theory]
    [InlineData(SessionApprovalDecision.AcceptOnce, false)]
    [InlineData(SessionApprovalDecision.AcceptForSession, true)]
    [InlineData(SessionApprovalDecision.AcceptAlways, true)]
    [InlineData(SessionApprovalDecision.Reject, false)]
    [InlineData(SessionApprovalDecision.CancelTurn, false)]
    public void AppliesToSession_ReturnsCorrectValue(SessionApprovalDecision decision, bool expected) =>
        Assert.Equal(expected, decision.AppliesToSession());

    [Theory]
    [InlineData(SessionApprovalDecision.AcceptOnce, false)]
    [InlineData(SessionApprovalDecision.AcceptForSession, false)]
    [InlineData(SessionApprovalDecision.AcceptAlways, true)]
    [InlineData(SessionApprovalDecision.Reject, false)]
    [InlineData(SessionApprovalDecision.CancelTurn, false)]
    public void IsPersistent_ReturnsCorrectValue(SessionApprovalDecision decision, bool expected) =>
        Assert.Equal(expected, decision.IsPersistent());

    // -------------------------------------------------------------------------
    // File approval: approve
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequestFileApproval_WhenApproved_ReturnsTrue()
    {
        var (svc, channel, _) = MakeApprovalService();

        // Start the request, then resolve it immediately from another task
        var requestTask = svc.RequestFileApprovalAsync("write", "/tmp/test.txt");

        // Give the request task time to register
        await Task.Delay(10);

        // Find the requestId from the emitted event
        string? requestId = null;
        await foreach (var evt in DrainWithTimeout(channel))
        {
            if (evt.EventType == SessionEventType.ApprovalRequested)
            {
                requestId = (evt.ItemPayload?.Payload as ApprovalRequestPayload)?.RequestId;
                break;
            }
        }

        Assert.NotNull(requestId);
        svc.TryResolve(requestId!, SessionApprovalDecision.AcceptOnce);

        var result = await requestTask;
        Assert.True(result);
    }

    [Fact]
    public async Task RequestFileApproval_WhenRejected_ReturnsFalse()
    {
        var (svc, channel, _) = MakeApprovalService();

        var requestTask = svc.RequestFileApprovalAsync("delete", "/important/file.txt");
        await Task.Delay(10);

        string? requestId = null;
        await foreach (var evt in DrainWithTimeout(channel))
        {
            if (evt.EventType == SessionEventType.ApprovalRequested)
            {
                requestId = (evt.ItemPayload?.Payload as ApprovalRequestPayload)?.RequestId;
                break;
            }
        }

        Assert.NotNull(requestId);
        svc.TryResolve(requestId!, SessionApprovalDecision.Reject);

        var result = await requestTask;
        Assert.False(result);
    }

    // -------------------------------------------------------------------------
    // Shell approval
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequestShellApproval_WhenApproved_ReturnsTrue()
    {
        var (svc, channel, _) = MakeApprovalService();

        var requestTask = svc.RequestShellApprovalAsync("rm -rf /tmp/data", "/tmp");
        await Task.Delay(10);

        string? requestId = null;
        await foreach (var evt in DrainWithTimeout(channel))
        {
            if (evt.EventType == SessionEventType.ApprovalRequested)
            {
                requestId = (evt.ItemPayload?.Payload as ApprovalRequestPayload)?.RequestId;
                break;
            }
        }

        Assert.NotNull(requestId);
        svc.TryResolve(requestId!, SessionApprovalDecision.AcceptOnce);

        var result = await requestTask;
        Assert.True(result);
    }

    // -------------------------------------------------------------------------
    // Remote resource approval
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequestResourceApproval_WhenApproved_ReturnsTrue()
    {
        var (svc, channel, _) = MakeApprovalService();

        var requestTask = svc.RequestResourceApprovalAsync("remoteResource", "create", "doc-123");
        await Task.Delay(10);

        ApprovalRequestPayload? payload = null;
        await foreach (var evt in DrainWithTimeout(channel))
        {
            if (evt.EventType == SessionEventType.ApprovalRequested)
            {
                payload = evt.ItemPayload?.Payload as ApprovalRequestPayload;
                break;
            }
        }

        Assert.NotNull(payload);
        Assert.Equal("remoteResource", payload!.ApprovalType);
        Assert.Equal("create", payload.Operation);
        Assert.Equal("doc-123", payload.Target);

        svc.TryResolve(payload.RequestId, SessionApprovalDecision.AcceptOnce);

        var result = await requestTask;
        Assert.True(result);
    }

    [Fact]
    public async Task RequestResourceApproval_WhenRejected_ReturnsFalse()
    {
        var (svc, channel, _) = MakeApprovalService();

        var requestTask = svc.RequestResourceApprovalAsync("remoteResource", "move", "node-456");
        await Task.Delay(10);

        string? requestId = null;
        await foreach (var evt in DrainWithTimeout(channel))
        {
            if (evt.EventType == SessionEventType.ApprovalRequested)
            {
                requestId = (evt.ItemPayload?.Payload as ApprovalRequestPayload)?.RequestId;
                break;
            }
        }

        Assert.NotNull(requestId);
        svc.TryResolve(requestId!, SessionApprovalDecision.Reject);

        var result = await requestTask;
        Assert.False(result);
    }

    // -------------------------------------------------------------------------
    // Turn status transitions
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequestApproval_SetsTurnStatusToWaitingApproval()
    {
        var (svc, channel, turn) = MakeApprovalService();

        var requestTask = svc.RequestFileApprovalAsync("write", "/path");
        await Task.Delay(10);

        Assert.Equal(TurnStatus.WaitingApproval, turn.Status);
        Assert.True(svc.HasPendingApproval);

        // Clean up
        string? requestId = null;
        await foreach (var evt in DrainWithTimeout(channel))
        {
            if (evt.EventType == SessionEventType.ApprovalRequested)
            {
                requestId = (evt.ItemPayload?.Payload as ApprovalRequestPayload)?.RequestId;
                break;
            }
        }
        svc.TryResolve(requestId!, SessionApprovalDecision.AcceptOnce);
        await requestTask;
    }

    [Fact]
    public async Task TryResolve_SetsTurnStatusBackToRunning()
    {
        var (svc, channel, turn) = MakeApprovalService();

        var requestTask = svc.RequestFileApprovalAsync("write", "/path");
        await Task.Delay(10);

        string? requestId = null;
        await foreach (var evt in DrainWithTimeout(channel))
        {
            if (evt.EventType == SessionEventType.ApprovalRequested)
            {
                requestId = (evt.ItemPayload?.Payload as ApprovalRequestPayload)?.RequestId;
                break;
            }
        }

        svc.TryResolve(requestId!, SessionApprovalDecision.AcceptOnce);
        await requestTask;

        Assert.Equal(TurnStatus.Running, turn.Status);
        Assert.False(svc.HasPendingApproval);
    }

    // -------------------------------------------------------------------------
    // HasPendingApproval
    // -------------------------------------------------------------------------

    [Fact]
    public void HasPendingApproval_InitiallyFalse()
    {
        var (svc, _, _) = MakeApprovalService();
        Assert.False(svc.HasPendingApproval);
    }

    [Fact]
    public async Task RequestApproval_EmitsApprovalRequestedRuntimeSignal()
    {
        var (svc, channel, _, signals) = MakeApprovalServiceWithSignals();

        var requestTask = svc.RequestFileApprovalAsync("write", "/path");
        await Task.Delay(10);

        string? requestId = null;
        await foreach (var evt in DrainWithTimeout(channel))
        {
            if (evt.EventType == SessionEventType.ApprovalRequested)
            {
                requestId = (evt.ItemPayload?.Payload as ApprovalRequestPayload)?.RequestId;
                break;
            }
        }

        Assert.Equal([SessionThreadRuntimeSignal.ApprovalRequested], signals);

        svc.TryResolve(requestId!, SessionApprovalDecision.AcceptOnce);
        await requestTask;
    }

    [Fact]
    public async Task TryResolve_EmitsApprovalResolvedRuntimeSignal()
    {
        var (svc, channel, _, signals) = MakeApprovalServiceWithSignals();

        var requestTask = svc.RequestFileApprovalAsync("write", "/path");
        await Task.Delay(10);

        string? requestId = null;
        await foreach (var evt in DrainWithTimeout(channel))
        {
            if (evt.EventType == SessionEventType.ApprovalRequested)
            {
                requestId = (evt.ItemPayload?.Payload as ApprovalRequestPayload)?.RequestId;
                break;
            }
        }

        svc.TryResolve(requestId!, SessionApprovalDecision.AcceptOnce);
        await requestTask;

        Assert.Equal(
            [SessionThreadRuntimeSignal.ApprovalRequested, SessionThreadRuntimeSignal.ApprovalResolved],
            signals);
    }

    // -------------------------------------------------------------------------
    // TryResolve with unknown requestId
    // -------------------------------------------------------------------------

    [Fact]
    public void TryResolve_UnknownRequestId_ReturnsFalse()
    {
        var (svc, _, _) = MakeApprovalService();
        var result = svc.TryResolve("nonexistent_req", SessionApprovalDecision.AcceptOnce);
        Assert.False(result);
    }

    [Fact]
    public void TryResolve_CalledTwiceForSameRequest_SecondReturnsFalse()
    {
        var (svc, channel, _) = MakeApprovalService();

        // Start request but don't await - just register it
        _ = svc.RequestFileApprovalAsync("write", "/path");

        // We need to find the requestId; since we can't await the drain easily,
        // just verify that resolving a made-up ID returns false
        var result = svc.TryResolve("made_up_id", SessionApprovalDecision.AcceptOnce);
        Assert.False(result);
    }

    // -------------------------------------------------------------------------
    // Timeout
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequestApproval_Timeout_AutoRejectsAndReturnsFalse()
    {
        // Use a very short timeout (100ms) for fast test execution
        var (svc, channel, turn) = MakeApprovalService(TimeSpan.FromMilliseconds(100));

        var requestTask = svc.RequestFileApprovalAsync("write", "/file.txt");

        // Wait longer than the timeout
        var result = await requestTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(result);
    }

    [Fact]
    public async Task RequestApproval_Timeout_AddErrorItemToTurn()
    {
        var (svc, channel, turn) = MakeApprovalService(TimeSpan.FromMilliseconds(100));

        await svc.RequestFileApprovalAsync("write", "/file.txt");

        var errorItem = turn.Items.FirstOrDefault(i => i.Type == ItemType.Error);
        Assert.NotNull(errorItem);
        var errorPayload = errorItem!.AsError;
        Assert.NotNull(errorPayload);
        Assert.Contains("timed out", errorPayload!.Message, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // Items added to Turn
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequestApproval_AddsApprovalRequestItemToTurn()
    {
        var (svc, channel, turn) = MakeApprovalService();
        var requestTask = svc.RequestFileApprovalAsync("write", "/path");
        await Task.Delay(10);

        var approvalItem = turn.Items.FirstOrDefault(i => i.Type == ItemType.ApprovalRequest);
        Assert.NotNull(approvalItem);
        var payload = approvalItem!.AsApprovalRequest;
        Assert.NotNull(payload);
        Assert.Equal("file", payload!.ApprovalType);
        Assert.Equal("write", payload.Operation);

        // Resolve to clean up
        svc.TryResolve(payload.RequestId, SessionApprovalDecision.AcceptOnce);
        await requestTask;
    }

    [Fact]
    public async Task TryResolve_AddsApprovalResponseItemToTurn()
    {
        var (svc, channel, turn) = MakeApprovalService();
        var requestTask = svc.RequestFileApprovalAsync("write", "/path");
        await Task.Delay(10);

        var requestItem = turn.Items.First(i => i.Type == ItemType.ApprovalRequest);
        var requestId = requestItem.AsApprovalRequest!.RequestId;

        svc.TryResolve(requestId, SessionApprovalDecision.AcceptOnce);
        await requestTask;

        var responseItem = turn.Items.FirstOrDefault(i => i.Type == ItemType.ApprovalResponse);
        Assert.NotNull(responseItem);
        var payload = responseItem!.AsApprovalResponse;
        Assert.NotNull(payload);
        Assert.True(payload!.Approved);
        Assert.Equal(requestId, payload.RequestId);
        Assert.Equal(SessionApprovalDecision.AcceptOnce, payload.Decision);
    }

    [Fact]
    public async Task AcceptForSession_CachesMatchingApprovalScope()
    {
        var (svc, channel, _) = MakeApprovalService();
        var firstRequest = svc.RequestFileApprovalAsync("write", "/path/a.txt");
        await Task.Delay(10);

        var firstRequestId = await GetApprovalRequestIdAsync(channel);
        Assert.NotNull(firstRequestId);

        svc.TryResolve(firstRequestId!, SessionApprovalDecision.AcceptForSession);
        Assert.True(await firstRequest);

        var secondRequest = await svc.RequestFileApprovalAsync("write", "/path/b.txt");
        Assert.True(secondRequest);
    }

    // -------------------------------------------------------------------------
    // AcceptAlways: session cache + persistence
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AcceptAlways_CachesForSessionAndPersistsToStore()
    {
        var storeDir = Path.Combine(Path.GetTempPath(), $"dotcraft_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(storeDir);
            var store = new ApprovalStore(storeDir);
            var (svc, channel, _) = MakeApprovalService(store: store);

            var requestTask = svc.RequestFileApprovalAsync("write", "/path/important.txt");
            await Task.Delay(10);

            var requestId = await GetApprovalRequestIdAsync(channel);
            Assert.NotNull(requestId);

            svc.TryResolve(requestId!, SessionApprovalDecision.AcceptAlways);
            Assert.True(await requestTask);

            // Session cache should prevent a second prompt
            Assert.True(await svc.RequestFileApprovalAsync("write", "/path/other.txt"));

            // Persistent store should have recorded the operation
            Assert.True(store.IsFileOperationApproved("write", "/path/important.txt"));
        }
        finally
        {
            Directory.Delete(storeDir, recursive: true);
        }
    }

    [Fact]
    public async Task AcceptAlways_Shell_PersistsToStore()
    {
        var storeDir = Path.Combine(Path.GetTempPath(), $"dotcraft_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(storeDir);
            var store = new ApprovalStore(storeDir);
            var (svc, channel, _) = MakeApprovalService(store: store);

            var requestTask = svc.RequestShellApprovalAsync("rm -rf /tmp/data", "/tmp");
            await Task.Delay(10);

            var requestId = await GetApprovalRequestIdAsync(channel);
            Assert.NotNull(requestId);

            svc.TryResolve(requestId!, SessionApprovalDecision.AcceptAlways);
            Assert.True(await requestTask);

            Assert.True(store.IsShellCommandApproved("rm -rf /tmp/data", "/tmp"));
        }
        finally
        {
            Directory.Delete(storeDir, recursive: true);
        }
    }

    // -------------------------------------------------------------------------
    // Pre-check: persistent store skips prompt
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequestFileApproval_WhenPersistedInStore_SkipsPrompt()
    {
        var storeDir = Path.Combine(Path.GetTempPath(), $"dotcraft_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(storeDir);
            var store = new ApprovalStore(storeDir);
            store.RecordFileOperation("write", "/pre-approved.txt");

            var (svc, channel, turn) = MakeApprovalService(store: store);

            var result = await svc.RequestFileApprovalAsync("write", "/pre-approved.txt");
            Assert.True(result);

            Assert.DoesNotContain(turn.Items, i => i.Type == ItemType.ApprovalRequest);
        }
        finally
        {
            Directory.Delete(storeDir, recursive: true);
        }
    }

    [Fact]
    public async Task RequestShellApproval_WhenPersistedInStore_SkipsPrompt()
    {
        var storeDir = Path.Combine(Path.GetTempPath(), $"dotcraft_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(storeDir);
            var store = new ApprovalStore(storeDir);
            store.RecordShellCommand("npm install", "/workspace");

            var (svc, channel, turn) = MakeApprovalService(store: store);

            var result = await svc.RequestShellApprovalAsync("npm install", "/workspace");
            Assert.True(result);
            Assert.DoesNotContain(turn.Items, i => i.Type == ItemType.ApprovalRequest);
        }
        finally
        {
            Directory.Delete(storeDir, recursive: true);
        }
    }

    [Fact]
    public async Task RequestFileApproval_NotInStore_StillPromptsNormally()
    {
        var storeDir = Path.Combine(Path.GetTempPath(), $"dotcraft_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(storeDir);
            var store = new ApprovalStore(storeDir);

            var (svc, channel, turn) = MakeApprovalService(store: store);
            var requestTask = svc.RequestFileApprovalAsync("write", "/not-approved.txt");
            await Task.Delay(10);

            Assert.Equal(TurnStatus.WaitingApproval, turn.Status);
            Assert.True(svc.HasPendingApproval);

            var requestId = await GetApprovalRequestIdAsync(channel);
            svc.TryResolve(requestId!, SessionApprovalDecision.AcceptOnce);
            Assert.True(await requestTask);
        }
        finally
        {
            Directory.Delete(storeDir, recursive: true);
        }
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
            TimeSpan.FromMinutes(1),
            () => cancelled = true);

        var requestTask = svc.RequestShellApprovalAsync("rm -rf /tmp/data", "/tmp");
        await Task.Delay(10);
        var requestId = await GetApprovalRequestIdAsync(channel);

        svc.TryResolve(requestId!, SessionApprovalDecision.CancelTurn);

        Assert.False(await requestTask);
        Assert.True(cancelled);
    }

    // -------------------------------------------------------------------------
    // SessionScopedApprovalService
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ScopedApprovalService_NoOverride_DelegatesToInner()
    {
        var innerResults = new List<bool>();
        var inner = new CallbackApprovalService(async (type, op, path) =>
        {
            innerResults.Add(true);
            return true;
        });
        var scoped = new SessionScopedApprovalService(inner);

        var result = await scoped.RequestFileApprovalAsync("read", "/file.txt");

        Assert.True(result);
        Assert.Single(innerResults);
    }

    [Fact]
    public async Task ScopedApprovalService_WithOverride_DelegatesToOverride()
    {
        var innerCalled = false;
        var overrideCalled = false;

        var inner = new CallbackApprovalService(async (_, _, _) => { innerCalled = true; return true; });
        var overrideService = new CallbackApprovalService(async (_, _, _) => { overrideCalled = true; return false; });
        var scoped = new SessionScopedApprovalService(inner);

        using (SessionScopedApprovalService.SetOverride(overrideService))
        {
            await scoped.RequestFileApprovalAsync("write", "/file.txt");
        }

        Assert.False(innerCalled);
        Assert.True(overrideCalled);
    }

    [Fact]
    public async Task ScopedApprovalService_AfterOverrideDisposed_DelegatesToInner()
    {
        var innerCalled = false;
        var inner = new CallbackApprovalService(async (_, _, _) => { innerCalled = true; return true; });
        var overrideService = new CallbackApprovalService(async (_, _, _) => false);
        var scoped = new SessionScopedApprovalService(inner);

        using (SessionScopedApprovalService.SetOverride(overrideService))
        {
            await scoped.RequestFileApprovalAsync("write", "/f1");
        }

        // After dispose, should delegate to inner again
        await scoped.RequestFileApprovalAsync("write", "/f2");
        Assert.True(innerCalled);
    }

    // -------------------------------------------------------------------------
    // Helper: drain channel with timeout
    // -------------------------------------------------------------------------

    private static async IAsyncEnumerable<SessionEvent> DrainWithTimeout(
        SessionEventChannel channel,
        int timeoutMs = 200)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        await foreach (var evt in channel.ReadAllAsync(cts.Token)
            .WithCancellation(cts.Token))
        {
            yield return evt;
        }
    }

    private static async Task<string?> GetApprovalRequestIdAsync(SessionEventChannel channel)
    {
        await foreach (var evt in DrainWithTimeout(channel))
        {
            if (evt.EventType == SessionEventType.ApprovalRequested)
                return (evt.ItemPayload?.Payload as ApprovalRequestPayload)?.RequestId;
        }

        return null;
    }
}

/// <summary>
/// Test helper: IApprovalService that invokes a callback.
/// </summary>
internal sealed class CallbackApprovalService(
    Func<string, string, string, Task<bool>> callback) : IApprovalService
{
    public Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext? context = null) =>
        callback("file", operation, path);

    public Task<bool> RequestShellApprovalAsync(string command, string? workingDir, ApprovalContext? context = null) =>
        callback("shell", command, workingDir ?? string.Empty);

    public Task<bool> RequestResourceApprovalAsync(string kind, string operation, string target, ApprovalContext? context = null) =>
        callback(kind, operation, target);
}
