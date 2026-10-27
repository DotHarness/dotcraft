using DotCraft.Contributions;

namespace DotCraft.Context;

/// <summary>The built-in behavioural guidance sections: working style, response style, editing workflow, file references, mode protocol, and structured user questions.</summary>
internal static class GuidancePromptSections
{
    /// <summary>Gets the <c>project-instructions</c> precedence and scope policy.</summary>
    internal static string ProjectInstructions { get; } = EmbeddedPrompt.Read("project-instructions");

    /// <summary>Gets the <c>working-style</c> section.</summary>
    internal static string WorkingStyle { get; } = EmbeddedPrompt.Read("working-style");

    /// <summary>Gets the <c>response-style</c> section.</summary>
    internal static string ResponseStyle { get; } = EmbeddedPrompt.Read("response-style");

    /// <summary>Gets the <c>editing-workflow</c> section.</summary>
    internal static string EditingWorkflow { get; } = EmbeddedPrompt.Read("editing-workflow");

    /// <summary>Gets the <c>file-references</c> section.</summary>
    internal static string FileReferences { get; } = EmbeddedPrompt.Read("file-references");

    /// <summary>Gets the <c>mode-protocol</c> section.</summary>
    internal static string ModeProtocol { get; } = EmbeddedPrompt.Read("mode-protocol");

    /// <summary>Gets the <c>request-user-input</c> section.</summary>
    internal static string RequestUserInput { get; } = EmbeddedPrompt.Read("request-user-input");

    /// <summary>Builds coordination guidance from the tools actually exposed to the model.</summary>
    internal static string? UserCoordination(SystemPromptSectionContext context)
    {
        var hasBlockingQuestion = context.IsToolAvailable("RequestUserInput");
        var hasAsyncMessage = context.IsToolAvailable("SendUserMessageAsync");
        if (!hasBlockingQuestion && !hasAsyncMessage)
            return null;

        var rules = new List<string>
        {
            "## User Coordination",
            string.Empty
        };
        if (hasBlockingQuestion)
        {
            rules.Add("- Use `RequestUserInput` when the answer is a prerequisite for further work and a short structured decision is appropriate.");
            rules.Add("- If `RequestUserInput` returns no answers, continue with your best judgment instead of asking again or waiting for a reply.");
        }
        if (hasAsyncMessage)
            rules.Add("- Use `SendUserMessageAsync` for questions, critical blockers or findings, and replies during ongoing work on the app surface. Use commentary for routine progress.");
        if (hasAsyncMessage)
            rules.Add("- After an asynchronous question, continue every authorized task that does not depend on the answer.");
        rules.Add("- Do not repeat the same question, authorization request, or status in both an asynchronous message and the final answer.");
        rules.Add("- Do not create a Goal implicitly. Only an existing Goal explicitly created by the user or system continues across turns.");
        return string.Join(Environment.NewLine, rules);
    }
}
