using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using DotCraft.Hooks;
using DotCraft.Tracing;

namespace DotCraft.Tools;

/// <summary>Runs the repository hook protocol at the common dispatcher boundary.</summary>
public sealed class HookRunnerToolDispatchAdapter(HookRunner hookRunner) : IToolDispatchHookRunner
{
    private readonly ConcurrentDictionary<string, Dictionary<string, object?>> _arguments = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public async ValueTask<ToolDispatchDecision> RunPreToolUseAsync(
        ToolInvocationContext context,
        ToolRegistration registration,
        JsonObject arguments,
        CancellationToken cancellationToken = default)
    {
        var values = arguments.ToDictionary(
            static pair => pair.Key,
            static pair => (object?)pair.Value?.DeepClone(),
            StringComparer.Ordinal);
        _arguments[context.CallId] = values;
        var hookResult = await hookRunner.RunAsync(
            HookEvent.PreToolUse,
            new HookInput
            {
                SessionId = context.ThreadId,
                ToolName = registration.Definition.Name.ToString(),
                ToolArgs = values
            },
            cancellationToken).ConfigureAwait(false);
        return hookResult.Blocked
            ? ToolDispatchDecision.Deny(
                ToolErrorCodes.Unauthorized,
                $"Tool call blocked by hook: {hookResult.BlockReason ?? "no reason given"}")
            : ToolDispatchDecision.Allow;
    }

    /// <inheritdoc />
    public async ValueTask RunTerminalAsync(
        ToolInvocationContext context,
        ToolRegistration registration,
        ToolExecutionResult result,
        CancellationToken cancellationToken = default)
    {
        _arguments.TryRemove(context.CallId, out var values);
        var hookEvent = result.Success ? HookEvent.PostToolUse : HookEvent.PostToolUseFailure;
        var hookResult = await hookRunner.RunAsync(
            hookEvent,
            new HookInput
            {
                SessionId = context.ThreadId,
                ToolName = registration.Definition.Name.ToString(),
                ToolArgs = values,
                ToolResult = result.Success ? result.Content : null,
                Error = result.Success ? null : result.Error?.Message
            },
            cancellationToken).ConfigureAwait(false);
        ToolHookFeedbackScope.Current?.Add(hookEvent, hookResult);
    }
}
