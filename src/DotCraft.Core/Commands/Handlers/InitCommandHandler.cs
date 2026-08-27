using DotCraft.Commands.Core;

namespace DotCraft.Commands.Handlers;

/// <summary>
/// Expands /init into an agent task that creates workspace project instructions.
/// </summary>
public sealed class InitCommandHandler : ICommandHandler
{
    internal const string Prompt = """
        Generate `AGENTS.md` in the root of the workspace that owns this thread, grounded in the current workspace.

        Before writing, check whether `AGENTS.override.md` or `AGENTS.md` already exists in that workspace root. If either exists, do not overwrite or modify it; tell the user that the existing root instructions were preserved and stop.

        Inspect the repository before writing so every path, command, convention, and recommendation is grounded in the actual project. Produce the guide in English.

        Document requirements:

        - Title the document "Repository Guidelines".
        - Use Markdown headings for structure.
        - Keep the document concise; 200-400 words is optimal.
        - Keep explanations short, direct, specific, and actionable.
        - Include examples such as commands, directory paths, or naming patterns where useful.

        Cover the applicable parts of project structure and module organization, build/test/development commands, coding style and naming conventions, testing guidelines, and commit/pull request expectations. Add or omit sections based on repository evidence; security, configuration, architecture, or agent-specific guidance may be included when relevant.
        """;

    /// <inheritdoc />
    public string[] Commands => ["/init"];

    /// <inheritdoc />
    public Task<CommandResult> HandleAsync(CommandContext context, ICommandResponder responder)
    {
        _ = context;
        _ = responder;
        return Task.FromResult(CommandResult.PromptExpansion(Prompt));
    }
}
