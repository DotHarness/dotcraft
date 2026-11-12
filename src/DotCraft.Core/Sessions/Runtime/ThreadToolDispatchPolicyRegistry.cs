using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Tools;

namespace DotCraft.Sessions;

public sealed class ThreadToolDispatchPolicyRegistry : IToolPolicyEvaluator
{
    private readonly ConcurrentDictionary<string, (ThreadCapabilityPolicyEvaluator Policy, AgentModeManager ModeManager)> _policies =
        new(StringComparer.Ordinal);

    public void Bind(
        string threadId,
        ThreadConfiguration configuration,
        AgentRuntimeContext context,
        AgentModeManager modeManager)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        _policies[threadId] = (new ThreadCapabilityPolicyEvaluator(configuration, context), modeManager);
    }

    public void Remove(string threadId) => _policies.TryRemove(threadId, out _);

    public ValueTask<ToolDispatchDecision> EvaluateAsync(
        ToolInvocationContext context,
        ToolRegistration registration,
        JsonObject arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_policies.TryGetValue(context.ThreadId, out var binding)
            ? binding.Policy.EvaluateRegistration(registration, arguments, binding.ModeManager.CurrentMode,
                context.WorkspacePath, context.ExecutionLocation)
            : ToolDispatchDecision.Allow);
    }
}
