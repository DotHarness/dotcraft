using Microsoft.Extensions.AI;

namespace DotCraft.Mcp;

internal sealed class McpSessionRecoveringTool : DelegatingAIFunction
{
    private readonly IMcpToolInvocationCoordinator _coordinator;
    private readonly string _serverName;
    private readonly string _toolName;

    public McpSessionRecoveringTool(
        IMcpToolInvocationCoordinator coordinator,
        string serverName,
        AIFunction innerTool)
        : base(innerTool)
    {
        _coordinator = coordinator;
        _serverName = serverName;
        _toolName = innerTool.Name;
    }

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var target = await _coordinator.TryGetInvocationTargetAsync(_serverName, _toolName, cancellationToken);
        if (target == null)
        {
            throw new InvalidOperationException(
                $"MCP tool '{_toolName}' from server '{_serverName}' is not currently available.");
        }

        try
        {
            return await InvokeToolAsync(target, arguments, cancellationToken);
        }
        catch (Exception ex) when (ShouldAttemptStaleSessionRecovery(target, ex))
        {
            var refreshedTarget = await _coordinator.RefreshToolAfterStaleSessionAsync(
                _serverName,
                _toolName,
                target.Generation,
                cancellationToken);

            if (refreshedTarget == null)
                throw;

            return await InvokeToolAsync(refreshedTarget, arguments, cancellationToken);
        }
    }

    private static async ValueTask<object?> InvokeToolAsync(
        McpToolInvocationTarget target,
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        if (target.ToolTimeout is not { } timeout)
            return await target.Tool.InvokeAsync(arguments, cancellationToken);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            return await target.Tool.InvokeAsync(arguments, timeoutCts.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"MCP tool '{target.ToolName}' from server '{target.ServerName}' timed out after {timeout.TotalSeconds:0.###}s.",
                ex);
        }
    }

    private static bool ShouldAttemptStaleSessionRecovery(McpToolInvocationTarget target, Exception exception) =>
        exception is not OperationCanceledException &&
        string.Equals(target.Transport, "streamableHttp", StringComparison.OrdinalIgnoreCase) &&
        McpStaleSessionDetector.IsStaleSessionFailure(exception, target.HasSessionId);
}
