using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Tools;

namespace DotCraft.Sessions;

/// <summary>
/// Keeps the current server-authoritative thread policy used by the common dispatcher.
/// Frozen snapshots retain declarations, while policy replacement takes effect immediately.
/// </summary>
public sealed class ThreadToolDispatchPolicyRegistry : IToolPolicyEvaluator
{
    private readonly ConcurrentDictionary<string, ThreadCapabilityPolicyEvaluator> _policies =
        new(StringComparer.Ordinal);

    /// <summary>Replaces the current effective policy for a thread.</summary>
    public void Bind(string threadId, ThreadConfiguration configuration, AgentRuntimeContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        _policies[threadId] = new ThreadCapabilityPolicyEvaluator(configuration, context);
    }

    /// <summary>Forgets policy state when a thread is archived or deleted.</summary>
    public void Remove(string threadId) => _policies.TryRemove(threadId, out _);

    /// <inheritdoc />
    public ValueTask<ToolDispatchDecision> EvaluateAsync(
        ToolInvocationContext context,
        ToolRegistration registration,
        JsonObject arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_policies.TryGetValue(context.ThreadId, out var policy)
            ? policy.EvaluateRegistration(registration, arguments)
            : ToolDispatchDecision.Allow);
    }
}
