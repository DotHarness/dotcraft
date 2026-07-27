using System.Collections.Concurrent;
using System.Text.Json;
using DotCraft.Tracing;

namespace DotCraft.Protocol.AppServer;

/// <summary>
/// Routes agent-side Node REPL calls to the Desktop client bound to the current thread.
/// </summary>
public sealed class WireNodeReplProxy : INodeReplProxy
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

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
    public async Task<NodeReplEvaluateResult?> EvaluateAsync(
        string code,
        int? timeoutSeconds = null,
        CancellationToken ct = default,
        NodeReplEvaluationMetadata? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            return new NodeReplEvaluateResult { Error = "NodeReplJs requires non-empty code." };

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
            var response = await binding.Transport.SendClientRequestAsync(
                AppServerMethods.ExtNodeReplEvaluate,
                new
                {
                    threadId,
                    turnId,
                    evaluationId,
                    browserSession = new
                    {
                        protocolVersion,
                        sessionId,
                        threadId,
                        turnId,
                        evaluationId
                    },
                    code,
                    timeoutMs = safeTimeout * 1000
                },
                ct,
                TimeSpan.FromSeconds(safeTimeout + 5));

            if (!response.Result.HasValue)
                return new NodeReplEvaluateResult
                {
                    Error = response.Error.HasValue ? response.Error.Value.ToString() : "Node REPL client returned no result."
                };

            try
            {
                return response.Result.Value.Deserialize<NodeReplEvaluateResult>(JsonOptions);
            }
            catch (Exception ex)
            {
                return new NodeReplEvaluateResult { Error = $"Failed to parse Node REPL response: {ex.Message}" };
            }
        }
        catch (OperationCanceledException)
        {
            SendCancelRequest(binding, threadId, evaluationId);
            return new NodeReplEvaluateResult { Error = "Node REPL evaluation was cancelled." };
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
                await binding.Transport.SendClientRequestAsync(
                    AppServerMethods.ExtNodeReplCancel,
                    new { threadId, evaluationId },
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

}
