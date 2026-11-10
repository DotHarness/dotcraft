using System.Text.Json;
using System.Threading.Channels;
using DotCraft.AppServer;
using DotCraft.Sessions.Wire;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class InMemoryTransport : IAppServerTransport
{
    private readonly Channel<AppServerIncomingMessage> _inbound =
        Channel.CreateUnbounded<AppServerIncomingMessage>();

    private readonly Channel<string> _outbound =
        Channel.CreateUnbounded<string>();

    private int _nextClientRequestId;
    private readonly CancellationTokenSource _lifetime = new();

    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };


    public void InjectFromClient(AppServerIncomingMessage msg) =>
        _inbound.Writer.TryWrite(msg);

    public async Task<JsonDocument> ReadNextSentAsync(TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));
        var json = await _outbound.Reader.ReadAsync(cts.Token);
        return JsonDocument.Parse(json);
    }

    public JsonDocument? TryReadSent()
    {
        if (_outbound.Reader.TryRead(out var json))
            return JsonDocument.Parse(json);
        return null;
    }

    public List<JsonDocument> DrainSent()
    {
        var result = new List<JsonDocument>();
        while (_outbound.Reader.TryRead(out var json))
            result.Add(JsonDocument.Parse(json));
        return result;
    }

    public async Task<List<JsonDocument>> WaitAndDrainAsync(int count, TimeSpan? timeout = null)
    {
        var msgs = new List<JsonDocument>();
        for (var i = 0; i < count; i++)
            msgs.Add(await ReadNextSentAsync(timeout));
        msgs.AddRange(DrainSent());
        return msgs;
    }

    public async Task<List<JsonDocument>> ReadTurnMessagesAsync(int count, TimeSpan? timeout = null)
    {
        var messages = new List<JsonDocument>();
        while (messages.Count < count)
        {
            var message = await ReadNextSentAsync(timeout);
            if (!message.RootElement.TryGetProperty("method", out var method)
                || method.GetString() is { } name && (name.StartsWith("turn/", StringComparison.Ordinal)
                    || name.StartsWith("item/", StringComparison.Ordinal)
                    || name == DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentProgress))
                messages.Add(message);
            else
                message.Dispose();
        }
        return messages;
    }

    public Func<string, object?, AppServerIncomingMessage>? ApprovalHandler { get; set; }

    public Func<string, object?, Task<AppServerIncomingMessage>>? ApprovalHandlerAsync { get; set; }


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

    public async Task<AppServerPendingClientRequest> StartClientRequestAsync(
        string method,
        object? @params,
        CancellationToken ct = default,
        TimeSpan? timeout = null)
    {
        var id = Interlocked.Increment(ref _nextClientRequestId);
        await WriteMessageAsync(new { jsonrpc = "2.0", id, method, @params }, ct);
        return new AppServerPendingClientRequest(id, AwaitResponseAsync(id, method, @params, ct, timeout));
    }

    private async Task<AppServerIncomingMessage> AwaitResponseAsync(
        int id,
        string method,
        object? parameters,
        CancellationToken ct,
        TimeSpan? timeout)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        cancellation.CancelAfter(timeout ?? TimeSpan.FromSeconds(120));
        if (ApprovalHandlerAsync != null)
            return await ApprovalHandlerAsync(method, parameters).WaitAsync(cancellation.Token);
        cancellation.Token.ThrowIfCancellationRequested();
        return ApprovalHandler?.Invoke(method, parameters) ?? BuildApprovalResponse(id, "accept");
    }


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


    public ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        _inbound.Writer.TryComplete();
        _outbound.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
