using DotCraft.Contributions;

namespace DotCraft.Memory;

/// <summary>
/// Admits or rejects each file-tool write to a <c>readWrite</c> attached memory store before it reaches disk.
/// Every registered gate must admit a write; a rejection's reason is shown to the model.
/// </summary>
public interface IMemoryWriteGate : IContributionContract
{
    ValueTask<MemoryWriteDecision> EvaluateAsync(MemoryWriteRequest request, CancellationToken cancellationToken);
}

public enum MemoryWriteOperation
{
    Create,
    Edit,
    Delete
}

/// <param name="RelativePath">The file's path relative to the store, with forward slashes.</param>
/// <param name="PreviousContent">The file's content before the write, or null for a new file.</param>
/// <param name="Content">The content the write would leave, or null for a delete.</param>
public sealed record MemoryWriteRequest(
    string StoreId,
    string RelativePath,
    MemoryWriteOperation Operation,
    string? PreviousContent,
    string? Content,
    string? ThreadId,
    string? TurnId);

public sealed record MemoryWriteDecision(bool Admitted, string? Reason = null)
{
    public static MemoryWriteDecision Admit { get; } = new(true);

    public static MemoryWriteDecision Reject(string reason) => new(false, reason);
}
