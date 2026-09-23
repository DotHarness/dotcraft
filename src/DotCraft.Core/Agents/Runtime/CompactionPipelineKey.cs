using DotCraft.Configuration;
using DotCraft.Context.Compaction;

namespace DotCraft.Agents;

internal readonly record struct CompactionPipelineKey(
    string SessionKey,
    EffectiveModelRuntime Runtime,
    bool AutoCompactEnabled,
    bool ReactiveCompactEnabled,
    int ContextWindow,
    int SummaryReserveTokens,
    int SummaryMaxOutputTokens,
    int AutoCompactBufferTokens,
    int WarningBufferTokens,
    int ErrorBufferTokens,
    int ManualCompactBufferTokens,
    int KeepRecentMinTokens,
    int KeepRecentMinGroups,
    int KeepRecentMaxTokens,
    bool MicrocompactEnabled,
    int MicrocompactKeepRecent,
    int MicrocompactGapMinutes,
    int MaxConsecutiveFailures)
{
    public static CompactionPipelineKey From(
        string sessionKey,
        EffectiveModelRuntime runtime,
        CompactionConfig compaction) =>
        new(
            sessionKey,
            runtime,
            compaction.AutoCompactEnabled,
            compaction.ReactiveCompactEnabled,
            compaction.ContextWindow,
            compaction.SummaryReserveTokens,
            compaction.SummaryMaxOutputTokens,
            compaction.AutoCompactBufferTokens,
            compaction.WarningBufferTokens,
            compaction.ErrorBufferTokens,
            compaction.ManualCompactBufferTokens,
            compaction.KeepRecentMinTokens,
            compaction.KeepRecentMinGroups,
            compaction.KeepRecentMaxTokens,
            compaction.MicrocompactEnabled,
            compaction.MicrocompactKeepRecent,
            compaction.MicrocompactGapMinutes,
            compaction.MaxConsecutiveFailures);
}
