using DotCraft.Hooks;
using DotCraft.Tools;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

/// <summary>The per-agent host state one pipeline composition makes available to the built-in chat middleware. Every member is optional; a built-in whose input is absent returns the inner client unchanged.</summary>
internal sealed class ChatPipelineHostInputs
{
    /// <summary>Gets the trace collector, or <see langword="null"/> when tracing is off.</summary>
    internal TraceCollector? TraceCollector { get; init; }

    /// <summary>Gets the deferred tool registry, or <see langword="null"/> when deferred loading is off.</summary>
    internal DeferredToolActivationIndex? DeferredTools { get; init; }

    /// <summary>Gets the hook runner for dynamic tool injection, or <see langword="null"/> when hooks run elsewhere (snapshot dispatch) or are not configured.</summary>
    internal HookRunner? HookRunner { get; init; }

    /// <summary>Gets the SubAgent progress entry, or <see langword="null"/> when the run is not tracked by the live table.</summary>
    internal SubAgentProgressBridge.ProgressEntry? SubAgentProgress { get; init; }

    /// <summary>Gets the factory for the tool-call orchestration client, whose configuration stays owned by the call site.</summary>
    internal Func<IChatClient, IChatClient>? CreateFunctionInvokingClient { get; init; }
}
