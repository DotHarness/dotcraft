using System.Globalization;
using System.Text.RegularExpressions;
namespace DotCraft.Context.Compaction;

/// <summary>
/// English prompt templates and formatting helpers for the compaction pipeline.
/// </summary>
public static class CompactionPrompts
{
    private static readonly string NoToolsPreambleEn = EmbeddedPrompt.Read("compaction-no-tools-preamble");

    private static readonly string BaseCompactPromptEn = EmbeddedPrompt.Read("compaction-full");

    // "up_to" variant: model sees only the summarized prefix; newer messages
    // follow after the summary in the next turn.
    private static readonly string PartialCompactUpToEn = EmbeddedPrompt.Read("compaction-partial-up-to");

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
