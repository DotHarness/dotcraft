using DotCraft.Agents;
using DotCraft.Skills;
using DotCraft.Tracing;

namespace DotCraft.Contributions;

/// <summary>The build-time values the agent factory computes for one prompt, so a contribution replacing the memory target can reproduce them.</summary>
public sealed record AgentPromptInputs
{
    /// <summary>Gets the tool names exposed to the model, ordered case-insensitively.</summary>
    public IReadOnlyList<string> ToolNames { get; init; } = [];

    /// <summary>Gets the MCP servers whose tools are discovered on demand, or <see langword="null"/> when deferred loading is inactive.</summary>
    public IReadOnlyList<string>? DeferredMcpServerNames { get; init; }

    /// <summary>Gets the pre-rendered SubAgent profile section, when SubAgents are exposed.</summary>
    public string? SubAgentProfilesSection { get; init; }

    /// <summary>Gets a value indicating whether skill self-learning variant mode is enabled.</summary>
    public bool SkillVariantModeEnabled { get; init; }

    /// <summary>Gets the skill-variant selection target for this build.</summary>
    public SkillVariantTarget? SkillVariantTarget { get; init; }

    /// <summary>Gets the channel or automation role instructions, or <see langword="null"/> when the agent carries them on its own instruction channel.</summary>
    public string? RoleInstructions { get; init; }

    /// <summary>Gets the instructions supplied by the application that started the thread, or <see langword="null"/>.</summary>
    public string? DeveloperInstructions { get; init; }
}

/// <summary>Describes the agent being built, so a contribution can decide whether to take part and how to parameterize the provider it returns.</summary>
public sealed record AgentContextRequest(string? ThreadId, string WorkspacePath, string BotPath)
{
    /// <summary>Gets the build-time prompt inputs, or <see langword="null"/> when the caller composes no prompt.</summary>
    public AgentPromptInputs? PromptInputs { get; init; }

    /// <summary>Materializes the kernel's own memory provider; only the agent factory can supply it.</summary>
    internal Func<AIContextProvider>? CreateBuiltInProvider { get; init; }

    /// <summary>The prompt-cache baseline collector, recorded against the effective prompt rather than the built-in's.</summary>
    internal TraceCollector? TraceCollector { get; init; }

    internal Func<AIContextProvider> RequireBuiltInProvider() =>
        CreateBuiltInProvider ?? throw new InvalidOperationException(
            "This agent context request was not created by the agent factory and carries no built-in "
            + "memory provider, so the 'memory' target cannot fall back to it.");
}

/// <summary>Contributes a pre-send context transform to an agent's <see cref="AIContextProvider"/> list.</summary>
public interface IAgentContextSource : IContributionContract
{
    /// <summary>Creates the provider to append for one agent, or returns <see langword="null"/> to decline taking part.</summary>
    AIContextProvider? CreateProvider(AgentContextRequest request);
}

/// <summary>An <see cref="IAgentContextSource"/> built from a delegate.</summary>
public sealed class AgentContextSource(Func<AgentContextRequest, AIContextProvider?> factory)
    : IAgentContextSource
{
    private readonly Func<AgentContextRequest, AIContextProvider?> _factory =
        factory ?? throw new ArgumentNullException(nameof(factory));

    /// <inheritdoc />
    public AIContextProvider? CreateProvider(AgentContextRequest request) => _factory(request);
}
