using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Hooks;
using DotCraft.Mcp;
using DotCraft.Sessions.Wire;
using DotCraft.Tools;
using ModelContextProtocol.Protocol;

namespace DotCraft.Sessions;

internal sealed class SessionHookMcpExecutor(
    Func<string?, McpClientManager?> resolveManager,
    Func<IReadOnlyList<string>?> resolveEnabledPlugins) : IHookMcpExecutor
{
    public async Task<HookMcpExecutionResult> ExecuteAsync(HookMcpExecutionRequest request, CancellationToken ct)
    {
        var manager = resolveManager(request.HookInput.SessionId);
        if (manager is null)
        {
            return request.SkipIfDormant
                ? new HookMcpExecutionResult(Succeeded: true, Skipped: true)
                : new HookMcpExecutionResult(Succeeded: false, Error: $"No active MCP runtime is available for server '{request.Server}'.");
        }

        var (exists, enabled, isReady, hasBeenInvoked) = await manager
            .TryGetServerInvocationStateAsync(request.Server, ct)
            .ConfigureAwait(false);

        if (!exists || !enabled || !isReady)
        {
            return request.SkipIfDormant
                ? new HookMcpExecutionResult(Succeeded: true, Skipped: true)
                : new HookMcpExecutionResult(Succeeded: false, Error: $"MCP server '{request.Server}' is not enabled or ready.");
        }

        if (request.SkipIfDormant && !hasBeenInvoked)
            return new HookMcpExecutionResult(Succeeded: true, Skipped: true);

        Dictionary<string, object?>? arguments = null;
        if (request.Input is { Count: > 0 })
        {
            arguments = request.Input.ToDictionary(
                static pair => pair.Key,
                static pair => (object?)pair.Value?.Deserialize<object>(SessionWireJsonOptions.Default),
                StringComparer.Ordinal);
        }

        var threadId = string.IsNullOrWhiteSpace(request.HookInput.SessionId)
            ? "host"
            : request.HookInput.SessionId!;
        var callId = $"hook_{Guid.NewGuid():N}";
        var toolName = new ToolName(request.Tool, request.Server);
        var definitionId = new ToolDefinitionId(ToolSourceKind.Mcp, request.Server, new SourceToolId(request.Tool));
        var invocationContext = new ToolInvocationContext(
            threadId,
            request.HookInput.TurnId,
            callId,
            ToolInvocationAudience.Host,
            toolName,
            definitionId,
            new RuntimeBindingId($"hook:{request.Server}:{request.Tool}"),
            SnapshotRevision: 0,
            StartedAt: DateTimeOffset.UtcNow,
            Origin: new ToolInvocationOrigin("hook"),
            WorkspacePath: request.HookInput.Cwd,
            IsSubagent: request.HookInput.IsSubagent,
            ParentThreadId: request.HookInput.ParentThreadId,
            EnabledPlugins: resolveEnabledPlugins());

        var result = await manager.CallToolAsync(
            request.Server,
            request.Tool,
            arguments,
            expectedGeneration: null,
            ct,
            invocationContext,
            meta: null,
            hookEvent: request.Event.ToString()).ConfigureAwait(false);

        var textOutput = ExtractText(result);
        return result.IsError == true
            ? new HookMcpExecutionResult(Succeeded: false, Error: textOutput ?? "The MCP hook tool returned an error.")
            : new HookMcpExecutionResult(Succeeded: true, Output: textOutput);
    }

    private static string? ExtractText(CallToolResult result)
    {
        if (result.Content is not { Count: > 0 })
            return null;

        var parts = result.Content
            .OfType<TextContentBlock>()
            .Select(static block => block.Text)
            .Where(static text => !string.IsNullOrWhiteSpace(text))
            .ToArray();
        return parts.Length == 0 ? null : string.Join("\n", parts);
    }
}
