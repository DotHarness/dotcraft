using DotCraft.Context;
using Microsoft.Extensions.AI;

namespace DotCraft.Tracing;

public sealed partial class TraceCollector
{
    public void RecordMaintenanceForkRequest(
        string sessionKey,
        MaintenanceForkTaskKind taskKind,
        string prompt,
        string? threadId,
        string? turnId,
        string? mode,
        string? modelId,
        string? providerId,
        int snapshotMessageCount,
        int extraTailMessageCount,
        IReadOnlyList<AITool>? tools,
        string? baseInstructionsFingerprint,
        string? toolFingerprint,
        long? estimatedInputTokens = null,
        string? snapshotSource = null,
        string? snapshotInvalidReason = null,
        int? effectiveBudgetTokens = null,
        string? inputBudgetSource = null,
        bool? preflightRejected = null,
        bool? cacheShapeApplied = null,
        string? cacheShapeKind = null,
        bool? promptCacheKeyPresent = null,
        string? cacheMarkerSource = null,
        string? cacheStateKeyKind = null,
        string? cacheStateKeyHash = null,
        string? cacheWriteMode = null,
        bool? tailCacheWriteSkipped = null,
        bool? providerImplicitCacheWrite = null,
        string requestPath = "snapshot",
        string selectionReason = "snapshot_prefix_matched")
    {
        var toolNames = NormalizeToolNames(tools?.Select(static tool => tool.Name ?? string.Empty));
        store.Record(new TraceEvent
        {
            Type = TraceEventType.MaintenanceForkRequest,
            SessionKey = sessionKey,
            Content = prompt,
            ToolName = FormatMaintenanceKind(taskKind),
            ModelId = modelId,
            ToolNames = toolNames,
            MetadataJson = SerializeMetadata(new
            {
                taskKind = FormatMaintenanceKind(taskKind),
                threadId,
                turnId,
                mode,
                requestPath,
                selectionReason,
                modelId,
                providerId,
                snapshotMessageCount,
                extraTailMessageCount,
                toolCount = toolNames.Length,
                toolNames,
                baseInstructionsFingerprint,
                toolFingerprint,
                estimatedInputTokens,
                snapshotSource,
                snapshotInvalidReason,
                effectiveBudgetTokens,
                inputBudgetSource,
                preflightRejected,
                cacheShapeApplied,
                cacheShapeKind,
                promptCacheKeyPresent,
                cacheMarkerSource,
                cacheStateKeyKind,
                cacheStateKeyHash,
                cacheWriteMode,
                tailCacheWriteSkipped,
                providerImplicitCacheWrite
            })
        });
    }

    public void RecordMaintenanceForkResponse(
        string sessionKey,
        MaintenanceForkTaskKind taskKind,
        ChatResponse? response,
        string? fallbackReason)
    {
        var text = response?.Text;
        var usage = response?.Usage is null
            ? (TokenUsageSnapshot?)null
            : TokenUsageExtractor.FromResponse(response);
        store.Record(new TraceEvent
        {
            Type = TraceEventType.MaintenanceForkResponse,
            SessionKey = sessionKey,
            Content = string.IsNullOrWhiteSpace(text) ? "(empty)" : text,
            ToolName = FormatMaintenanceKind(taskKind),
            ResponseId = response?.ResponseId,
            ModelId = response?.ModelId,
            FinishReason = response?.FinishReason?.ToString(),
            MetadataJson = SerializeMetadata(new
            {
                taskKind = FormatMaintenanceKind(taskKind),
                fallbackReason,
                responseMessages = DescribeMessages(response?.Messages),
                usage
            })
        });
    }

    public void RecordMaintenanceForkResponse(
        string sessionKey,
        MaintenanceForkTaskKind taskKind,
        string fallbackReason,
        string? providerError = null)
    {
        store.Record(new TraceEvent
        {
            Type = TraceEventType.MaintenanceForkResponse,
            SessionKey = sessionKey,
            Content = "(empty)",
            ToolName = FormatMaintenanceKind(taskKind),
            MetadataJson = SerializeMetadata(new
            {
                taskKind = FormatMaintenanceKind(taskKind),
                fallbackReason,
                providerError,
                responseMessages = Array.Empty<object>()
            })
        });
    }

    private static string FormatMaintenanceKind(MaintenanceForkTaskKind kind) => kind switch
    {
        MaintenanceForkTaskKind.ContextCompaction => "context_compaction",
        _ => kind.ToString()
    };
}
