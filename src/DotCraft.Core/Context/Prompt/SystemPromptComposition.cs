using DotCraft.Contributions;
using Microsoft.Extensions.Logging;

namespace DotCraft.Context;

/// <summary>Joins the effective prompt sections and applies the Tier-C assembler. A section or assembler that throws is logged and skipped.</summary>
internal static class SystemPromptComposition
{
    /// <summary>The separator placed between two adjacent prompt sections.</summary>
    internal const string SectionSeparator = "\n\n---\n\n";

    /// <summary>Joins already-produced section bodies with the prompt section separator.</summary>
    internal static string? Join(IReadOnlyList<string> parts) =>
        parts.Count == 0 ? null : string.Join(SectionSeparator, parts);

    /// <summary>Evaluates the resolved sections in order and joins the non-empty results.</summary>
    internal static string Compose(
        IReadOnlyList<ISystemPromptSection> sections,
        SystemPromptSectionContext context,
        ILogger logger)
    {
        var parts = ContributionRead.Fold(
            sections,
            new List<string>(sections.Count),
            (accumulated, section) =>
            {
                if (section.GetContent(context) is { } content && !string.IsNullOrWhiteSpace(content))
                    accumulated.Add(content);
                return accumulated;
            },
            (section, ex) => logger.LogError(
                ex,
                "System prompt section {SectionName} failed and was omitted.",
                SafeName(section)));

        return string.Join(SectionSeparator, parts);
    }

    /// <summary>Applies the single effective Tier-C assembler: the contribution point's authority, highest order with later registration breaking ties.</summary>
    internal static string ApplyAssembler(
        IReadOnlyList<ISystemPromptAssembler> assemblers,
        string prompt,
        SystemPromptSectionContext context,
        ILogger logger)
    {
        if (ContributionRead.Authority(assemblers) is not { } assembler)
            return prompt;

        try
        {
            return assembler.Assemble(prompt, context) ?? prompt;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "System prompt assembler {AssemblerType} failed; using the default assembly.",
                assembler.GetType().FullName);
            return prompt;
        }
    }

    private static string SafeName(ISystemPromptSection section)
    {
        try
        {
            return section.Name;
        }
        catch (Exception)
        {
            return section.GetType().FullName ?? "<unknown>";
        }
    }
}
