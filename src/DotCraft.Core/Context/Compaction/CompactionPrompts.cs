using System.Text.RegularExpressions;
using DotCraft.Localization;

namespace DotCraft.Context.Compaction;

/// <summary>
/// English prompt templates and formatting helpers for the compaction pipeline.
/// </summary>
public static class CompactionPrompts
{
    // Aggressive no-tools preamble. We still surface tools to the summarizer
    // because FunctionInvokingChatClient is installed on the chat stack, so
    // the system prompt has to be explicit that any tool call wastes the turn.
    private const string NoToolsPreambleEn = """
CRITICAL: Respond with TEXT ONLY. Do NOT call any tools.

- Do NOT use ReadFile, Exec, GrepFiles, FindFiles, EditFile, WriteFile, or ANY other tool.
- You already have all the context you need in the conversation above.
- Tool calls will be REJECTED and will waste your only turn; you will fail the task.
- Return only the handoff summary. Do not include a separate analysis draft.

""";

    private const string NoToolsTrailerEn =
        "\n\nREMINDER: Do NOT call any tools. Respond with the handoff summary only. "
        + "Tool calls will be rejected and you will fail the task.";

    private const string BaseCompactPromptEn = $$"""
Create a concise handoff summary for continuing this session after context compaction.

Length:
- Target about 4,000-6,000 output tokens.
- Stay below 12,000 output tokens.
- Do not produce a separate analysis draft or hidden reasoning section.

Include only information needed to continue work:

1. Current task and user intent: the active request, constraints, and any explicit preferences that still matter.
2. Key decisions and assumptions: important design choices, protocol/API expectations, and tradeoffs already settled.
3. Important files and code areas: files read, edited, or planned, with why each matters.
4. Errors, failures, and fixes: only issues that affect the next step or explain current state.
5. Current state: what has already been completed, what is partially done, and what remains.
6. Next step: the most direct continuation aligned with the latest user request.

Do not list every user message by default.
Do not include complete code snippets, logs, or command outputs unless a tiny excerpt is essential for continuing the task.
Prefer compact bullets over chronological narration.

Structure your response as:

<summary>
1. Current Task and User Intent:
   ...
2. Key Decisions and Assumptions:
   - ...
3. Important Files and Code Areas:
   - ...
4. Errors, Failures, and Fixes:
   - ...
5. Current State:
   ...
6. Next Step:
   ...
</summary>

If you choose not to use tags, keep the same section order.
""";

    // "up_to" variant: model sees only the summarized prefix; newer messages
    // follow after the summary in the next turn.
    private const string PartialCompactUpToEn = $$"""
Create a concise handoff summary for the older portion of this conversation.
This summary will be placed before newer messages that are preserved verbatim after compaction; you do not see those newer messages here.

Length:
- Target about 4,000-6,000 output tokens.
- Stay below 12,000 output tokens.
- Do not produce a separate analysis draft or hidden reasoning section.

Include only information needed to understand the older context before reading the preserved recent tail:

1. Earlier task and user intent
2. Key decisions and assumptions
3. Important files and code areas
4. Errors, failures, and fixes
5. Work completed in the summarized prefix
6. Context needed by the preserved recent messages

Do not list every user message by default.
Do not include complete code snippets, logs, or command outputs unless a tiny excerpt is essential for continuing the task.
Prefer compact bullets over chronological narration.

Structure your response as:

<summary>
1. Earlier Task and User Intent:
   ...
2. Key Decisions and Assumptions:
   - ...
3. Important Files and Code Areas:
   - ...
4. Errors, Failures, and Fixes:
   - ...
5. Work Completed:
   ...
6. Context for Preserved Recent Messages:
   ...
</summary>

If you choose not to use tags, keep the same section order.
""";

    private const string ContinuationPrefaceEn =
        "This session is being continued from a previous conversation that ran out of context. "
        + "The summary below covers the earlier portion of the conversation.\n\n";

    private const string TranscriptHintEn =
        "\n\nIf you need specific details from before compaction (like exact code snippets, error messages, or content you generated), "
        + "read the full transcript at: {0}";

    private const string RecentPreservedEn = "\n\nRecent messages are preserved verbatim.";

    /// <summary>
    /// Returns the system-prompt text for a full-history compaction.
    /// </summary>
    public static string GetCompactPrompt(Language? language = null) =>
        NoToolsPreambleEn + BaseCompactPromptEn + NoToolsTrailerEn;

    /// <summary>
    /// Returns the system-prompt text for a partial (up-to) compaction where
    /// the summary will precede retained recent messages.
    /// </summary>
    public static string GetPartialCompactPrompt(Language? language = null) =>
        NoToolsPreambleEn + PartialCompactUpToEn + NoToolsTrailerEn;

    private static readonly Regex AnalysisBlockRegex =
        new(@"<analysis>[\s\S]*?</analysis>", RegexOptions.Compiled);

    private static readonly Regex SummaryBlockRegex =
        new(@"<summary>([\s\S]*?)</summary>", RegexOptions.Compiled);

    private static readonly Regex MultipleBlankLinesRegex =
        new(@"\n\n+", RegexOptions.Compiled);

    /// <summary>
    /// Strips the <c>&lt;analysis&gt;</c> scratchpad and unwraps the
    /// <c>&lt;summary&gt;</c> block, returning a plain-text summary.
    /// </summary>
    public static string FormatCompactSummary(string summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
            return string.Empty;

        var formatted = AnalysisBlockRegex.Replace(summary, string.Empty);
        var match = SummaryBlockRegex.Match(formatted);
        if (match.Success)
        {
            var content = match.Groups[1].Value.Trim();
            formatted = SummaryBlockRegex.Replace(formatted, $"Summary:\n{content}", 1);
        }

        formatted = MultipleBlankLinesRegex.Replace(formatted, "\n\n");
        return formatted.Trim();
    }

    /// <summary>
    /// Wraps a formatted summary with the continuation preamble that gets
    /// prepended to the new conversation history after compaction.
    /// </summary>
    public static string GetCompactUserSummaryMessage(
        string summary,
        string? transcriptPath = null,
        bool recentMessagesPreserved = false,
        Language? language = null)
    {
        var formatted = FormatCompactSummary(summary);

        var text = ContinuationPrefaceEn + formatted;

        if (!string.IsNullOrWhiteSpace(transcriptPath))
        {
            text += string.Format(TranscriptHintEn, transcriptPath);
        }

        if (recentMessagesPreserved)
        {
            text += RecentPreservedEn;
        }

        return text;
    }
}
