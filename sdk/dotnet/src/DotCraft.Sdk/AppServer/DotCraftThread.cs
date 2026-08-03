using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using DotCraft.Protocol.Contracts;
using DotCraft.Protocol.Contracts.AppServer;
using DotCraft.Sdk.Wire;

namespace DotCraft.Sdk.AppServer;

/// <summary>Active handle to one server-backed thread.</summary>
public sealed class DotCraftThread
{
    private readonly DotCraftClient _client;
    private readonly SemaphoreSlim _subscribeLock = new(1, 1);
    private SessionThread _snapshot;
    private long _subscribedGeneration;

    internal DotCraftThread(DotCraftClient client, SessionThread snapshot)
    {
        _client = client;
        _snapshot = snapshot;
        Id = snapshot.Id;
    }

    /// <summary>Thread identifier.</summary>
    public string Id { get; }

    /// <summary>Latest known typed thread snapshot.</summary>
    public SessionThread Snapshot => _snapshot;

    /// <summary>Runs one text turn and returns the merged assistant reply.</summary>
    public Task<DotCraftRunResult> RunAsync(
        string text,
        RunOptions? options = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(ToParts(text), options, cancellationToken);

    /// <summary>Runs one typed-input turn and returns the merged assistant reply.</summary>
    public async Task<DotCraftRunResult> RunAsync(
        IReadOnlyList<InputPart> input,
        RunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var value = options ?? new RunOptions();
        var reducer = new RunReducer();
        string? turnId = null;
        string? terminalType = null;
        SessionTurn? terminalTurn = null;
        string? errorMessage = null;
        List<AppServerNotification>? raw = value.CollectRawEvents ? [] : null;

        await foreach (var runEvent in RunStreamedAsync(input, value, cancellationToken).ConfigureAwait(false))
        {
            raw?.Add(runEvent.Raw);
            reducer.Accept(runEvent);
            turnId ??= runEvent.TurnId;
            if (!IsTerminal(runEvent.Type))
                continue;

            terminalType = runEvent.Type;
            if (runEvent is DotCraftRunEvent<TurnNotification> terminal)
            {
                terminalTurn = terminal.Params.Turn;
                errorMessage = terminal.Type == DotCraftRunEventTypes.Cancelled
                    ? terminal.Params.Reason
                    : terminal.Params.Error ?? terminal.Params.Turn.Error;
            }
        }

        if (value.ThrowOnFailure && terminalType == DotCraftRunEventTypes.Failed)
            throw new TurnFailedError(errorMessage ?? "The turn failed.", Id, turnId);
        if (value.ThrowOnFailure && terminalType == DotCraftRunEventTypes.Cancelled)
            throw new TurnCancelledError(Id, turnId, errorMessage);

        return new DotCraftRunResult(Id, turnId, reducer.GetText(terminalTurn), terminalTurn, raw);
    }

    /// <summary>Runs one text turn and streams typed events until its terminal event.</summary>
    public IAsyncEnumerable<DotCraftRunEvent> RunStreamedAsync(
        string text,
        RunOptions? options = null,
        CancellationToken cancellationToken = default) =>
        RunStreamedAsync(ToParts(text), options, cancellationToken);

    /// <summary>Runs one typed-input turn and streams typed events until its terminal event.</summary>
    public async IAsyncEnumerable<DotCraftRunEvent> RunStreamedAsync(
        IReadOnlyList<InputPart> input,
        RunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var value = options ?? new RunOptions();
        var channel = Channel.CreateUnbounded<DotCraftRunEvent>(new UnboundedChannelOptions { SingleReader = true });
        string? turnId = null;

        void HandleConnectionState(WireConnectionState state, Exception? error)
        {
            if (state == WireConnectionState.Ready)
                return;
            Interlocked.Exchange(ref _subscribedGeneration, 0);
            channel.Writer.TryComplete(new RunDisconnectedError(Id, turnId, error));
        }

        _client.Wire.StateChanged += HandleConnectionState;
        using var notificationRegistration = _client.Wire.RegisterNotificationHandlerRaw(notification =>
        {
            try
            {
                var runEvent = AppServerRunEventFactory.Parse(notification);
                if (!string.Equals(runEvent.ThreadId, Id, StringComparison.Ordinal))
                    return;
                turnId ??= runEvent.TurnId;
                channel.Writer.TryWrite(runEvent);
                if (IsTerminal(runEvent.Type))
                    channel.Writer.TryComplete();
            }
            catch (Exception exception)
            {
                channel.Writer.TryComplete(exception);
            }
        });

        try
        {
            await EnsureSubscribedAsync(cancellationToken).ConfigureAwait(false);
            TurnEnqueueResult? queued = null;
            try
            {
                var start = await _client.Turns.StartAsync(Id, input, value.Sender, cancellationToken).ConfigureAwait(false);
                turnId = start.Turn.Id;
            }
            catch (JsonRpcException exception) when (exception.Code == AppServerErrorCodes.TurnInProgress)
            {
                if (!value.EnqueueIfBusy)
                    throw new TurnInProgressError("A turn is already running on this thread.", exception);

                queued = await _client.Turns.EnqueueAsync(Id, input, value.Sender, cancellationToken).ConfigureAwait(false);
            }

            if (queued is not null)
            {
                var raw = new AppServerNotification(
                    "turn/enqueue",
                    JsonSerializer.SerializeToElement(queued, AppServerContractJson.Options));
                yield return new DotCraftRunEvent<TurnEnqueueResult>(
                    DotCraftRunEventTypes.QueueUpdated,
                    Id,
                    null,
                    queued,
                    raw);
                yield break;
            }

            using var interruptRegistration = cancellationToken.Register(() =>
            {
                if (turnId is not null)
                    _ = _client.Turns.InterruptAsync(Id, turnId, CancellationToken.None);
            });

            await foreach (var runEvent in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return runEvent;
                if (IsTerminal(runEvent.Type))
                    yield break;
            }
        }
        finally
        {
            _client.Wire.StateChanged -= HandleConnectionState;
        }
    }

    /// <summary>Enqueues typed input to run after the active turn.</summary>
    public Task<TurnEnqueueResult> EnqueueAsync(
        IReadOnlyList<InputPart> input,
        SenderContext? sender = null,
        CancellationToken cancellationToken = default) =>
        _client.Turns.EnqueueAsync(Id, input, sender, cancellationToken);

    /// <summary>Interrupts a running turn on this thread.</summary>
    public async Task InterruptAsync(string turnId, CancellationToken cancellationToken = default) =>
        await _client.Turns.InterruptAsync(Id, turnId, cancellationToken).ConfigureAwait(false);

    /// <summary>Subscribes this connection to the thread's events.</summary>
    public async Task SubscribeAsync(bool replayRecent = false, CancellationToken cancellationToken = default)
    {
        await _client.Wire.ThreadSubscribeAsync(new ThreadSubscribeParams
        {
            ThreadId = Id,
            ReplayRecent = replayRecent
        }, cancellationToken).ConfigureAwait(false);
        Interlocked.Exchange(ref _subscribedGeneration, _client.Wire.ConnectionGeneration);
    }

    /// <summary>Unsubscribes this connection from the thread's events.</summary>
    public async Task UnsubscribeAsync(CancellationToken cancellationToken = default)
    {
        await _client.Wire.ThreadUnsubscribeAsync(new ThreadUnsubscribeParams { ThreadId = Id }, cancellationToken).ConfigureAwait(false);
        Interlocked.Exchange(ref _subscribedGeneration, 0);
    }

    /// <summary>Sets the thread operational mode.</summary>
    public async Task SetModeAsync(string mode, CancellationToken cancellationToken = default) =>
        await _client.Wire.ThreadModeSetAsync(new ThreadModeSetParams { ThreadId = Id, Mode = mode }, cancellationToken).ConfigureAwait(false);

    /// <summary>Archives the thread.</summary>
    public async Task ArchiveAsync(CancellationToken cancellationToken = default) =>
        await _client.Wire.ThreadArchiveAsync(new ThreadArchiveParams { ThreadId = Id }, cancellationToken).ConfigureAwait(false);

    /// <summary>Deletes the thread.</summary>
    public async Task DeleteAsync(CancellationToken cancellationToken = default) =>
        await _client.Wire.ThreadDeleteAsync(new ThreadDeleteParams { ThreadId = Id }, cancellationToken).ConfigureAwait(false);

    /// <summary>Re-reads and returns the typed thread snapshot.</summary>
    public async Task<SessionThread> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var result = await _client.Threads.ReadAsync(Id, cancellationToken: cancellationToken).ConfigureAwait(false);
        _snapshot = result.Thread;
        return _snapshot;
    }

    /// <summary>Registers a typed runtime dynamic-tool handler scoped to this thread.</summary>
    public IDisposable OnToolCall(
        string? @namespace,
        string toolName,
        Func<DynamicToolCallParams, CancellationToken, Task<DynamicToolCallResult>> handler) =>
        _client.RegisterDynamicToolHandler(Id, @namespace, toolName, handler);

    internal void UpdateSnapshot(SessionThread snapshot) => _snapshot = snapshot;

    private async Task EnsureSubscribedAsync(CancellationToken cancellationToken)
    {
        if (HasCurrentSubscription())
            return;
        await _subscribeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (HasCurrentSubscription())
                return;
            await SubscribeAsync(replayRecent: false, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _subscribeLock.Release();
        }
    }

    private bool HasCurrentSubscription()
    {
        var generation = _client.Wire.ConnectionGeneration;
        return generation != 0 &&
               _client.Wire.State == WireConnectionState.Ready &&
               Interlocked.Read(ref _subscribedGeneration) == generation;
    }

    private static IReadOnlyList<InputPart> ToParts(string text) => [new InputPart { Type = "text", Text = text }];

    private static bool IsTerminal(string type) =>
        type is DotCraftRunEventTypes.Completed or DotCraftRunEventTypes.Failed or DotCraftRunEventTypes.Cancelled;

    private sealed class RunReducer
    {
        private readonly List<string> _order = [];
        private readonly Dictionary<string, StringBuilder> _deltas = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _snapshots = new(StringComparer.Ordinal);

        public void Accept(DotCraftRunEvent runEvent)
        {
            if (runEvent.Type == DotCraftRunEventTypes.AgentMessageDelta &&
                runEvent is DotCraftRunEvent<ItemDeltaNotification> delta &&
                delta.Params.ItemId is not null)
            {
                if (!_deltas.TryGetValue(delta.Params.ItemId, out var builder))
                {
                    builder = new StringBuilder();
                    _deltas[delta.Params.ItemId] = builder;
                    Track(delta.Params.ItemId);
                }
                builder.Append(delta.Params.Delta);
                return;
            }

            if (runEvent.Type == DotCraftRunEventTypes.ItemCompleted &&
                runEvent is DotCraftRunEvent<ItemNotification> completed)
            {
                AcceptItem(completed.Params.Item);
            }
        }

        public string GetText(SessionTurn? terminalTurn)
        {
            if (terminalTurn?.Items is { } items)
            {
                var authoritative = items.Select(TryReadAgentText)
                    .Where(static text => !string.IsNullOrEmpty(text))
                    .ToArray();
                if (authoritative.Length > 0)
                    return string.Join("\n\n", authoritative!);
            }

            return string.Join("\n\n", _order.Select(ResolveItemText).Where(static text => !string.IsNullOrEmpty(text)));
        }

        private void AcceptItem(SessionItem item)
        {
            var text = TryReadAgentText(item);
            if (text is null)
                return;
            _snapshots[item.Id] = text;
            Track(item.Id);
        }

        private static string? TryReadAgentText(SessionItem item)
        {
            if (!string.Equals(item.PayloadKind, "agentMessage", StringComparison.Ordinal))
                return null;
            try
            {
                var parsed = SessionItemPayloadParser.Parse(item);
                return parsed.TryGet<AgentMessagePayload>(out var payload) ? payload!.Text : null;
            }
            catch (JsonException exception)
            {
                throw new AppServerProtocolException("An agentMessage item did not match AgentMessagePayload.", exception);
            }
        }

        private string ResolveItemText(string itemId)
        {
            var hasSnapshot = _snapshots.TryGetValue(itemId, out var snapshot);
            var hasDelta = _deltas.TryGetValue(itemId, out var deltaBuilder);
            var delta = hasDelta ? deltaBuilder!.ToString() : string.Empty;
            return hasSnapshot && (!hasDelta || snapshot!.Length >= delta.Length) ? snapshot! : delta;
        }

        private void Track(string itemId)
        {
            if (!_order.Contains(itemId, StringComparer.Ordinal))
                _order.Add(itemId);
        }
    }
}
