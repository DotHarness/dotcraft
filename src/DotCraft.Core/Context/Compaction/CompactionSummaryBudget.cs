namespace DotCraft.Context.Compaction;

internal static class CompactionSummaryBudget
{
    public const string TooLongReason = "compact_summary_too_long";

    public static int ResolveMaxOutputTokens(
        CompactionConfig config,
        PromptRequestSnapshot? snapshot)
    {
        var configured = Math.Max(1, config.SummaryMaxOutputTokens);
        return snapshot?.MaxOutputTokens is > 0
            ? Math.Min(configured, snapshot.MaxOutputTokens.Value)
            : configured;
    }

    public static bool ExceedsMaxOutputTokens(
        string summary,
        CompactionConfig config)
    {
        var limit = Math.Max(1, config.SummaryMaxOutputTokens);
        return MessageTokenEstimator.RoughTokenCount(summary) > limit;
    }
}
