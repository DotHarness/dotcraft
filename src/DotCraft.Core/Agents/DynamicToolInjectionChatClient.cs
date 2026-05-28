using DotCraft.Hooks;
using DotCraft.Tools;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

/// <summary>
/// A <see cref="DelegatingChatClient"/> placed <b>inside</b>
/// <c>FunctionInvokingChatClient</c> in the pipeline. Before each LLM call it
/// checks whether the model has activated new deferred MCP tools via
/// <c>SearchTools</c>, and if so clones the incoming <see cref="ChatOptions"/>
/// and appends the newly activated tool schemas so the model can see them.
///
/// The tool list only ever grows (append-only) within a single agent instance.
/// This ensures the prompt prefix remains stable after the first discovery
/// event, allowing the LLM provider's prompt cache to re-establish quickly.
/// </summary>
internal sealed class DynamicToolInjectionChatClient(
    IChatClient innerClient,
    DeferredToolRegistry registry,
    TraceCollector? traceCollector = null,
    HookRunner? hookRunner = null)
    : DelegatingChatClient(innerClient)
{
    // Tracks which deferred tool names have already been injected into a
    // ChatOptions sent to the LLM. Per-instance (i.e. per-agent), monotonically
    // growing to keep the tools list stable and cache-friendly.
    private readonly HashSet<string> _sentToolNames = new(StringComparer.Ordinal);

    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options = InjectActivatedTools(options);
        return base.GetResponseAsync(messages, options, cancellationToken);
    }

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options = InjectActivatedTools(options);
        return base.GetStreamingResponseAsync(messages, options, cancellationToken);
    }

    /// <summary>
    /// Returns <paramref name="options"/> unchanged when no new tools have been
    /// activated since the last call. Otherwise clones the options and appends
    /// the new tool schemas so the LLM sees them.
    /// </summary>
    private ChatOptions? InjectActivatedTools(ChatOptions? options)
    {
        var activatedNames = registry.GetActivatedToolNames();

        // Compute the set of newly activated tools that have not been sent yet.
        List<string>? newNames = null;
        foreach (var name in activatedNames)
        {
            if (!_sentToolNames.Contains(name))
            {
                newNames ??= [];
                newNames.Add(name);
            }
        }

        if (newNames == null)
            return options; // nothing new — preserve exact options reference for cache

        // Clone to avoid mutating the shared ChatOptions object.
        var cloned = options?.Clone() ?? new ChatOptions();
        cloned.Tools ??= [];

        var injected = new List<string>(newNames.Count);
        foreach (var name in newNames)
        {
            if (registry.DeferredTools.TryGetValue(name, out var tool))
            {
                // Apply hook wrapping so deferred tools go through the same
                // PreToolUse/PostToolUse/PostToolUseFailure pipeline as static tools.
                if (hookRunner != null && hookRunner.HasToolHooks && tool is AIFunction fn)
                {
                    var wrapped = new HookWrappedFunction(fn, hookRunner);
                    // Replace the raw entry in ActivatedToolsList so
                    // FunctionInvokingChatClient.AdditionalTools executes the wrapped version.
                    registry.ReplaceActivatedTool(name, wrapped);
                    tool = wrapped;
                }

                cloned.Tools.Add(tool);
                _sentToolNames.Add(name);
                injected.Add(name);
            }
        }

        if (injected.Count > 0 && traceCollector != null)
        {
            var sessionKey = TracingChatClient.CurrentSessionKey ?? TracingChatClient.GetActiveSessionKey();
            if (!string.IsNullOrEmpty(sessionKey))
                traceCollector.RecordToolInjection(sessionKey, injected);
        }

        return cloned;
    }
}
