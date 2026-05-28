using System.Collections.Concurrent;
using DotCraft.Security;

namespace DotCraft.Protocol;

/// <summary>
/// Per-Turn IApprovalService that routes approval requests through the Session event stream.
/// When a tool requests approval, this service creates an ApprovalRequest Item, emits the
/// approval/requested event, and suspends tool execution until the adapter calls ResolveApproval.
/// </summary>
internal sealed class SessionApprovalService : IApprovalService
{
    private readonly SessionEventChannel _channel;
    private readonly SessionTurn _turn;
    private readonly Func<int> _nextItemSeq;
    private readonly TimeSpan _timeout;
    private readonly Action _cancelTurn;
    private readonly ApprovalStore? _store;
    private readonly Action<string, SessionThreadRuntimeSignal>? _runtimeSignalForBroadcast;

    private readonly ConcurrentDictionary<string, PendingApproval> _pending = new();
    private readonly ConcurrentDictionary<string, byte> _sessionApprovedScopes = new();

    private sealed class PendingApproval(
        string scopeKey,
        ApprovalRequestPayload payload,
        TaskCompletionSource<SessionApprovalDecision> completion)
    {
        public string ScopeKey { get; } = scopeKey;
        public ApprovalRequestPayload Payload { get; } = payload;
        public TaskCompletionSource<SessionApprovalDecision> Completion { get; } = completion;
    }

    public SessionApprovalService(
        SessionEventChannel channel,
        SessionTurn turn,
        Func<int> nextItemSeq,
        TimeSpan timeout,
        Action cancelTurn,
        ApprovalStore? store = null,
        Action<string, SessionThreadRuntimeSignal>? runtimeSignalForBroadcast = null)
    {
        _channel = channel;
        _turn = turn;
        _nextItemSeq = nextItemSeq;
        _timeout = timeout;
        _cancelTurn = cancelTurn;
        _store = store;
        _runtimeSignalForBroadcast = runtimeSignalForBroadcast;
    }

    /// <summary>
    /// True when there is at least one unresolved approval request.
    /// </summary>
    public bool HasPendingApproval => !_pending.IsEmpty;

    // -------------------------------------------------------------------------
    // IApprovalService
    // -------------------------------------------------------------------------

    public Task<bool> RequestFileApprovalAsync(
        string operation,
        string path,
        ApprovalContext? context = null)
    {
        var requestId = Guid.NewGuid().ToString("N")[..12];
        var scopeKey = BuildScopeKey("file", operation, path);
        var payload = new ApprovalRequestPayload
        {
            ApprovalType = "file",
            Operation = operation,
            Target = path,
            RequestId = requestId,
            ScopeKey = scopeKey,
            Reason = $"Agent wants to perform a '{operation}' file operation on: {path}"
        };
        return RequestApprovalAsync(requestId, scopeKey, payload);
    }

    public Task<bool> RequestShellApprovalAsync(
        string command,
        string? workingDir,
        ApprovalContext? context = null)
    {
        var requestId = Guid.NewGuid().ToString("N")[..12];
        var scopeKey = BuildScopeKey("shell", command, workingDir);
        var payload = new ApprovalRequestPayload
        {
            ApprovalType = "shell",
            Operation = command,
            Target = workingDir ?? string.Empty,
            RequestId = requestId,
            ScopeKey = scopeKey,
            Reason = $"Agent wants to execute a shell command: {command}"
        };
        return RequestApprovalAsync(requestId, scopeKey, payload);
    }

    public Task<bool> RequestResourceApprovalAsync(
        string kind,
        string operation,
        string target,
        ApprovalContext? context = null)
    {
        var requestId = Guid.NewGuid().ToString("N")[..12];
        var scopeKey = BuildScopeKey(kind, operation, target);
        var payload = new ApprovalRequestPayload
        {
            ApprovalType = kind,
            Operation = operation,
            Target = target,
            RequestId = requestId,
            ScopeKey = scopeKey,
            Reason = $"Agent wants to perform '{operation}' on remote resource: {target}"
        };
        return RequestApprovalAsync(requestId, scopeKey, payload);
    }

    // -------------------------------------------------------------------------
    // Called by SessionService.ResolveApprovalAsync
    // -------------------------------------------------------------------------

    /// <summary>
    /// Resolves a pending approval request with the user's decision.
    /// Returns false if no matching pending request exists.
    /// </summary>
    public bool TryResolve(string requestId, SessionApprovalDecision decision)
    {
        if (!_pending.TryRemove(requestId, out var pending))
            return false;

        if (decision.AppliesToSession())
            _sessionApprovedScopes.TryAdd(pending.ScopeKey, 0);

        if (decision.IsPersistent())
            PersistApproval(pending.Payload);

        // Create and emit ApprovalResponse Item
        var responseItem = CreateItem(ItemType.ApprovalResponse, new ApprovalResponsePayload
        {
            RequestId = requestId,
            Approved = decision.IsApproved(),
            Decision = decision
        });
        _turn.Items.Add(responseItem);

        // Restore Running status before completing TCS so the Turn status is correct
        // when agent execution resumes
        _turn.Status = TurnStatus.Running;

        _channel.EmitItemStarted(responseItem);
        _channel.EmitApprovalResolved(responseItem);
        _channel.EmitItemCompleted(responseItem);
        _runtimeSignalForBroadcast?.Invoke(_turn.ThreadId, SessionThreadRuntimeSignal.ApprovalResolved);

        if (decision == SessionApprovalDecision.CancelTurn)
            _cancelTurn();

        pending.Completion.TrySetResult(decision);
        return true;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private async Task<bool> RequestApprovalAsync(
        string requestId,
        string scopeKey,
        ApprovalRequestPayload payload)
    {
        if (_sessionApprovedScopes.ContainsKey(scopeKey))
            return true;

        if (IsPersistedApproval(payload))
            return true;

        // Create ApprovalRequest Item
        var requestItem = CreateItem(ItemType.ApprovalRequest, payload);
        _turn.Items.Add(requestItem);
        _turn.Status = TurnStatus.WaitingApproval;

        // Register TCS before emitting the event so there's no race
        var tcs = new TaskCompletionSource<SessionApprovalDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = new PendingApproval(scopeKey, payload, tcs);

        _channel.EmitItemStarted(requestItem);
        _channel.EmitItemCompleted(requestItem);
        _channel.EmitApprovalRequested(requestItem);
        _runtimeSignalForBroadcast?.Invoke(_turn.ThreadId, SessionThreadRuntimeSignal.ApprovalRequested);

        // Apply timeout
        using var cts = new CancellationTokenSource(_timeout);
        await using var reg = cts.Token.Register(() =>
        {
            if (_pending.TryRemove(requestId, out var pending))
            {
                // Timeout: emit an Error Item and auto-reject
                var errorItem = CreateItem(ItemType.Error, new ErrorPayload
                {
                    Message = $"Approval request '{requestId}' timed out after {_timeout.TotalSeconds:0}s.",
                    Code = "approval_timeout",
                    Fatal = false
                });
                _turn.Items.Add(errorItem);
                _turn.Status = TurnStatus.Running;
                _channel.EmitItemStarted(errorItem);
                _channel.EmitItemCompleted(errorItem);

                pending.Completion.TrySetResult(SessionApprovalDecision.Reject);
            }
        });

        var decision = await tcs.Task;
        return decision.IsApproved();
    }

    private bool IsPersistedApproval(ApprovalRequestPayload payload)
    {
        if (_store == null) return false;
        return payload.ApprovalType switch
        {
            "file" => _store.IsFileOperationApproved(payload.Operation, payload.Target),
            "shell" => _store.IsShellCommandApproved(payload.Operation, payload.Target),
            _ => false
        };
    }

    private void PersistApproval(ApprovalRequestPayload payload)
    {
        if (_store == null) return;
        switch (payload.ApprovalType)
        {
            case "file":
                _store.RecordFileOperation(payload.Operation, payload.Target);
                break;
            case "shell":
                _store.RecordShellCommand(payload.Operation, payload.Target);
                break;
        }
    }

    private static string BuildScopeKey(string approvalType, string operation, string? target)
    {
        var normalizedType = approvalType.ToLowerInvariant();
        var normalizedOperation = operation.ToLowerInvariant();
        return normalizedType switch
        {
            "shell" => "shell:*",
            "file" => $"file:{normalizedOperation}",
            _ => $"{normalizedType}:{normalizedOperation}:{target ?? string.Empty}"
        };
    }

    private SessionItem CreateItem(ItemType type, object payload)
    {
        var seq = _nextItemSeq();
        var item = new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(seq),
            TurnId = _turn.Id,
            Type = type,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = payload
        };
        return item;
    }
}
