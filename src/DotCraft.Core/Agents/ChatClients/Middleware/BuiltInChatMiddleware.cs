using DotCraft.Contributions;
using DotCraft.Tools;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

/// <summary>The built-in chat middleware, one stateless singleton per wrapper. Each decides from the host inputs whether it applies, returning the inner client unchanged when it does not.</summary>
internal static class BuiltInChatMiddleware
{
    /// <summary>Records model calls, outside the orchestration loop.</summary>
    internal sealed class Tracing : IChatMiddleware
    {
        public string Name => ChatMiddlewareNames.Tracing;

        public IChatClient Wrap(IChatClient inner, ChatPipelineContext context) =>
            context.Host?.TraceCollector is { } collector
                ? new TracingChatClient(inner, collector)
                : inner;
    }

    /// <summary>Runs the tool-call orchestration loop configured by the call site.</summary>
    internal sealed class FunctionInvocation : IChatMiddleware
    {
        public string Name => ChatMiddlewareNames.FunctionInvocation;

        public IChatClient Wrap(IChatClient inner, ChatPipelineContext context) =>
            context.Host?.CreateFunctionInvokingClient?.Invoke(inner) ?? inner;
    }

    /// <summary>Injects tools the model activated mid-turn under simulated deferred loading.</summary>
    internal sealed class DynamicToolInjection : IChatMiddleware
    {
        public string Name => ChatMiddlewareNames.DynamicToolInjection;

        public IChatClient Wrap(IChatClient inner, ChatPipelineContext context)
        {
            if (context.Host is not { DeferredTools: { } registry } host
                || registry.Mode != DeferredToolLoadingMode.Simulated)
            {
                return inner;
            }

            return new DynamicToolInjectionChatClient(
                inner,
                registry,
                host.TraceCollector,
                host.HookRunner);
        }
    }

    /// <summary>Normalizes image content before it reaches the provider adapters.</summary>
    internal sealed class ImageSanitizing : IChatMiddleware
    {
        public string Name => ChatMiddlewareNames.ImageSanitizing;

        public IChatClient Wrap(IChatClient inner, ChatPipelineContext context) =>
            new ImageContentSanitizingChatClient(inner, context.Host?.ToolResultsAcceptImages == true);
    }
}
