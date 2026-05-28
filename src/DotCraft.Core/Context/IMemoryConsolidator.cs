using Microsoft.Extensions.AI;

namespace DotCraft.Context;

/// <summary>
/// Consolidates completed conversation history into durable workspace memory.
/// </summary>
public interface IMemoryConsolidator
{
    /// <summary>
    /// Consolidates the given model-visible history snapshot.
    /// </summary>
    Task<MemoryConsolidationResult> ConsolidateAsync(
        IReadOnlyList<ChatMessage> messagesToArchive,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Consolidates memory from a captured prompt request snapshot when one is available.
/// </summary>
public interface IMemoryForkConsolidator : IMemoryConsolidator
{
    /// <summary>
    /// Consolidates using a cache-safe prompt fork when possible, otherwise falls back.
    /// </summary>
    Task<MemoryConsolidationResult> ConsolidateAsync(
        IReadOnlyList<ChatMessage> messagesToArchive,
        PromptRequestSnapshot? snapshot,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The high-level outcome of a memory consolidation attempt.
/// </summary>
public enum MemoryConsolidationOutcome
{
    /// <summary>
    /// Durable memory files were changed.
    /// </summary>
    Succeeded,

    /// <summary>
    /// The attempt completed but produced no durable memory change.
    /// </summary>
    Skipped,

    /// <summary>
    /// The attempt failed before a reliable result was produced.
    /// </summary>
    Failed
}

/// <summary>
/// Result returned by a memory consolidation attempt.
/// </summary>
public sealed record MemoryConsolidationResult
{
    /// <summary>
    /// Consolidation outcome.
    /// </summary>
    public MemoryConsolidationOutcome Outcome { get; init; }

    /// <summary>
    /// Whether MEMORY.md was updated.
    /// </summary>
    public bool MemoryWritten { get; init; }

    /// <summary>
    /// Whether HISTORY.md was appended.
    /// </summary>
    public bool HistoryWritten { get; init; }

    /// <summary>
    /// Optional diagnostic reason for skipped or failed attempts.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Returns a success result.
    /// </summary>
    public static MemoryConsolidationResult Succeeded(bool memoryWritten, bool historyWritten) => new()
    {
        Outcome = MemoryConsolidationOutcome.Succeeded,
        MemoryWritten = memoryWritten,
        HistoryWritten = historyWritten
    };

    /// <summary>
    /// Returns a skipped result.
    /// </summary>
    public static MemoryConsolidationResult Skipped(string? message = null) => new()
    {
        Outcome = MemoryConsolidationOutcome.Skipped,
        Message = message
    };

    /// <summary>
    /// Returns a failed result.
    /// </summary>
    public static MemoryConsolidationResult Failed(string? message = null) => new()
    {
        Outcome = MemoryConsolidationOutcome.Failed,
        Message = message
    };
}
