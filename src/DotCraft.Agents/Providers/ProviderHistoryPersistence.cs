using System.Text.Json;

namespace DotCraft.Sessions;

internal static class ProviderHistorySchema
{
    public const int CurrentSchemaVersion = 1;
    public const string OpenAIResponsesProtocol = Configuration.ModelProviderProtocols.OpenAIResponses;
}

internal static class ProviderHistorySources
{
    public const string LocalInput = "local_input";
    public const string ProviderOutput = "provider_output";
}

internal static class ProviderHistoryReasons
{
    public const string RemoteCompaction = "remote_compaction";
    public const string Fork = "fork";
    public const string ForkNativeCompaction = "fork_native_compaction";
    public const string Recovery = "recovery";
    public const string RecoveryNativeCompaction = "recovery_native_compaction";

    public static bool IsNativeCompacted(string? reason) =>
        reason is RemoteCompaction or ForkNativeCompaction or RecoveryNativeCompaction;
}

internal sealed class ProviderHistoryEntry
{
    public string EntryId { get; init; } = string.Empty;

    public JsonElement Item { get; init; }
}

internal sealed class ProviderHistoryItemsAppendedPayload
{
    public int SchemaVersion { get; init; }
    public string ThreadId { get; init; } = string.Empty;
    public string TurnId { get; init; } = string.Empty;
    public string Protocol { get; init; } = string.Empty;
    public string GenerationId { get; init; } = string.Empty;
    public string ContextWindowId { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string? AttemptId { get; init; }
    public List<ProviderHistoryEntry> Entries { get; init; } = [];
}

internal sealed class ProviderHistoryReplacedPayload
{
    public int SchemaVersion { get; init; }
    public string ThreadId { get; init; } = string.Empty;
    public string Protocol { get; init; } = string.Empty;
    public string GenerationId { get; init; } = string.Empty;
    public string ContextWindowId { get; init; } = string.Empty;
    public string? CoveredThroughTurnId { get; init; }
    public string Reason { get; init; } = string.Empty;
    public List<ProviderHistoryEntry> Entries { get; init; } = [];
}

internal sealed class ProviderHistoryAttemptAbortedPayload
{
    public int SchemaVersion { get; init; }
    public string ThreadId { get; init; } = string.Empty;
    public string TurnId { get; init; } = string.Empty;
    public string Protocol { get; init; } = string.Empty;
    public string GenerationId { get; init; } = string.Empty;
    public string AttemptId { get; init; } = string.Empty;
}

internal sealed record ProviderHistorySnapshot(
    string GenerationId,
    string ContextWindowId,
    IReadOnlyList<ProviderHistoryEntry> Entries,
    string? CoveredThroughTurnId,
    bool IsNativeCompacted = false)
{
    public static ProviderHistorySnapshot Empty(string contextWindowId) =>
        new(contextWindowId, contextWindowId, [], null);
}

internal static class ProviderHistoryEntryCloner
{
    public static ProviderHistoryEntry Clone(ProviderHistoryEntry entry) => new()
    {
        EntryId = entry.EntryId,
        Item = entry.Item.Clone()
    };
}
