namespace DotCraft.Contributions;

/// <summary>Terminal composition authority over the whole system prompt (Tier C), applied after every <see cref="ISystemPromptSection"/>.</summary>
/// <remarks>At most one assembler is ever effective: only the last contribution of the resolved list is applied, and earlier ones are inert.</remarks>
public interface ISystemPromptAssembler : IContributionContract
{
    /// <summary>Produces the final system prompt from the default-assembled text; returning the input unchanged is a valid no-op.</summary>
    string Assemble(string prompt, SystemPromptSectionContext context);
}
