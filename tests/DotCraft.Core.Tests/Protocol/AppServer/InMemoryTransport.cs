using System.Text.Json;
using System.Threading.Channels;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

/// <summary>
/// In-process implementation of <see cref="IAppServerTransport"/> for xUnit tests.
/// Replaces stdio with in-memory channels so the full AppServer protocol stack
/// (handler, dispatcher, connection) can be exercised without a real process.
///
/// Inbound: tests call <see cref="InjectFromClient"/> to push messages the server will read.
/// Outbound: server writes via <see cref="WriteMessageAsync"/>; tests drain with
/// <see cref="ReadNextSentAsync"/> or <see cref="TryReadSent"/>.
/// </summary>
internal sealed class InMemoryTransport : IAppServerTransport
{
    // Messages injected by the test (simulates the client writing to server stdin).
    private readonly Channel<AppServerIncomingMessage> _inbound =
        Channel.CreateUnbounded<AppServerIncomingMessage>();

    // Messages written by the server (simulates server writing to client stdout).
    private readonly Channel<string> _outbound =
        Channel.CreateUnbounded<string>();

    private int _nextClientRequestId;

    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    // -------------------------------------------------------------------------
    // Test helpers (used by test code, not by the server)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Injects a message that the server will read on the next <see cref="ReadMessageAsync"/> call.
    /// </summary>
    public void InjectFromClient(AppServerIncomingMessage msg) =>
        _inbound.Writer.TryWrite(msg);

    /// <summary>
    /// Reads the next message that the server sent, waiting up to <paramref name="timeout"/>.
    /// Throws <see cref="OperationCanceledException"/> if the timeout elapses.
    /// </summary>
    public async Task<JsonDocument> ReadNextSentAsync(TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));
        var json = await _outbound.Reader.ReadAsync(cts.Token);
        return JsonDocument.Parse(json);
    }

    /// <summary>
    /// Reads a message the server sent without waiting. Returns null if none is available.
    /// </summary>
    public JsonDocument? TryReadSent()
    {
        if (_outbound.Reader.TryRead(out var json))
            return JsonDocument.Parse(json);
        return null;
    }

    /// <summary>
    /// Drains all currently buffered outbound messages (non-blocking).
    /// </summary>
    public List<JsonDocument> DrainSent()
    {
        var result = new List<JsonDocument>();
        while (_outbound.Reader.TryRead(out var json))
            result.Add(JsonDocument.Parse(json));
        return result;
    }

    /// <summary>
    /// Waits until at least <paramref name="count"/> messages have been captured,
    /// then drains all available messages.
    /// </summary>
    public async Task<List<JsonDocument>> WaitAndDrainAsync(int count, TimeSpan? timeout = null)
    {
        var msgs = new List<JsonDocument>();
        for (var i = 0; i < count; i++)
            msgs.Add(await ReadNextSentAsync(timeout));
        // Drain any additional messages that may have arrived
        msgs.AddRange(DrainSent());
        return msgs;
    }

    /// <summary>
    /// Optional override for server-initiated approval requests (item/approval/request).
    /// When null, the transport auto-accepts all approval requests.
    /// Signature: (method, @params) → AppServerIncomingMessage response
    /// </summary>
    public Func<string, object?, AppServerIncomingMessage>? ApprovalHandler { get; set; }

    // -------------------------------------------------------------------------
    // IAppServerTransport
    // -------------------------------------------------------------------------

    public async Task<AppServerIncomingMessage?> ReadMessageAsync(CancellationToken ct = default)
    {
        try
        {
            return await _inbound.Reader.ReadAsync(ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (ChannelClosedException) { return null; }
    }

    public async Task WriteMessageAsync(object message, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(message, SessionWireJsonOptions.Default);
        await _outbound.Writer.WriteAsync(json, ct);
    }

    public async Task<AppServerIncomingMessage> SendClientRequestAsync(
        string method,
        object? @params,
        CancellationToken ct = default,
        TimeSpan? timeout = null)
    {
        var id = Interlocked.Increment(ref _nextClientRequestId);

        // Write the approval request to outbound so tests can observe it
        await WriteMessageAsync(new { jsonrpc = "2.0", id, method, @params }, ct);

        if (ApprovalHandler != null)
            return ApprovalHandler(method, @params);

        // Default: auto-accept with decision = "accept"
        return BuildApprovalResponse(id, "accept");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static AppServerIncomingMessage BuildApprovalResponse(int id, string decision)
    {
        var json = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id,
            result = new { decision }
        }, SessionWireJsonOptions.Default);
        return JsonSerializer.Deserialize<AppServerIncomingMessage>(json, CamelCaseOptions)!;
    }

    public static AppServerIncomingMessage BuildRequest(string method, object? @params = null, int id = 1)
    {
        var json = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params
        }, SessionWireJsonOptions.Default);
        return JsonSerializer.Deserialize<AppServerIncomingMessage>(json, CamelCaseOptions)!;
    }

    /// <summary>
    /// Builds an <see cref="AppServerIncomingMessage"/> representing a JSON-RPC response.
    /// Used by <see cref="ApprovalHandler"/> to return a client approval decision.
    /// </summary>
    public static AppServerIncomingMessage BuildClientResponse(int id, object? result)
    {
        var json = JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }, SessionWireJsonOptions.Default);
        return JsonSerializer.Deserialize<AppServerIncomingMessage>(json, CamelCaseOptions)!;
    }

    public static AppServerIncomingMessage BuildNotification(string method, object? @params = null)
    {
        var json = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method,
            @params
        }, SessionWireJsonOptions.Default);
        return JsonSerializer.Deserialize<AppServerIncomingMessage>(json, CamelCaseOptions)!;
    }

    // -------------------------------------------------------------------------
    // IAsyncDisposable
    // -------------------------------------------------------------------------

    public ValueTask DisposeAsync()
    {
        _inbound.Writer.TryComplete();
        _outbound.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
