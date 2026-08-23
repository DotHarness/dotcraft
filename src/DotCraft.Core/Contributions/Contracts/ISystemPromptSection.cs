namespace DotCraft.Contributions;

/// <summary>One named segment of the assembled system prompt, joined in ascending <see cref="ContributionOptions.Order"/>.</summary>
public interface ISystemPromptSection : IContributionContract
{
    /// <summary>Gets the stable, kebab-case section name used in diagnostics.</summary>
    string Name { get; }

    /// <summary>Produces the section content, or <see langword="null"/> or whitespace to omit the section.</summary>
    string? GetContent(SystemPromptSectionContext context);
}
