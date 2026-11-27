using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using DotCraft.Sessions;
using DotCraft.Tools;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DotCraft.Mcp;

internal sealed class McpInvocationScope : IDisposable
{
    internal sealed record Invocation(
        ToolInvocationContext Context,
        Task ClientCompletion,
        CancellationToken CallCancellation);

    internal sealed class ActiveInvocationTable
    {
        private readonly ConcurrentDictionary<string, Invocation> _invocations = new(StringComparer.Ordinal);

        public void Register(string callId, Invocation invocation) =>
            _invocations[callId] = invocation;

        public void Unregister(string callId, Invocation invocation) =>
            _invocations.TryRemove(new KeyValuePair<string, Invocation>(callId, invocation));

        public bool TryGet(string callId, out Invocation? invocation) =>
            _invocations.TryGetValue(callId, out invocation);
    }

    private static readonly AsyncLocal<Invocation?> Current = new();
    private static readonly ConditionalWeakTable<McpClient, ActiveInvocationTable> ClientTables = new();

    private readonly Invocation? _previous;
    private readonly ActiveInvocationTable? _table;
    private readonly string? _callId;
    private readonly Invocation? _current;

    public McpInvocationScope(
        ToolInvocationContext? context,
        McpClient? client,
        CancellationToken callCancellation = default)
    {
        _previous = Current.Value;
        if (context is null)
        {
            Current.Value = null;
            return;
        }

        var invocation = new Invocation(context, client?.Completion ?? Task.CompletedTask, callCancellation);
        _current = invocation;
        Current.Value = invocation;

        if (client is not null
            && !string.IsNullOrWhiteSpace(context.CallId)
            && ClientTables.TryGetValue(client, out var table))
        {
            _table = table;
            _callId = context.CallId;
            table.Register(_callId, invocation);
        }
    }

    public static void AttachClient(McpClient client, ActiveInvocationTable table) =>
        ClientTables.AddOrUpdate(client, table);

    public void Dispose()
    {
        if (_table is not null && _callId is not null && _current is not null)
            _table.Unregister(_callId, _current);
        Current.Value = _previous;
    }

    public static async ValueTask<ElicitResult> HandleAsync(
        IMcpElicitationHandler? handler,
        string serverName,
        ActiveInvocationTable? activeInvocations,
        ElicitRequestParams? request,
        CancellationToken cancellationToken)
    {
        var invocation = Current.Value
            ?? TryResolveActiveInvocation(activeInvocations, request)
            ?? throw new McpElicitationUnavailableException("unattributed");
        if (request is null)
            throw new McpElicitationUnavailableException("unattributed");
        if (invocation.Context.TurnServices?.GetApprovalPolicy?.Invoke() == ApprovalPolicy.Deny)
            return new ElicitResult { Action = "decline" };
        if (handler is null)
            throw new McpElicitationUnavailableException("no_client");

        using var inputCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, invocation.CallCancellation);
        using var stopWatching = new CancellationTokenSource();
        var watcher = CancelOnCompletionAsync(invocation.ClientCompletion, inputCancellation, stopWatching.Token);
        try
        {
            inputCancellation.Token.ThrowIfCancellationRequested();
            return await handler.HandleAsync(invocation.Context, serverName, request, inputCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && invocation.ClientCompletion.IsCompleted)
        {
            throw new McpElicitationUnavailableException("disconnected");
        }
        finally
        {
            stopWatching.Cancel();
            await watcher.ConfigureAwait(false);
        }
    }

    private static Invocation? TryResolveActiveInvocation(
        ActiveInvocationTable? activeInvocations,
        ElicitRequestParams? request)
    {
        if (activeInvocations is null || request?.Meta is null)
            return null;
        if (request.Meta["callId"] is not JsonValue value
            || !value.TryGetValue<string>(out var callId)
            || string.IsNullOrWhiteSpace(callId))
            return null;
        return activeInvocations.TryGet(callId, out var invocation) ? invocation : null;
    }

    private static async Task CancelOnCompletionAsync(
        Task completion,
        CancellationTokenSource inputCancellation,
        CancellationToken stopWatching)
    {
        try
        {
            await completion.WaitAsync(stopWatching).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stopWatching.IsCancellationRequested)
        {
            return;
        }
        catch
        {
        }
        inputCancellation.Cancel();
    }
}
