using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using DotCraft.CodexGateway.Protocol;
using DotCraft.CodexGateway.Workspace;
using DotCraft.Sdk;

namespace DotCraft.CodexGateway.Tests;

internal sealed class RealGatewayPeer : IAsyncDisposable
{
    private readonly CodexGatewaySession _session;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _responses = new();
    private readonly Channel<JsonElement> _notifications = Channel.CreateUnbounded<JsonElement>();
    private readonly Channel<JsonElement> _requests = Channel.CreateUnbounded<JsonElement>();
    private int _nextId;

    public RealGatewayPeer(string stateDirectory, params RealWorkspaceAppServer[] sources)
    {
        Registrations = sources.Select(source => new CodexWorkspaceRegistration(
            source.Root, Path.GetFileName(source.Root), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)).ToList();
        Catalog = new CodexWorkspaceCatalog(() => Registrations.ToArray(), stateDirectory);
        _session = new CodexGatewaySession(new() { StateDirectory = stateDirectory }, Catalog,
            () => new("fixture", "gateway", null, "connected"), SendAsync,
            connectWorkspace: async (workspace, send, broker, ct) =>
            {
                SourceConnections++;
                var bridge = new CodexInteractiveBridge(WorkspaceRoutes.Open(stateDirectory, workspace.Path),
                    workspace.Path, broker, send);
                var source = sources.Single(source => source.Root == workspace.Path);
                var client = await source.ConnectAsync(new DotCraftRemoteOptions
                {
                    ClientName = "gateway-fixture", AutoReconnect = false, ApprovalSupport = true,
                    RequestUserInputSupport = true, ApprovalHandler = bridge.HandleApprovalAsync,
                    UserInputHandler = bridge.HandleUserInputAsync,
                    ExtraCapabilities = new Dictionary<string, object?>
                    {
                        ["remoteHost"] = true, ["commandExecutionStreaming"] = true, ["toolExecutionLifecycle"] = true
                    }
                }, ct);
                return new CodexWorkspaceSession(client, workspace.Path, stateDirectory, send, bridge);
            });
    }

    public CodexWorkspaceCatalog Catalog { get; }
    public List<CodexWorkspaceRegistration> Registrations { get; }
    public int SourceConnections { get; private set; }
    public ConcurrentQueue<(string Method, string ItemId, string? Phase, string? Delivery)> AssistantEvents { get; } = new();

    private Task SendAsync(JsonElement message, CancellationToken ct)
    {
        if (message.TryGetProperty("method", out var method))
        {
            if (method.GetString() is "item/started" or "item/completed")
            {
                var item = message.GetProperty("params").GetProperty("item");
                if (item.GetProperty("type").GetString() == "agentMessage")
                    AssistantEvents.Enqueue((method.GetString()!, item.GetProperty("id").GetString()!,
                        item.GetProperty("phase").GetString(), item.GetProperty("delivery").GetString()));
            }
            if (message.TryGetProperty("id", out _)) _requests.Writer.TryWrite(message.Clone());
            else _notifications.Writer.TryWrite(message.Clone());
        }
        else if (message.TryGetProperty("id", out var id) && _responses.TryRemove(id.GetInt32(), out var response))
        {
            response.TrySetResult(message.Clone());
        }
        return Task.CompletedTask;
    }

    public async Task<JsonElement> RequestAsync(string method, object parameters, CancellationToken ct)
    {
        var id = Interlocked.Increment(ref _nextId);
        var response = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _responses[id] = response;
        await _session.ReceiveAsync(JsonSerializer.SerializeToElement(new { id, method, @params = parameters }), ct);
        var result = await response.Task.WaitAsync(ct);
        if (result.TryGetProperty("error", out var error))
            throw new InvalidOperationException(method + ": " + error.GetRawText());
        return result.GetProperty("result").Clone();
    }

    public Task AnswerAsync(JsonElement request, object result, CancellationToken ct) =>
        _session.ReceiveAsync(JsonSerializer.SerializeToElement(new { id = request.GetProperty("id"), result }), ct);

    public async Task<JsonElement> NextRequestAsync(string method, CancellationToken ct)
    {
        while (true)
        {
            var message = await _requests.Reader.ReadAsync(ct);
            if (message.GetProperty("method").GetString() == method) return message;
        }
    }

    public async Task<JsonElement> NextNotificationAsync(string method, string thread, CancellationToken ct)
    {
        while (true)
        {
            var message = await _notifications.Reader.ReadAsync(ct);
            if (message.GetProperty("method").GetString() == method &&
                message.GetProperty("params").GetProperty("threadId").GetString() == thread) return message.GetProperty("params").Clone();
        }
    }

    public ValueTask DisposeAsync() => _session.DisposeAsync();
}
