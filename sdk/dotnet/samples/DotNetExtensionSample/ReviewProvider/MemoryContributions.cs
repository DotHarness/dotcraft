using DotCraft.Memory;

namespace Acme.ReviewCore;

internal sealed class ReviewMemoryWriteGate : IMemoryWriteGate
{
    public ValueTask<MemoryWriteDecision> EvaluateAsync(
        MemoryWriteRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(request.Operation == MemoryWriteOperation.Delete
            ? MemoryWriteDecision.Reject("Shared review memory cannot be deleted.")
            : MemoryWriteDecision.Admit);
}
