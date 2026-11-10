using DotCraft.Tools;
using ModelContextProtocol.Protocol;

namespace DotCraft.Mcp;

internal sealed class McpInvocationScope : IDisposable
{
    private sealed record Invocation(ToolInvocationContext Context, Task ClientCompletion);

    private static readonly AsyncLocal<Invocation?> Current = new();
    private readonly Invocation? _previous;

    public McpInvocationScope(ToolInvocationContext? context, Task clientCompletion)
    {
        _previous = Current.Value;
        Current.Value = context is null ? null : new Invocation(context, clientCompletion);
    }

    public void Dispose() => Current.Value = _previous;

    public static async ValueTask<ElicitResult> HandleAsync(
        IMcpElicitationHandler? handler,
        string serverName,
        ElicitRequestParams? request,
        CancellationToken cancellationToken)
    {
        var invocation = Current.Value ?? throw new McpElicitationUnavailableException("unattributed");
        if (handler is null)
            throw new McpElicitationUnavailableException("no_client");
        if (request is null)
            throw new McpElicitationUnavailableException("unattributed");

        using var inputCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
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
