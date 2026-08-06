using System.Text.Json;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

/// <summary>Source classifications for opaque provider-history entries.</summary>
public enum ProviderHistoryEntrySource
{
    LocalInput,
    ProviderOutput
}

/// <summary>Provider and Session identity carried by a history operation.</summary>
public sealed record ProviderHistoryIdentity(
    string ProviderId,
    string Protocol,
    int SchemaVersion,
    string ThreadId,
    string? TurnId,
    string GenerationId,
    string ContextWindowId);

/// <summary>An opaque provider-native history item with a stable ledger identity.</summary>
public sealed record ProviderHistoryItem(string EntryId, JsonElement Payload);

/// <summary>An immutable provider-native history snapshot loaded by Session Core.</summary>
public sealed record OpaqueProviderHistorySnapshot(
    ProviderHistoryIdentity Identity,
    IReadOnlyList<ProviderHistoryItem> Items,
    string? CoveredThroughTurnId,
    bool IsNativeCompacted = false);

/// <summary>Persists opaque provider-native history without interpreting provider payloads.</summary>
public interface IProviderHistorySink
{
    ValueTask AppendAsync(
        ProviderHistoryIdentity identity,
        ProviderHistoryEntrySource source,
        string? attemptId,
        IReadOnlyList<ProviderHistoryItem> items,
        CancellationToken cancellationToken);

    ValueTask ReplaceAsync(
        ProviderHistoryIdentity identity,
        string? coveredThroughTurnId,
        string reason,
        IReadOnlyList<ProviderHistoryItem> items,
        CancellationToken cancellationToken);

    ValueTask AbortAttemptAsync(
        ProviderHistoryIdentity identity,
        string attemptId,
        CancellationToken cancellationToken);
}

/// <summary>Coordinates provider-owned history with generic retry and tool-loop middleware.</summary>
public interface IProviderConversationHistory
{
    ValueTask HistoryReplacedAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options,
        string reason,
        CancellationToken cancellationToken);

    void MarkProjectionCovered(IReadOnlyList<ChatMessage> samplingMessages);

    string? BeginAttempt();

    ValueTask AbortAttemptAsync(string? attemptId, CancellationToken cancellationToken);

    void EndAttempt(string? attemptId);

    OpaqueProviderHistorySnapshot CaptureOpaqueSnapshot();

    bool TryEstimateActiveContextTokens(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options,
        out long tokens)
    {
        tokens = 0;
        return false;
    }
}

/// <summary>Creates a provider-owned history session over a Core-owned opaque sink.</summary>
public interface IProviderHistorySessionFactory
{
    IProviderConversationHistory CreateSession(
        ProviderConversationIdentity conversationIdentity,
        OpaqueProviderHistorySnapshot snapshot,
        IReadOnlyList<ChatMessage> coveredMessages,
        IProviderHistorySink? sink);
}

/// <summary>Provider-neutral lifecycle phases at which native history may be compacted.</summary>
public enum ProviderCompactionPhase
{
    PreTurn,
    MidTurn,
    Manual,
    Reactive
}

/// <summary>Opaque provider-native input captured for a compaction backend.</summary>
public sealed record ProviderNativeCompactionInput(
    IReadOnlyList<ProviderHistoryItem> Items,
    int CoveredMessageCount,
    string? CoveredThroughTurnId);

/// <summary>Opaque provider-native history produced by a compaction backend.</summary>
public sealed record ProviderNativeCompactionReplacement(
    string Protocol,
    IReadOnlyList<ProviderHistoryItem> Items,
    int CoveredMessageCount,
    string? CoveredThroughTurnId,
    long EstimatedTokensAfter);

/// <summary>Bridges Core-owned compaction policy to provider-owned native history.</summary>
public interface IProviderCompactionBridge
{
    ValueTask<ProviderNativeCompactionInput> CaptureInputAsync(
        ProviderCompactionPhase phase,
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options,
        CancellationToken cancellationToken);

    ValueTask ReplaceAsync(
        ProviderNativeCompactionReplacement replacement,
        CancellationToken cancellationToken);

    long EstimateContextTokens(
        ProviderNativeCompactionInput snapshot,
        IReadOnlyList<ChatMessage> pendingTail,
        ChatOptions? options);
}
