namespace DotCraft.Tracing;

internal sealed record PromptCacheRequestShapeSnapshot(
    int SchemaVersion,
    string Protocol,
    string Model,
    string? PromptCacheKeyHash,
    string? PromptCacheKeySource,
    string? InstructionsHash,
    string ToolsHash,
    string? ReasoningHash,
    string InputHash,
    int InputItemCount,
    IReadOnlyList<string> InputItemHashes,
    int InputBytes,
    int InputItemIdEligibleCount,
    int InputItemIdPresentCount,
    int InputItemIdGeneratedCount,
    int InputItemIdMissingCount,
    int InputItemIdInvalidSourceCount,
    int? MaxOutputTokensRequested,
    bool MaxOutputTokensPresentAfterOAuthRewrite,
    bool MaxOutputTokensRemovedByOAuthRewrite,
    string? ReasoningEffort,
    string ToolChoiceKind,
    int ToolCount,
    bool StreamingEnabled);

public sealed record PromptCachePointTraceEntry(
    string Model,
    string Role,
    int MessageIndex,
    int ContentIndex,
    int Sequence,
    string HashPrefix,
    bool Remembered,
    bool Latest,
    string ContentKind);
internal sealed record PromptCacheRequestDiagnosticSnapshot(
    string Model,
    string MarkerStrategy,
    string? Ttl,
    int LlmCallIndex,
    int BreakpointCount,
    int CandidateCount,
    int NewSelectedCount,
    int RememberedSelectedCount,
    bool LatestSelectedPointIsNew,
    string? SystemHash,
    string? ToolSchemaHash,
    string? ReasoningHash,
    int ToolCount,
    IReadOnlyList<PromptCacheSelectedPointDiagnostic> SelectedPoints,
    IReadOnlyList<PromptCacheCandidateCountDiagnostic> CandidateCounts);
internal sealed record PromptCacheSelectedPointDiagnostic(
    string Role,
    string ContentKind,
    int MessageIndex,
    int ContentIndex,
    int Sequence,
    string HashPrefix,
    bool Remembered,
    bool Latest);
internal sealed record PromptCacheCandidateCountDiagnostic(
    string Role,
    string ContentKind,
    int Count);
