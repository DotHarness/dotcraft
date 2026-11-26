using System.Collections.Concurrent;
using DotCraft.Contributions;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Tools;
using DotCraft.Tracing;
using DotCraft.Utilities;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

/// <summary>
/// Routes agent-side Node REPL calls to the Desktop client bound to the current thread.
/// </summary>
public sealed class WireNodeReplProxy : INodeReplProxy, IThreadForkToolBindingSource, IThreadLifecycleObserver, ITurnLifecycleContributor
{
    private readonly ConcurrentDictionary<string, NodeReplThreadBinding> _byThread = new();
    private readonly ConcurrentDictionary<string, bool> _activeSessions = new();
    private readonly ConcurrentDictionary<string, InFlightEvaluation> _inFlight = new();

    /// <inheritdoc />
    public bool IsAvailable
    {
        get
        {
            var binding = GetCurrentBinding();
            return binding?.Connection is { } connection && SupportsNodeRepl(connection);
        }
    }

    /// <summary>
    /// Binds a thread to the transport that created/resumed it.
    /// </summary>
    public bool BindThread(string threadId, IAppServerTransport transport, AppServerConnection connection,
        bool replaceExisting = true)
    {
        if (!SupportsNodeRepl(connection) || connection.IsClosed)
            return false;
        var candidate = new NodeReplThreadBinding(threadId, transport, connection);
        var binding = _byThread.AddOrUpdate(threadId, candidate, (_, current) =>
            ReferenceEquals(current.Connection, connection) || (!replaceExisting && !current.Connection.IsClosed)
                ? current : candidate);
        return ReferenceEquals(binding, candidate);
    }

    bool IThreadForkToolBindingSource.TryForkThreadBinding(string parentThreadId, string childThreadId)
    {
        if (!_byThread.TryGetValue(parentThreadId, out var parent)
            || parent.Connection.IsClosed)
        {
            return false;
        }

        _byThread[childThreadId] = parent with { ThreadId = childThreadId };
        return true;
    }

    /// <summary>
    /// Removes all thread bindings for a disconnected transport.
    /// </summary>
    public void UnbindTransport(IAppServerTransport transport)
    {
        foreach (var kv in _byThread.ToArray())
        {
            if (ReferenceEquals(kv.Value.Transport, transport) && _byThread.TryRemove(kv))
                _activeSessions.TryRemove(kv.Key, out _);
        }
    }

    /// <summary>
    /// Removes a single thread binding.
    /// </summary>
    public void UnbindThread(string threadId)
    {
        _byThread.TryRemove(threadId, out _);
        _activeSessions.TryRemove(threadId, out _);
    }

    public async Task DisposeThreadSessionAsync(
        string threadId,
        bool isSubagent = false,
        CancellationToken ct = default)
    {
        var wasActive = _activeSessions.TryRemove(threadId, out var wasSubagent);
        if (!_byThread.TryRemove(threadId, out var binding) || binding.Connection.IsClosed)
            return;

        var effectiveSubagent = wasSubagent || isSubagent;
        if (!wasActive && !effectiveSubagent)
            return;

        await SendTurnEndedAsync(binding, threadId, turnId: null, effectiveSubagent, disposeSession: true, ct);
    }

    public async Task OnTurnEndedAsync(TurnLifecycleContext context, CancellationToken cancellationToken = default)
    {
        if (!_activeSessions.TryGetValue(context.ThreadId, out var isSubagent))
            return;

        if (isSubagent)
            _activeSessions.TryRemove(context.ThreadId, out _);

        if (!_byThread.TryGetValue(context.ThreadId, out var binding) || binding.Connection.IsClosed)
            return;

        await SendTurnEndedAsync(binding, context.ThreadId, context.TurnId, isSubagent, disposeSession: isSubagent, cancellationToken);
    }

    public Task OnThreadDeletingAsync(SessionThread thread, CancellationToken cancellationToken = default) =>
        DisposeThreadSessionAsync(thread.Id, thread.Source.SubAgent is not null, cancellationToken);

    /// <inheritdoc />
    public bool IsComputerUseAvailable
    {
        get
        {
            var binding = GetCurrentBinding();
            return binding?.Connection is { HasNodeRepl: true, HasComputerUse: true };
        }
    }

    /// <inheritdoc />
    public bool IsBrowserUseAvailable
    {
        get
        {
            var binding = GetCurrentBinding();
            return binding?.Connection is { HasNodeRepl: true, HasBrowserUse: true };
        }
    }

    public async Task<bool> RequestApprovalAsync(
        AppServerConnection connection,
        string threadId,
        string evaluationId,
        ResourceApprovalRequest request)
    {
        if (!_inFlight.TryGetValue(evaluationId, out var evaluation)
            || !ReferenceEquals(evaluation.Connection, connection)
            || !string.Equals(evaluation.ThreadId, threadId, StringComparison.Ordinal))
        {
            throw new KeyNotFoundException($"Evaluation '{evaluationId}' is not in flight for thread '{threadId}'.");
        }

        if (evaluation.ApprovalService is null)
            return false;

        evaluation.Deadline.Pause();
        try
        {
            return await evaluation.ApprovalService.RequestResourceApprovalAsync(request).ConfigureAwait(false);
        }
        finally
        {
            evaluation.Deadline.Resume();
        }
    }

    /// <inheritdoc />
    public async Task<NodeReplEvaluation?> EvaluateAsync(
        string code,
        int? timeoutSeconds = null,
        CancellationToken ct = default,
        NodeReplEvaluationMetadata? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            return new NodeReplEvaluation { Error = "NodeReplJs requires non-empty code." };

        var threadId = string.IsNullOrWhiteSpace(metadata?.ThreadId)
            ? ResolveCurrentThreadId()
            : metadata!.ThreadId;
        if (threadId == null || !_byThread.TryGetValue(threadId, out var binding))
            return null;

        var safeTimeout = Math.Clamp(timeoutSeconds ?? 30, 1, 120);
        var evaluationId = "node-repl-" + Guid.NewGuid().ToString("N");
        var sessionId = string.IsNullOrWhiteSpace(metadata?.SessionId) ? threadId : metadata!.SessionId;
        var turnId = string.IsNullOrWhiteSpace(metadata?.TurnId) ? null : metadata!.TurnId;
        var protocolVersion = metadata?.ProtocolVersion > 0 ? metadata.ProtocolVersion : 1;
        var isSubagent = metadata?.IsSubagent == true;
        _activeSessions.AddOrUpdate(threadId, isSubagent, (_, existing) => existing || isSubagent);
        try
        {
            var request = new Contract.NodeReplEvaluateParams
            {
                ThreadId = threadId,
                TurnId = turnId is null
                    ? default
                    : new Protocol.Optional<string?>(turnId),
                EvaluationId = evaluationId,
                BrowserSession = new Contract.NodeReplBrowserSessionParams
                {
                    ProtocolVersion = protocolVersion,
                    SessionId = sessionId,
                    ThreadId = threadId,
                    TurnId = turnId is null
                        ? default
                        : new Protocol.Optional<string?>(turnId),
                    EvaluationId = evaluationId,
                    IsSubagent = isSubagent ? new Protocol.Optional<bool>(true) : default
                },
                Code = code,
                TimeoutMs = safeTimeout * 1000
            };
            using var deadline = new PausableDeadline(TimeSpan.FromSeconds(safeTimeout + 5), ct);
            _inFlight[evaluationId] = new InFlightEvaluation(
                threadId,
                binding.Connection,
                deadline,
                ToolHostExecutionScope.Current?.ApprovalService);
            AppServerTypedClientResponse<Contract.NodeReplEvaluateResult> response;
            try
            {
                response = await binding.Transport.RequestAsync(
                    Contract.AppServerRpc.ExtNodeReplEvaluate,
                    request,
                    deadline.Token,
                    Timeout.InfiniteTimeSpan);
            }
            finally
            {
                _inFlight.TryRemove(evaluationId, out _);
            }

            if (response.Result is null)
                return new NodeReplEvaluation
                {
                    Error = response.Error?.ToString() ?? response.InvalidResult ?? "Node REPL client returned no result."
                };

            try
            {
                return ToDomain(response.Result);
            }
            catch (Exception ex)
            {
                return new NodeReplEvaluation { Error = $"Failed to parse Node REPL response: {ex.Message}" };
            }
        }
        catch (OperationCanceledException)
        {
            SendCancelRequest(binding, threadId, evaluationId);
            return new NodeReplEvaluation { Error = "Node REPL evaluation was cancelled." };
        }
    }

    private static bool SupportsNodeRepl(AppServerConnection connection) =>
        connection.HasNodeRepl && (connection.HasBrowserUse || connection.HasComputerUse);

    private NodeReplThreadBinding? GetCurrentBinding()
    {
        var threadId = ResolveCurrentThreadId();
        if (threadId == null)
            return null;
        return _byThread.TryGetValue(threadId, out var b) ? b : null;
    }

    private static string? ResolveCurrentThreadId()
        => TracingChatClient.CurrentSessionKey ?? TracingChatClient.GetActiveSessionKey();

    private static async Task SendTurnEndedAsync(
        NodeReplThreadBinding binding,
        string threadId,
        string? turnId,
        bool isSubagent,
        bool disposeSession,
        CancellationToken ct)
    {
        try
        {
            await binding.Transport.NotifyContractAsync(
                Contract.AppServerRpc.ExtNodeReplTurnEnded,
                new Contract.NodeReplTurnEndedParams
                {
                    ThreadId = threadId,
                    TurnId = turnId is null ? default : new Protocol.Optional<string?>(turnId),
                    IsSubagent = isSubagent ? new Protocol.Optional<bool>(true) : default,
                    DisposeSession = disposeSession ? new Protocol.Optional<bool>(true) : default
                },
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
        {
        }
    }

    private static void SendCancelRequest(
        NodeReplThreadBinding binding,
        string threadId,
        string evaluationId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await binding.Transport.RequestAsync(
                    Contract.AppServerRpc.ExtNodeReplCancel,
                    new Contract.NodeReplCancelParams { ThreadId = threadId, EvaluationId = evaluationId },
                    CancellationToken.None,
                    TimeSpan.FromSeconds(2));
            }
            catch
            {
                // Best-effort cancellation; the original evaluate path already returned.
            }
        });
    }

    private sealed record NodeReplThreadBinding(
        string ThreadId,
        IAppServerTransport Transport,
        AppServerConnection Connection);

    private sealed record InFlightEvaluation(
        string ThreadId,
        AppServerConnection Connection,
        PausableDeadline Deadline,
        IApprovalService? ApprovalService);

    private static NodeReplEvaluation ToDomain(Contract.NodeReplEvaluateResult value) => new()
    {
        Text = ValueOrDefault(value.Text),
        ResultText = ValueOrDefault(value.ResultText),
        Error = ValueOrDefault(value.Error),
        Surface = ValueOrDefault(value.Surface),
        Logs =ValueOrDefault(value.Logs)?.ToList() ?? [],
        Images = ValueOrDefault(value.Images)?.Select(static image => new NodeReplImage
        {
            MediaType = ValueOrDefault(image.MediaType) ?? "image/png",
            DataBase64 = ValueOrDefault(image.DataBase64) ?? string.Empty
        }).ToList() ?? []
    };

    private static T? ValueOrDefault<T>(Protocol.Optional<T> value) =>
        value.IsSet ? value.Value : default;

}
