namespace DotCraft.Contributions;

/// <summary>The stable Tier-B target names of the built-in chat middleware, each registered as its <see cref="ContributionOptions.TargetName"/>.</summary>
public static class ChatMiddlewareNames
{
    /// <summary>Records model calls into the trace collector. Agent pipeline only.</summary>
    public const string Tracing = "tracing";

    /// <summary>Runs the tool-call orchestration loop. Every pipeline.</summary>
    public const string FunctionInvocation = "function-invocation";

    /// <summary>Publishes SubAgent progress to the live table. SubAgent pipeline only.</summary>
    public const string SubAgentProgress = "subagent-progress";

    /// <summary>Records SubAgent model calls into the trace collector, inside the orchestration loop. SubAgent pipeline only.</summary>
    public const string SubAgentTracing = "subagent-tracing";

    /// <summary>Injects deferred tools activated mid-turn. Agent pipeline only.</summary>
    public const string DynamicToolInjection = "dynamic-tool-injection";

    /// <summary>Normalizes image content before it reaches the provider. Agent pipeline only.</summary>
    public const string ImageSanitizing = "image-sanitizing";
}
