using System.Globalization;
using System.Text.RegularExpressions;
namespace DotCraft.Context.Compaction;

/// <summary>
/// English prompt templates and formatting helpers for the compaction pipeline.
/// </summary>
public static class CompactionPrompts
{
    private const string NoToolsPreambleEn = """
Summarize the supplied conversation. Return only the handoff summary as text. Do not call tools or include a separate analysis draft.

""";

    private const string BaseCompactPromptEn = """
Create a concise handoff summary for continuing this session after context compaction.
Stay below {0} tokens, and use only the length the remaining work needs.

Write these sections in order, as compact bullets focused on the continuation:

<summary>
1. Current Task and User Intent: the active request, constraints, and explicit preferences that still matter.
2. Key Decisions and Assumptions: design choices, protocol or API expectations, and tradeoffs already settled.
3. Important Files and Code Areas: files read, edited, or planned, with why each matters.
4. Errors, Failures, and Fixes: only issues that affect the next step or explain the current state.
5. Current State: what is complete, what is partially done, and what remains.
6. Next Step: the most direct continuation aligned with the latest user request.
</summary>

Include code, logs, or command output only as short excerpts needed for the next step. Point to files or sources that can be read again instead of copying them.
""";

    // "up_to" variant: model sees only the summarized prefix; newer messages
    // follow after the summary in the next turn.
    private const string PartialCompactUpToEn = """
Create a concise handoff summary for the older portion of this conversation.
This summary will precede newer messages preserved verbatim after compaction. Those newer messages are not included here.
Stay below {0} tokens, and use only the length the remaining work needs.

Write these sections in order, as compact bullets covering what the preserved recent messages need:

<summary>
1. Earlier Task and User Intent: the request, constraints, and explicit preferences that still matter.
2. Key Decisions and Assumptions: design choices, protocol or API expectations, and tradeoffs already settled.
3. Important Files and Code Areas: files read, edited, or planned, with why each matters.
4. Errors, Failures, and Fixes: only issues that affect later work or explain the current state.
5. Work Completed: what the summarized prefix finished.
6. Context for Preserved Recent Messages: what the recent messages assume from earlier context.
</summary>

Include code, logs, or command output only as short excerpts needed for the next step. Point to files or sources that can be read again instead of copying them.
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
    public static string GetCompactPrompt(int maxOutputTokens) =>
        NoToolsPreambleEn + FormatBudget(BaseCompactPromptEn, maxOutputTokens);

    /// <summary>
    /// Returns the system-prompt text for a partial (up-to) compaction where
    /// the summary will precede retained recent messages.
    /// </summary>
    public static string GetPartialCompactPrompt(int maxOutputTokens) =>
        NoToolsPreambleEn + FormatBudget(PartialCompactUpToEn, maxOutputTokens);

    private static string FormatBudget(string template, int maxOutputTokens) =>
        string.Format(CultureInfo.InvariantCulture, template, maxOutputTokens.ToString("N0", CultureInfo.InvariantCulture));

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
        bool recentMessagesPreserved = false)
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
