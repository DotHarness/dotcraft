using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using DotCraft.Sdk.Wire;

namespace DotCraft.Sdk.AppServer;

/// <summary>
/// Active handle to one server-backed thread. Exposes the high-level Run profile
/// (<see cref="RunAsync(string, RunOptions?, CancellationToken)"/> and
/// <see cref="RunStreamedAsync(string, RunOptions?, CancellationToken)"/>) plus thread lifecycle operations.
/// </summary>
public sealed class DotCraftThread
{
    private readonly DotCraftClient _client;
    private readonly SemaphoreSlim _subscribeLock = new(1, 1);
    private JsonElement _snapshot;
    private bool _subscribed;

    internal DotCraftThread(DotCraftClient client, string id, JsonElement snapshot)
    {
        _client = client;
        Id = id;
        _snapshot = snapshot;
    }

    /// <summary>Thread identifier.</summary>
    public string Id { get; }

    /// <summary>Latest known raw thread snapshot.</summary>
    public JsonElement Snapshot => _snapshot;

    /// <summary>
    /// Runs one turn and returns the merged assistant reply after the terminal event.
    /// </summary>
    public Task<DotCraftRunResult> RunAsync(string text, RunOptions? options = null, CancellationToken cancellationToken = default) =>
        RunAsync(ToParts(text), options, cancellationToken);

    /// <summary>
    /// Runs one turn and returns the merged assistant reply after the terminal event.
    /// </summary>
    public async Task<DotCraftRunResult> RunAsync(
        IReadOnlyList<TurnInputPart> input,
        RunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var opts = options ?? new RunOptions();
        var reducer = new RunReducer();
        string? turnId = null;
        string? terminalType = null;
        JsonElement? terminalTurn = null;
        string? errorMessage = null;
        List<AppServerNotification>? raw = opts.CollectRawEvents ? [] : null;

        await foreach (var evt in RunStreamedAsync(input, opts, cancellationToken).ConfigureAwait(false))
        {
            raw?.Add(evt.Raw);
            reducer.Accept(evt);
            turnId ??= evt.TurnId;

            if (IsTerminal(evt.Type))
            {
                terminalType = evt.Type;
                terminalTurn = ExtractTurn(evt.Raw.Params);
                errorMessage = ExtractTerminalError(evt.Raw.Params, evt.Type);
            }
        }

        if (opts.ThrowOnFailure && terminalType == DotCraftRunEventTypes.Failed)
        {
            throw new TurnFailedError(errorMessage ?? "The turn failed.", Id, turnId);
        }

        if (opts.ThrowOnFailure && terminalType == DotCraftRunEventTypes.Cancelled)
        {
            throw new TurnCancelledError(Id, turnId, errorMessage);
        }

        return new DotCraftRunResult(Id, turnId, reducer.GetText(terminalTurn), terminalTurn, raw);
    }

    /// <summary>
    /// Runs one turn and streams normalized events until the terminal event.
    /// </summary>
    public IAsyncEnumerable<DotCraftRunEvent> RunStreamedAsync(string text, RunOptions? options = null, CancellationToken cancellationToken = default) =>
        RunStreamedAsync(ToParts(text), options, cancellationToken);

    /// <summary>
    /// Runs one turn and streams normalized events until the terminal event.
    /// </summary>
    public async IAsyncEnumerable<DotCraftRunEvent> RunStreamedAsync(
        IReadOnlyList<TurnInputPart> input,
        RunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var opts = options ?? new RunOptions();
        await EnsureSubscribedAsync(cancellationToken).ConfigureAwait(false);

        var channel = Channel.CreateUnbounded<DotCraftRunEvent>(new UnboundedChannelOptions { SingleReader = true });
        using var registration = _client.Wire.RegisterNotificationHandlerRaw(notification =>
        {
            var threadId = ExtractThreadId(notification.Params);
            if (threadId is null || !string.Equals(threadId, Id, StringComparison.Ordinal))
            {
                return;
            }

            var runEvent = Normalize(notification);
            channel.Writer.TryWrite(runEvent);
            if (IsTerminal(runEvent.Type))
            {
                channel.Writer.TryComplete();
            }
        });

        string? turnId = null;
        var busy = false;
        try
        {
            var startResult = await _client.Turns
                .StartAsync(Id, input, opts.Sender, opts.ModelId, cancellationToken)
                .ConfigureAwait(false);
            turnId = startResult.TurnId;
        }
        catch (JsonRpcException ex) when (ex.Code == AppServerErrorCodes.TurnInProgress)
        {
            if (!opts.EnqueueIfBusy)
            {
                throw new TurnInProgressError("A turn is already running on this thread.", ex);
            }

            busy = true;
        }

        if (busy)
        {
            var enqueue = await _client.Turns
                .EnqueueAsync(Id, input, opts.Sender, cancellationToken)
                .ConfigureAwait(false);
            yield return new DotCraftRunEvent(
                DotCraftRunEventTypes.QueueUpdated,
                Id,
                null,
                new AppServerNotification("turn/enqueue", enqueue.Raw));
            yield break;
        }

        await using var interruptRegistration = turnId is null
            ? default
            : cancellationToken.Register(() =>
            {
                try
                {
                    _ = _client.Turns.InterruptAsync(Id, turnId, CancellationToken.None);
                }
                catch
                {
                    // Best-effort interrupt on cancellation.
                }
            });

        await foreach (var runEvent in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return runEvent;
            if (IsTerminal(runEvent.Type))
            {
                yield break;
            }
        }
    }

    /// <summary>Enqueues input to run after the active turn.</summary>
    public Task<DotCraftTurnEnqueueResult> EnqueueAsync(
        IReadOnlyList<TurnInputPart> input,
        object? sender = null,
        CancellationToken cancellationToken = default) =>
        _client.Turns.EnqueueAsync(Id, input, sender, cancellationToken);

    /// <summary>Interrupts a running turn on this thread.</summary>
    public Task InterruptAsync(string turnId, CancellationToken cancellationToken = default) =>
        _client.Turns.InterruptAsync(Id, turnId, cancellationToken);

    /// <summary>Subscribes this connection to the thread's events.</summary>
    public async Task SubscribeAsync(bool replayRecent = false, CancellationToken cancellationToken = default)
    {
        await _client.RequestRawAsync("thread/subscribe", new { threadId = Id, replayRecent }, cancellationToken).ConfigureAwait(false);
        _subscribed = true;
    }

    /// <summary>Unsubscribes this connection from the thread's events.</summary>
    public async Task UnsubscribeAsync(CancellationToken cancellationToken = default)
    {
        await _client.RequestRawAsync("thread/unsubscribe", new { threadId = Id }, cancellationToken).ConfigureAwait(false);
        _subscribed = false;
    }

    /// <summary>Sets the thread operational mode.</summary>
    public Task SetModeAsync(string mode, CancellationToken cancellationToken = default) =>
        _client.RequestRawAsync("thread/mode/set", new { threadId = Id, mode }, cancellationToken);

    /// <summary>Archives the thread.</summary>
    public Task ArchiveAsync(CancellationToken cancellationToken = default) =>
        _client.RequestRawAsync("thread/archive", new { threadId = Id }, cancellationToken);

    /// <summary>Deletes the thread.</summary>
    public Task DeleteAsync(CancellationToken cancellationToken = default) =>
        _client.RequestRawAsync("thread/delete", new { threadId = Id }, cancellationToken);

    /// <summary>Re-reads the thread snapshot from the server.</summary>
    public async Task<JsonElement> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var result = await _client.Threads.ReadAsync(Id, cancellationToken: cancellationToken).ConfigureAwait(false);
        _snapshot = result.Thread;
        return _snapshot;
    }

    /// <summary>Registers a runtime dynamic tool handler scoped to this thread.</summary>
    public IDisposable OnToolCall(
        string? @namespace,
        string toolName,
        Func<DynamicToolCall, CancellationToken, Task<DynamicToolResult>> handler) =>
        _client.RegisterDynamicToolHandler(Id, @namespace, toolName, handler);

    internal void UpdateSnapshot(JsonElement snapshot) => _snapshot = snapshot;

    private async Task EnsureSubscribedAsync(CancellationToken cancellationToken)
    {
        if (_subscribed)
        {
            return;
        }

        await _subscribeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_subscribed)
            {
                return;
            }

            await _client.RequestRawAsync("thread/subscribe", new { threadId = Id, replayRecent = false }, cancellationToken).ConfigureAwait(false);
            _subscribed = true;
        }
        finally
        {
            _subscribeLock.Release();
        }
    }

    private static IReadOnlyList<TurnInputPart> ToParts(string text) => [new TurnInputPart("text", Text: text)];

    private static bool IsTerminal(string type) =>
        type is DotCraftRunEventTypes.Completed or DotCraftRunEventTypes.Failed or DotCraftRunEventTypes.Cancelled;

    private static DotCraftRunEvent Normalize(AppServerNotification notification)
    {
        var type = EventType(notification.Method);
        var threadId = ExtractThreadId(notification.Params) ?? string.Empty;
        var turnId = ExtractTurnId(notification.Params);
        return new DotCraftRunEvent(type, threadId, turnId, notification);
    }

    private static string EventType(string method) => method switch
    {
        "thread/started" => DotCraftRunEventTypes.ThreadStarted,
        "thread/resumed" => DotCraftRunEventTypes.ThreadResumed,
        "thread/statusChanged" => DotCraftRunEventTypes.ThreadStatusChanged,
        "thread/runtimeChanged" => DotCraftRunEventTypes.ThreadRuntimeChanged,
        "thread/queue/updated" => DotCraftRunEventTypes.QueueUpdated,
        "turn/started" => DotCraftRunEventTypes.TurnStarted,
        "item/started" => DotCraftRunEventTypes.ItemStarted,
        "item/completed" => DotCraftRunEventTypes.ItemCompleted,
        "item/agentMessage/delta" => DotCraftRunEventTypes.AgentMessageDelta,
        "item/reasoning/delta" => DotCraftRunEventTypes.ReasoningDelta,
        "item/toolCall/argumentsDelta" => DotCraftRunEventTypes.ToolArgumentsDelta,
        "item/approval/resolved" => DotCraftRunEventTypes.ApprovalResolved,
        "item/usage/delta" => DotCraftRunEventTypes.UsageDelta,
        "subagent/progress" => DotCraftRunEventTypes.SubagentProgress,
        "plan/updated" => DotCraftRunEventTypes.PlanUpdated,
        "system/event" => DotCraftRunEventTypes.SystemEvent,
        "turn/completed" => DotCraftRunEventTypes.Completed,
        "turn/failed" => DotCraftRunEventTypes.Failed,
        "turn/cancelled" => DotCraftRunEventTypes.Cancelled,
        _ => DotCraftRunEventTypes.Raw
    };

    private static string? ExtractThreadId(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return JsonElementReaders.ReadString(parameters, "threadId")
               ?? JsonElementReaders.ReadNestedString(parameters, "turn", "threadId")
               ?? JsonElementReaders.ReadNestedString(parameters, "thread", "id");
    }

    private static string? ExtractTurnId(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return JsonElementReaders.ReadString(parameters, "turnId")
               ?? JsonElementReaders.ReadNestedString(parameters, "turn", "id");
    }

    private static JsonElement? ExtractTurn(JsonElement parameters) =>
        parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("turn", out var turn)
            ? turn.Clone()
            : null;

    private static string? ExtractTerminalError(JsonElement parameters, string type)
    {
        if (parameters.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (type == DotCraftRunEventTypes.Failed)
        {
            return JsonElementReaders.ReadString(parameters, "error")
                   ?? JsonElementReaders.ReadNestedString(parameters, "turn", "error");
        }

        if (type == DotCraftRunEventTypes.Cancelled)
        {
            return JsonElementReaders.ReadString(parameters, "reason");
        }

        return null;
    }

    /// <summary>
    /// Merges streamed agent-message deltas with final item snapshots so the run text is not duplicated.
    /// </summary>
    private sealed class RunReducer
    {
        private readonly List<string> _order = [];
        private readonly Dictionary<string, StringBuilder> _deltas = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _snapshots = new(StringComparer.Ordinal);

        public void Accept(DotCraftRunEvent runEvent)
        {
            switch (runEvent.Type)
            {
                case DotCraftRunEventTypes.AgentMessageDelta:
                {
                    var itemId = JsonElementReaders.ReadString(runEvent.Params, "itemId");
                    var delta = JsonElementReaders.ReadString(runEvent.Params, "delta");
                    if (itemId is null || delta is null)
                    {
                        return;
                    }

                    if (!_deltas.TryGetValue(itemId, out var builder))
                    {
                        builder = new StringBuilder();
                        _deltas[itemId] = builder;
                        Track(itemId);
                    }

                    builder.Append(delta);
                    break;
                }

                case DotCraftRunEventTypes.ItemCompleted:
                {
                    if (!runEvent.Params.TryGetProperty("item", out var item) || item.ValueKind != JsonValueKind.Object)
                    {
                        return;
                    }

                    if (JsonElementReaders.ReadString(item, "type") != "agentMessage")
                    {
                        return;
                    }

                    var itemId = JsonElementReaders.ReadString(item, "id");
                    var text = JsonElementReaders.ReadNestedString(item, "payload", "text");
                    if (itemId is null || text is null)
                    {
                        return;
                    }

                    _snapshots[itemId] = text;
                    Track(itemId);
                    break;
                }
            }
        }

        public string GetText(JsonElement? terminalTurn)
        {
            // Prefer the authoritative final items from turn/completed.
            if (terminalTurn is { ValueKind: JsonValueKind.Object } turn &&
                turn.TryGetProperty("items", out var items) &&
                items.ValueKind == JsonValueKind.Array)
            {
                var fromTurn = items.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.Object && JsonElementReaders.ReadString(item, "type") == "agentMessage")
                    .Select(item => JsonElementReaders.ReadNestedString(item, "payload", "text"))
                    .Where(text => !string.IsNullOrEmpty(text))
                    .ToList();
                if (fromTurn.Count > 0)
                {
                    return string.Join("\n\n", fromTurn);
                }
            }

            // Fall back to streamed snapshots/deltas, preferring snapshots when at least as long.
            var merged = _order
                .Select(ResolveItemText)
                .Where(text => !string.IsNullOrEmpty(text));
            return string.Join("\n\n", merged);
        }

        private string ResolveItemText(string itemId)
        {
            var hasSnapshot = _snapshots.TryGetValue(itemId, out var snapshot);
            var hasDelta = _deltas.TryGetValue(itemId, out var deltaBuilder);
            var delta = hasDelta ? deltaBuilder!.ToString() : string.Empty;

            if (hasSnapshot && (!hasDelta || snapshot!.Length >= delta.Length))
            {
                return snapshot!;
            }

            return delta;
        }

        private void Track(string itemId)
        {
            if (!_order.Contains(itemId))
            {
                _order.Add(itemId);
            }
        }
    }
}
