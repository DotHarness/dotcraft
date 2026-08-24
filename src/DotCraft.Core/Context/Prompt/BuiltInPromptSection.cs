using DotCraft.Contributions;

namespace DotCraft.Context;

/// <summary>A stateless built-in prompt section backed by a delegate; per-build inputs travel on the evaluation context.</summary>
internal sealed class BuiltInPromptSection(
    string name,
    Func<SystemPromptSectionContext, string?> content) : ISystemPromptSection
{
    /// <inheritdoc />
    public string Name => name;

    /// <inheritdoc />
    public string? GetContent(SystemPromptSectionContext context) => content(context);
}
