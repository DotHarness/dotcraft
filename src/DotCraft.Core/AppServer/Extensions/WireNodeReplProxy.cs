using System.Collections.Concurrent;
using DotCraft.Tracing;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

/// <summary>
/// Routes agent-side Node REPL calls to the Desktop client bound to the current thread.
/// </summary>
public sealed class WireNodeReplProxy : INodeReplProxy
{
    private readonly ConcurrentDictionary<string, NodeReplThreadBinding> _byThread = new();

    /// <inheritdoc />
    public bool IsAvailable
    {
        get
        {
            var binding = GetCurrentBinding();
            return binding?.Connection is { HasNodeRepl: true, HasBrowserUse: true };
        }
    }

    /// <summary>
    /// Binds a thread to the transport that created/resumed it.
    /// </summary>
    public void BindThread(string threadId, IAppServerTransport transport, AppServerConnection connection)
    {
        if (!connection.HasNodeRepl || !connection.HasBrowserUse)
            return;
        _byThread[threadId] = new NodeReplThreadBinding(threadId, transport, connection);
    }

    /// <summary>
    /// Removes all thread bindings for a disconnected transport.
    /// </summary>
    public void UnbindTransport(IAppServerTransport transport)
    {
        foreach (var kv in _byThread.ToArray())
        {
            if (ReferenceEquals(kv.Value.Transport, transport))
                _byThread.TryRemove(kv.Key, out _);
        }
    }

    /// <summary>
    /// Removes a single thread binding.
    /// </summary>
    public void UnbindThread(string threadId) => _byThread.TryRemove(threadId, out _);

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
        try
        {
            var request = new Contract.NodeReplEvaluateParams
            {
                ThreadId = threadId,
                TurnId = turnId is null
                    ? default
                    : new DotCraft.Protocol.Optional<string?>(turnId),
                EvaluationId = evaluationId,
                BrowserSession = new Contract.NodeReplBrowserSessionParams
                {
                    ProtocolVersion = protocolVersion,
                    SessionId = sessionId,
                    ThreadId = threadId,
                    TurnId = turnId is null
                        ? default
                        : new DotCraft.Protocol.Optional<string?>(turnId),
                    EvaluationId = evaluationId,
                },
                Code = code,
                TimeoutMs = safeTimeout * 1000
            };
            var response = await binding.Transport.RequestAsync(
                Contract.AppServerRpc.ExtNodeReplEvaluate,
                request,
                ct,
                TimeSpan.FromSeconds(safeTimeout + 5));

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

    private NodeReplThreadBinding? GetCurrentBinding()
    {
        var threadId = ResolveCurrentThreadId();
        if (threadId == null)
            return null;
        return _byThread.TryGetValue(threadId, out var b) ? b : null;
    }

    private static string? ResolveCurrentThreadId()
        => TracingChatClient.CurrentSessionKey ?? TracingChatClient.GetActiveSessionKey();

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

    private static NodeReplEvaluation ToDomain(Contract.NodeReplEvaluateResult value) => new()
    {
        Text = ValueOrDefault(value.Text),
        ResultText = ValueOrDefault(value.ResultText),
        Error = ValueOrDefault(value.Error),
        Logs = ValueOrDefault(value.Logs)?.ToList() ?? [],
        Images = ValueOrDefault(value.Images)?.Select(static image => new NodeReplImage
        {
            MediaType = ValueOrDefault(image.MediaType) ?? "image/png",
            DataBase64 = ValueOrDefault(image.DataBase64) ?? string.Empty
        }).ToList() ?? []
    };

    private static T? ValueOrDefault<T>(DotCraft.Protocol.Optional<T> value) =>
        value.IsSet ? value.Value : default;

}
