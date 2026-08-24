using DotCraft.Context;

namespace DotCraft.Contributions;

/// <summary>The read-only context created once per prompt build and handed to every <see cref="ISystemPromptSection"/> and <see cref="ISystemPromptAssembler"/>.</summary>
public sealed class SystemPromptSectionContext
{
    /// <summary>Creates the evaluation context for one prompt build.</summary>
    /// <param name="availableToolNames">
    /// The tool names exposed to the model for this build, or <see langword="null"/> when the caller
    /// does not constrain sections by tool availability.
    /// </param>
    public SystemPromptSectionContext(
        string? threadId,
        string workspacePath,
        string craftPath,
        IReadOnlyList<string>? availableToolNames = null,
        string? originChannel = null)
    {
        ThreadId = threadId;
        WorkspacePath = workspacePath ?? throw new ArgumentNullException(nameof(workspacePath));
        CraftPath = craftPath ?? throw new ArgumentNullException(nameof(craftPath));
        AvailableToolNames = availableToolNames;
        OriginChannel = originChannel;
    }

    /// <summary>Gets the thread the prompt is built for, or <see langword="null"/> when unbound.</summary>
    public string? ThreadId { get; }

    /// <summary>Gets the absolute workspace path.</summary>
    public string WorkspacePath { get; }

    /// <summary>Gets the absolute DotCraft data directory path.</summary>
    public string CraftPath { get; }

    /// <summary>Gets the tool names exposed to the model for this build, or <see langword="null"/> when unconstrained.</summary>
    public IReadOnlyList<string>? AvailableToolNames { get; }

    /// <summary>Gets the channel the owning thread originated from, when known.</summary>
    public string? OriginChannel { get; }

    /// <summary>Determines whether a tool is exposed to the model for this build, comparing names case-insensitively.</summary>
    public bool IsToolAvailable(string toolName) =>
        AvailableToolNames?.Any(name => string.Equals(name, toolName, StringComparison.OrdinalIgnoreCase))
        == true;

    /// <summary>Gets the kernel-owned inputs the built-in sections read from.</summary>
    internal PromptSectionSources? Sources { get; init; }

    internal PromptSectionSources RequireSources() =>
        Sources ?? throw new InvalidOperationException(
            "This system prompt section context was not created by the prompt builder and carries no "
            + "kernel state. Built-in sections can only be evaluated through PromptBuilder.");
}
