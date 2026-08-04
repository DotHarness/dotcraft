using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.Sessions.Wire;
using ModelPreference = DotCraft.Configuration.ModelPreference;

namespace DotCraft.AppServer;

/// <summary>
/// JSON-RPC 2.0 client for the DotCraft AppServer stdio protocol.
/// Communicates over a pair of <see cref="Stream"/> objects (stdin/stdout of a subprocess,
/// in-memory pipes, or any other byte stream), implementing the full Session Wire Protocol.
///
/// Server-initiated requests (e.g. <c>item/approval/request</c>) are dispatched through
/// <see cref="ServerRequestHandler"/> when set; otherwise they are placed in the notification
/// queue and can be retrieved via <see cref="WaitForNotificationAsync"/>.
/// </summary>
public sealed class AppServerWireClient(Stream input, Stream output) : IAsyncDisposable
{
    private readonly StreamReader _reader = new(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
    private readonly StreamWriter _writer = new(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
    {
        AutoFlush = true
    };
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonDocument>> _pending = new();
    private readonly Channel<JsonDocument> _notifications = Channel.CreateUnbounded<JsonDocument>();
    private readonly Channel<JsonDocument> _jobResultNotifications = Channel.CreateUnbounded<JsonDocument>();
    /// <summary>
    /// Per-thread notification queues when <see cref="RegisterThreadChannel"/> is active.
    /// Routes turn/item notifications so concurrent sessions do not steal from a shared channel.
    /// </summary>
    private readonly ConcurrentDictionary<string, Channel<JsonDocument>> _threadChannels = new();

    private Task? _readerTask;
    private readonly CancellationTokenSource _disposeCts = new();
    private int _nextId;
    private bool _disposed;

    /// <summary>
    /// Optional handler for server-initiated JSON-RPC requests (messages with both
    /// <c>method</c> and <c>id</c>). Receives the full message document and returns
    /// a result object to be sent back as the response.
    ///
    /// When null (default), server requests are placed in the notification queue.
    /// </summary>
    public Func<JsonDocument, Task<object?>>? ServerRequestHandler { get; set; }

    /// <summary>
    /// Starts the background reader loop. Must be called before sending any requests.
    /// </summary>
    public void Start() => _readerTask = Task.Run(ReaderLoopAsync);

    // -------------------------------------------------------------------------
    // Protocol helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Performs the full <c>initialize</c> → <c>initialized</c> handshake and
    /// returns the server's initialize response document.
    /// </summary>
    public async Task<JsonDocument> InitializeAsync(
        string clientName = "dotcraft-cli",
        string clientVersion = "0.1.0",
        bool approvalSupport = true,
        bool streamingSupport = true,
        bool toolExecutionLifecycle = false,
        IReadOnlyList<string>? optOutMethods = null,
        Contract.AcpExtensionCapability? acpExtensions = null)
    {
        var capabilities = new Contract.ClientCapabilities
        {
            ApprovalSupport = approvalSupport,
            StreamingSupport = streamingSupport,
            ToolExecutionLifecycle = toolExecutionLifecycle,
            OptOutNotificationMethods = [.. optOutMethods ?? []],
            AcpExtensions = acpExtensions
        };

        var result = await SendRequestAsync(Protocol.AppServer.AppServerMethodNames.Initialize, new Contract.InitializeParams
        {
            ClientInfo = new Contract.ClientInfo { Name = clientName, Version = clientVersion },
            Capabilities = capabilities
        });
        await SendNotificationAsync(Protocol.AppServer.AppServerMethodNames.Initialized);
        return result;
    }

    /// <summary>
    /// Reads all JSON-RPC notifications until a terminal turn event is received
    /// (<c>turn/completed</c>, <c>turn/failed</c>, or <c>turn/cancelled</c>).
    /// </summary>
    public async IAsyncEnumerable<JsonDocument> ReadTurnNotificationsAsync(
        TimeSpan? timeout = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromMinutes(5));

        while (!ct.IsCancellationRequested && DateTime.UtcNow < deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) yield break;

            JsonDocument? notif;
            try { notif = await WaitForNotificationAsync(null, remaining, ct); }
            catch (OperationCanceledException) { yield break; }

            if (notif == null) yield break;

            yield return notif;

            if (notif.RootElement.TryGetProperty("method", out var m))
            {
                var method = m.GetString();
                if (method is Protocol.AppServer.AppServerMethodNames.TurnCompleted or Protocol.AppServer.AppServerMethodNames.TurnFailed or Protocol.AppServer.AppServerMethodNames.TurnCancelled)
                    yield break;
            }
        }
    }

    /// <summary>
    /// Reads JSON-RPC notifications for a single thread until a terminal turn event
    /// (<c>turn/completed</c>, <c>turn/failed</c>, or <c>turn/cancelled</c>).
    /// Requires <see cref="RegisterThreadChannel"/> to be called for <paramref name="threadId"/>
    /// before the turn starts.
    /// </summary>
    public async IAsyncEnumerable<JsonDocument> ReadThreadTurnNotificationsAsync(
        string threadId,
        TimeSpan? timeout = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromMinutes(5));

        while (!ct.IsCancellationRequested && DateTime.UtcNow < deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) yield break;

            JsonDocument? notif;
            try { notif = await WaitForThreadNotificationAsync(threadId, null, remaining, ct); }
            catch (OperationCanceledException) { yield break; }

            if (notif == null) yield break;

            yield return notif;

            if (notif.RootElement.TryGetProperty("method", out var m))
            {
                var method = m.GetString();
                if (method is Protocol.AppServer.AppServerMethodNames.TurnCompleted or Protocol.AppServer.AppServerMethodNames.TurnFailed or Protocol.AppServer.AppServerMethodNames.TurnCancelled)
                    yield break;
            }
        }
    }

    /// <summary>
    /// Registers a dedicated notification channel for <paramref name="threadId"/>.
    /// The reader loop routes matching notifications here instead of the global queue.
    /// </summary>
    /// <exception cref="InvalidOperationException">A channel is already registered for this thread.</exception>
    public void RegisterThreadChannel(string threadId)
    {
        ArgumentException.ThrowIfNullOrEmpty(threadId);
        var channel = Channel.CreateUnbounded<JsonDocument>();
        if (!_threadChannels.TryAdd(threadId, channel))
        {
            channel.Writer.TryComplete();
            throw new InvalidOperationException($"A thread notification channel is already registered for '{threadId}'.");
        }
    }

    /// <summary>
    /// Removes the per-thread channel and completes its writer so readers stop.
    /// </summary>
    public void UnregisterThreadChannel(string threadId)
    {
        if (string.IsNullOrEmpty(threadId)) return;
        if (_threadChannels.TryRemove(threadId, out var channel))
            channel.Writer.TryComplete();
    }

    /// <summary>
    /// Extracts <c>threadId</c> from notification params (<c>params.threadId</c> or <c>params.turn.threadId</c>).
    /// </summary>
    private static string? ExtractThreadId(JsonDocument doc)
    {
        if (!doc.RootElement.TryGetProperty("params", out var p))
            return null;
        if (p.TryGetProperty("threadId", out var tid) && tid.ValueKind == JsonValueKind.String)
            return tid.GetString();
        if (p.TryGetProperty("turn", out var turn) && turn.TryGetProperty("threadId", out var ttid) &&
            ttid.ValueKind == JsonValueKind.String)
            return ttid.GetString();
        return null;
    }

    // -------------------------------------------------------------------------
    // Model catalog management (model/list)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Lists provider models from the server's configured provider endpoint.
    /// Requires the server to advertise <c>modelCatalogManagement</c> capability.
    /// </summary>
    public Task<Contract.ModelListResult> ModelListAsync(CancellationToken ct = default) =>
        ModelListAsync(providerId: null, ct);

    /// <summary>
    /// Lists models for a specific provider id, or the workspace-selected provider when omitted.
    /// </summary>
    public async Task<Contract.ModelListResult> ModelListAsync(string? providerId, CancellationToken ct = default)
    {
        var doc = await SendRequestAsync(
            Protocol.AppServer.AppServerMethodNames.ModelList,
            new Contract.ModelListParams { ProviderId = providerId },
            ct: ct);

        ThrowIfError(doc, "model/list");

        var result = doc.RootElement.GetProperty("result");
        return JsonSerializer.Deserialize<Contract.ModelListResult>(result.GetRawText(), Protocol.AppServerContractJson.Options)
               ?? new Contract.ModelListResult
               {
                   Success = false,
                   ErrorCode = "Unknown",
                   ErrorMessage = "Server returned an empty model list payload."
               };
    }

    // -------------------------------------------------------------------------
    // Provider management
    // -------------------------------------------------------------------------

    /// <summary>
    /// Lists configured model providers.
    /// </summary>
    public async Task<IReadOnlyList<Contract.ProviderInfo>> ProviderListAsync(CancellationToken ct = default)
    {
        var doc = await SendRequestAsync(
            Protocol.AppServer.AppServerMethodNames.ProviderList,
            new Contract.ProviderListParams(),
            ct: ct);

        ThrowIfError(doc, "provider/list");

        var result = doc.RootElement.GetProperty("result");
        var response = JsonSerializer.Deserialize<Contract.ProviderListResult>(
            result.GetRawText(), Protocol.AppServerContractJson.Options);
        return response?.Providers is { IsSet: true } providers ? providers.Value ?? [] : [];
    }

    /// <summary>
    /// Creates a personal model provider entry.
    /// </summary>
    public async Task<Contract.ProviderInfo> ProviderCreateAsync(Contract.ProviderCreateParams provider, CancellationToken ct = default)
    {
        var doc = await SendRequestAsync(
            Protocol.AppServer.AppServerMethodNames.ProviderCreate,
            provider,
            ct: ct);

        ThrowIfError(doc, "provider/create");

        var result = doc.RootElement.GetProperty("result");
        var response = JsonSerializer.Deserialize<Contract.ProviderMutationResult>(
            result.GetRawText(), Protocol.AppServerContractJson.Options);
        return response?.Provider is { IsSet: true } created ? created.Value ?? new Contract.ProviderInfo() : new Contract.ProviderInfo();
    }

    /// <summary>
    /// Updates a personal model provider entry.
    /// </summary>
    public async Task<Contract.ProviderInfo> ProviderUpdateAsync(Contract.ProviderUpdateParams provider, CancellationToken ct = default)
    {
        var doc = await SendRequestAsync(
            Protocol.AppServer.AppServerMethodNames.ProviderUpdate,
            provider,
            ct: ct);

        ThrowIfError(doc, "provider/update");

        var result = doc.RootElement.GetProperty("result");
        var response = JsonSerializer.Deserialize<Contract.ProviderMutationResult>(
            result.GetRawText(), Protocol.AppServerContractJson.Options);
        return response?.Provider is { IsSet: true } updated ? updated.Value ?? new Contract.ProviderInfo() : new Contract.ProviderInfo();
    }

    /// <summary>
    /// Deletes a personal model provider entry.
    /// </summary>
    public async Task<bool> ProviderDeleteAsync(string id, CancellationToken ct = default)
    {
        var doc = await SendRequestAsync(
            Protocol.AppServer.AppServerMethodNames.ProviderDelete,
            new Contract.ProviderDeleteParams { Id = id },
            ct: ct);

        ThrowIfError(doc, "provider/delete");

        var result = doc.RootElement.GetProperty("result");
        var response = JsonSerializer.Deserialize<Contract.ProviderDeleteResult>(
            result.GetRawText(), Protocol.AppServerContractJson.Options);
        return response?.Deleted is { IsSet: true } deleted && deleted.Value;
    }

    /// <summary>
    /// Tests a persisted or draft personal model provider through the provider-neutral probe contract.
    /// </summary>
    public async Task<Contract.ProviderTestResult> ProviderTestAsync(Contract.ProviderTestParams provider, CancellationToken ct = default)
    {
        var doc = await SendRequestAsync(
            Protocol.AppServer.AppServerMethodNames.ProviderTest,
            provider,
            ct: ct);

        ThrowIfError(doc, "provider/test");

        var result = doc.RootElement.GetProperty("result");
        return JsonSerializer.Deserialize<Contract.ProviderTestResult>(result.GetRawText(), Protocol.AppServerContractJson.Options)
               ?? new Contract.ProviderTestResult
               {
                   Success = false,
                   ErrorCode = "Unknown",
                   ErrorMessage = "Server returned an empty provider test payload."
               };
    }

    /// <summary>
    /// Updates workspace provider and provider-specific MainAgent model preferences.
    /// Null values are sent as explicit removals.
    /// </summary>
    public Task<Contract.WorkspaceConfigUpdateResult> WorkspaceConfigUpdateAsync(
        string? providerId,
        IReadOnlyDictionary<string, ModelPreference>? providerPreferences,
        CancellationToken ct = default)
    {
        var payload = new JsonObject
        {
            ["providerId"] = providerId == null ? null : JsonValue.Create(providerId),
            ["providerPreferences"] = providerPreferences == null
                ? null
                : JsonSerializer.SerializeToNode(providerPreferences, SessionWireJsonOptions.Default)
        };
        return WorkspaceConfigUpdateAsync(payload, ct);
    }

    /// <summary>
    /// Updates workspace config using an explicit JSON object payload.
    /// Include a property with a null value when the server should remove that setting.
    /// </summary>
    public async Task<Contract.WorkspaceConfigUpdateResult> WorkspaceConfigUpdateAsync(JsonObject payload, CancellationToken ct = default)
    {
        var doc = await SendRequestAsync(
            Protocol.AppServer.AppServerMethodNames.WorkspaceConfigUpdate,
            payload,
            ct: ct);

        ThrowIfError(doc, "workspace/config/update");

        var result = doc.RootElement.GetProperty("result");
        return JsonSerializer.Deserialize<Contract.WorkspaceConfigUpdateResult>(
                   result.GetRawText(),
                   Protocol.AppServerContractJson.Options)
               ?? new Contract.WorkspaceConfigUpdateResult();
    }

    // -------------------------------------------------------------------------
    // Cron management (spec Section 16)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Lists cron jobs from the AppServer's in-memory CronService.
    /// Requires the server to advertise <c>cronManagement</c> capability.
    /// Throws <see cref="Exception"/> on wire errors or if the server returns a JSON-RPC error.
    /// </summary>
    public async Task<List<Contract.CronJobWireInfo>> CronListAsync(
        bool includeDisabled = false,
        CancellationToken ct = default)
    {
        var doc = await SendRequestAsync(
            Protocol.AppServer.AppServerMethodNames.CronList,
            new Contract.CronListParams { IncludeDisabled = includeDisabled },
            ct: ct);

        ThrowIfError(doc, "cron/list");

        var result = doc.RootElement.GetProperty("result");
        var response = JsonSerializer.Deserialize<Contract.CronListResult>(
            result.GetRawText(),
            Protocol.AppServerContractJson.Options);
        return response is { Jobs.IsSet: true }
            ? response.Jobs.Value?.ToList() ?? []
            : [];
    }

    /// <summary>
    /// Removes a cron job from the AppServer's in-memory CronService.
    /// Throws <see cref="Exception"/> if the job is not found or a wire error occurs.
    /// </summary>
    public async Task CronRemoveAsync(string jobId, CancellationToken ct = default)
    {
        var doc = await SendRequestAsync(
            Protocol.AppServer.AppServerMethodNames.CronRemove,
            new Contract.CronRemoveParams { JobId = jobId },
            ct: ct);

        ThrowIfError(doc, jobId);
    }

    /// <summary>
    /// Enables or disables a cron job on the AppServer.
    /// Throws <see cref="Exception"/> if the job is not found or a wire error occurs.
    /// </summary>
    public async Task<Contract.CronJobWireInfo> CronEnableAsync(
        string jobId,
        bool enabled,
        CancellationToken ct = default)
    {
        var doc = await SendRequestAsync(
            Protocol.AppServer.AppServerMethodNames.CronEnable,
            new Contract.CronEnableParams { JobId = jobId, Enabled = enabled },
            ct: ct);

        ThrowIfError(doc, jobId);

        var result = doc.RootElement.GetProperty("result");
        var response = JsonSerializer.Deserialize<Contract.CronEnableResult>(
            result.GetRawText(),
            Protocol.AppServerContractJson.Options);
        return response is { Job.IsSet: true } && response.Job.Value is { } job
            ? job
            : throw new InvalidOperationException($"Server returned empty job for '{jobId}'.");
    }

    /// <summary>
    /// Triggers an immediate heartbeat run on the server (spec Section 17.2).
    /// Uses a 120-second timeout because the heartbeat runs the full agent pipeline.
    /// </summary>
    public async Task<Contract.HeartbeatTriggerResult> HeartbeatTriggerAsync(CancellationToken ct = default)
    {
        var doc = await SendRequestAsync(
            Protocol.AppServer.AppServerMethodNames.HeartbeatTrigger,
            new Protocol.RpcEmpty(),
            timeout: TimeSpan.FromSeconds(120),
            ct: ct);

        ThrowIfError(doc, "heartbeat/trigger");

        var result = doc.RootElement.GetProperty("result");
        return JsonSerializer.Deserialize<Contract.HeartbeatTriggerResult>(
            result.GetRawText(), Protocol.AppServerContractJson.Options)
               ?? new Contract.HeartbeatTriggerResult();
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if the JSON-RPC document contains an
    /// error field, using the error message from the server response.
    /// </summary>
    private static void ThrowIfError(JsonDocument doc, string context)
    {
        if (doc.RootElement.TryGetProperty("error", out var error))
        {
            var message = error.TryGetProperty("message", out var msg)
                ? msg.GetString() ?? "Unknown error"
                : "Unknown error";
            throw new InvalidOperationException(message);
        }
    }

    // -------------------------------------------------------------------------
    // Core JSON-RPC primitives
    // -------------------------------------------------------------------------

    /// <summary>Sends a JSON-RPC request and awaits the server response.</summary>
    public async Task<JsonDocument> SendRequestAsync(
        string method,
        object? @params = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonDocument>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        try
        {
            var request = JsonSerializer.Serialize(
                new { jsonrpc = "2.0", id, method, @params },
                SessionWireJsonOptions.Default);
            await WriteLineAsync(request);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token, ct);
            cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(30));
            await using var reg = cts.Token.Register(() => tcs.TrySetCanceled(cts.Token));

            return await tcs.Task;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    /// <summary>Sends a JSON-RPC notification (no id, no response expected).</summary>
    public async Task SendNotificationAsync(string method, object? @params = null)
    {
        var notification = JsonSerializer.Serialize(
            new { jsonrpc = "2.0", method, @params },
            SessionWireJsonOptions.Default);
        await WriteLineAsync(notification);
    }

    /// <summary>
    /// Waits for the next notification matching <paramref name="method"/> (null = any).
    /// Returns null on timeout or cancellation.
    /// </summary>
    public async Task<JsonDocument?> WaitForNotificationAsync(
        string? method = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));

        while (!ct.IsCancellationRequested && DateTime.UtcNow < deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) break;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
            cts.CancelAfter(remaining);

            JsonDocument notif;
            try { notif = await _notifications.Reader.ReadAsync(cts.Token); }
            catch (OperationCanceledException) { break; }
            catch (ChannelClosedException) { break; }

            if (method == null) return notif;

            if (notif.RootElement.TryGetProperty("method", out var m) && m.GetString() == method)
                return notif;

            // Not the requested method — re-enqueue and continue waiting
            _notifications.Writer.TryWrite(notif);
        }

        return null;
    }

    /// <summary>
    /// Waits for the next notification on the per-thread channel for <paramref name="threadId"/>.
    /// When <paramref name="method"/> is non-null, non-matching notifications are re-queued to the same channel.
    /// Returns null if the channel is not registered, on timeout, or cancellation.
    /// </summary>
    public async Task<JsonDocument?> WaitForThreadNotificationAsync(
        string threadId,
        string? method = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(threadId);
        if (!_threadChannels.TryGetValue(threadId, out var ch))
            return null;

        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));

        while (!ct.IsCancellationRequested && DateTime.UtcNow < deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) break;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
            cts.CancelAfter(remaining);

            JsonDocument notif;
            try { notif = await ch.Reader.ReadAsync(cts.Token); }
            catch (OperationCanceledException) { break; }
            catch (ChannelClosedException) { break; }

            if (method == null) return notif;

            if (notif.RootElement.TryGetProperty("method", out var m) && m.GetString() == method)
                return notif;

            ch.Writer.TryWrite(notif);
        }

        return null;
    }

    /// <summary>
    /// Waits for the next <c>system/jobResult</c> notification from the dedicated channel.
    /// Returns null on timeout or cancellation.
    /// </summary>
    public async Task<JsonDocument?> WaitForJobResultAsync(
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));

        while (!ct.IsCancellationRequested && DateTime.UtcNow < deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) break;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
            cts.CancelAfter(remaining);

            try { return await _jobResultNotifications.Reader.ReadAsync(cts.Token); }
            catch (OperationCanceledException) { break; }
            catch (ChannelClosedException) { break; }
        }

        return null;
    }

    /// <summary>
    /// Sends a JSON-RPC response to a server-initiated request.
    /// Used by the background reader loop when <see cref="ServerRequestHandler"/> is set.
    /// </summary>
    public async Task SendResponseAsync(int requestId, object? result)
    {
        var response = JsonSerializer.Serialize(
            new { jsonrpc = "2.0", id = requestId, result },
            SessionWireJsonOptions.Default);
        await WriteLineAsync(response);
    }

    // -------------------------------------------------------------------------
    // Background reader loop
    // -------------------------------------------------------------------------

    private async Task ReaderLoopAsync()
    {
        var ct = _disposeCts.Token;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                string? line;
                try { line = await _reader.ReadLineAsync(ct); }
                catch (OperationCanceledException) { break; }

                if (line == null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                JsonDocument doc;
                try { doc = JsonDocument.Parse(line); }
                catch (JsonException) { continue; }

                var root = doc.RootElement;
                var hasMethod = root.TryGetProperty("method", out var methodEl);
                var hasId = root.TryGetProperty("id", out var idEl) &&
                            idEl.ValueKind != JsonValueKind.Null &&
                            idEl.ValueKind != JsonValueKind.Undefined;

                // Response to a pending client request
                if (!hasMethod && hasId && idEl.ValueKind == JsonValueKind.Number)
                {
                    if (_pending.TryGetValue(idEl.GetInt32(), out var tcs))
                    {
                        tcs.TrySetResult(doc);
                        continue;
                    }
                }

                // Server-initiated request (has both method and numeric id)
                if (hasMethod && hasId && idEl.ValueKind == JsonValueKind.Number)
                {
                    var handler = ServerRequestHandler;
                    if (handler != null)
                    {
                        var requestDoc = doc;
                        var reqId = idEl.GetInt32();
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var result = await handler(requestDoc);
                                await SendResponseAsync(reqId, result);
                            }
                            catch { /* failures silently suppressed; caller should handle internally */ }
                        }, ct);
                        continue;
                    }
                }

                // system/jobResult → dedicated channel to avoid being consumed during a turn
                if (hasMethod && methodEl.GetString() == Protocol.AppServer.AppServerMethodNames.SystemJobResult)
                {
                    _jobResultNotifications.Writer.TryWrite(doc);
                    continue;
                }

                // Per-thread queue when a session registered before turn/start
                var routedThreadId = ExtractThreadId(doc);
                if (!string.IsNullOrEmpty(routedThreadId) &&
                    _threadChannels.TryGetValue(routedThreadId, out var threadChannel))
                {
                    threadChannel.Writer.TryWrite(doc);
                    continue;
                }

                // Notification or unhandled server request → notification queue
                _notifications.Writer.TryWrite(doc);
            }
        }
        catch (Exception) { /* reader loop terminated */ }
        finally
        {
            _notifications.Writer.TryComplete();
            _jobResultNotifications.Writer.TryComplete();
            foreach (var kv in _threadChannels)
                kv.Value.Writer.TryComplete();
            foreach (var tcs in _pending.Values)
                tcs.TrySetCanceled();
        }
    }

    private async Task WriteLineAsync(string line)
    {
        await _writeLock.WaitAsync(_disposeCts.Token);
        try { await _writer.WriteLineAsync(line.AsMemory(), _disposeCts.Token); }
        finally { _writeLock.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await _disposeCts.CancelAsync();
        _reader.Dispose();
        await _writeLock.WaitAsync();
        try
        {
            await _writer.DisposeAsync();
        }
        finally
        {
            _writeLock.Release();
        }
        if (_readerTask != null)
        {
            try
            {
                await _readerTask;
            }
            catch
            {
                // ignored
            }
        }

        _disposeCts.Dispose();
        _writeLock.Dispose();
    }
}
