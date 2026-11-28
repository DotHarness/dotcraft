using DotCraft.Hooks;
using DotCraft.Tools;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

/// <summary>
/// Injects <c>SearchTools</c>-activated deferred tools into each outgoing <see cref="ChatOptions"/>
/// in append-only activation order without mutating the caller-owned options.
/// </summary>
internal sealed class DynamicToolInjectionChatClient(
    IChatClient innerClient,
    DeferredToolActivationIndex registry,
    TraceCollector? traceCollector = null,
    HookRunner? hookRunner = null)
    : DelegatingChatClient(innerClient)
{
    private readonly object _sync = new();
    private readonly HashSet<string> _injectedNames = new(StringComparer.Ordinal);
    private readonly List<AITool> _injectedTools = [];

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

    private ChatOptions? InjectActivatedTools(ChatOptions? options)
    {
        var activatedNames = registry.GetActivatedToolNames();
        if (activatedNames.Count == 0)
            return options;

        List<string>? newlyInjected = null;
        AITool[] allInjected;
        lock (_sync)
        {
            foreach (var name in activatedNames)
            {
                if (_injectedNames.Contains(name))
                    continue;

                if (!registry.DeferredTools.TryGetValue(name, out var tool))
                    continue;

                if (hookRunner is { HasToolHooks: true } && tool is AIFunction fn)
                {
                    var wrapped = new HookWrappedFunction(fn, hookRunner);
                    // Keep FunctionInvokingChatClient.AdditionalTools aligned with the wrapped schema.
                    registry.ReplaceActivatedTool(name, wrapped);
                    tool = wrapped;
                }

                _injectedNames.Add(name);
                _injectedTools.Add(tool);
                newlyInjected ??= [];
                newlyInjected.Add(name);
            }

            if (_injectedTools.Count == 0)
                return options;

            allInjected = [.. _injectedTools];
        }

        if (newlyInjected is { Count: > 0 } && traceCollector != null)
        {
            var sessionKey = TracingChatClient.CurrentSessionKey ?? TracingChatClient.GetActiveSessionKey();
            if (!string.IsNullOrEmpty(sessionKey))
                traceCollector.RecordToolInjection(sessionKey, newlyInjected);
        }

        var cloned = options?.Clone() ?? new ChatOptions();
        cloned.Tools ??= [];

        var existingNames = new HashSet<string>(
            cloned.Tools.Select(static tool => tool.Name),
            StringComparer.Ordinal);
        foreach (var tool in allInjected)
        {
            if (existingNames.Add(tool.Name))
                cloned.Tools.Add(tool);
        }

        return cloned;
    }
}
