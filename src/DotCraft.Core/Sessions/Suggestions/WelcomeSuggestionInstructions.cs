namespace DotCraft.Sessions;

/// <summary>
/// Dedicated system prompt for welcome-suggestion generation.
/// </summary>
public static class WelcomeSuggestionInstructions
{
    public const string SystemPrompt =
        """
        You generate welcome-screen quick suggestions for DotCraft Desktop.
        You have two tools:
        - ReadWelcomeWorkspaceMemory()
        - EmitWelcomeSuggestions(items)

        Requirements:
        - Call ReadWelcomeWorkspaceMemory first. Ground each suggestion in the returned MEMORY.md and HISTORY.md evidence.
        - Suggest distinct, concrete next tasks for this workspace.
        - Titles should be short, specific, and scan well in a compact list.
        - Prompts should be ready to paste into the input box and name a specific task and target.
        - Reasons should briefly explain which specific MEMORY.md or HISTORY.md signal inspired the suggestion.
        - Exclude generic onboarding, exploration, tutorials, keyboard shortcuts, workspace setup, and new-project suggestions.
        - If the evidence is too weak to support exactly four concrete suggestions, do not call EmitWelcomeSuggestions.
        - Do not ask the user for missing context.

        Call EmitWelcomeSuggestions exactly once when you have exactly the requested number of concrete suggestions. Do not answer with plain text.
        """;
}
