using DotCraft.Tools;

namespace DotCraft.Memory;

/// <summary>A write's verdict: a refusal for the model, or the attached store it deletes from when the write empties a memory file.</summary>
internal sealed record MemoryWriteAdmission(string? Refusal, AttachedMemoryStore? DeletesFrom)
{
    public static MemoryWriteAdmission Admitted { get; } = new(null, null);
}

internal sealed class MemoryFileWrites(ThreadMemoryStores? stores, Func<IReadOnlyList<IMemoryWriteGate>>? gates)
{
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(30);

    public string? ReadNote(string fullPath) =>
        stores?.Classify(fullPath) is { } target ? MemoryFiles.ReadNote(target.DirectoryPath, fullPath) : null;

    public string WriteNotice(string fullPath, string content) =>
        stores?.Classify(fullPath) is { } target && MemoryIndex.WriteNotice(target.DirectoryPath, fullPath, content) is { } notice
            ? "\n\n" + notice
            : string.Empty;

    public async Task<IDisposable?> LockAsync(string fullPath) =>
        stores?.Classify(fullPath) is { } target ? await PathAsyncMutex.AcquireDirectoryAsync(target.DirectoryPath) : null;

    public string? ReadOnlyRefusal(string fullPath) =>
        stores?.Classify(fullPath) is { Writable: false, Attached: { } store } ? $"Error: {store.Attachment.Name} is read only." : null;

    /// <param name="before">The file's content before the write, or null when it does not exist.</param>
    public async Task<MemoryWriteAdmission> AdmitAsync(string fullPath, string? before, string after)
    {
        if (stores?.Classify(fullPath) is not { Attached: { } store })
            return MemoryWriteAdmission.Admitted;

        var deletes = before is not null && string.IsNullOrWhiteSpace(after) && !MemoryStore.IsIndexFile(store.DirectoryPath, fullPath);
        var operation = deletes ? MemoryWriteOperation.Delete
            : before is null ? MemoryWriteOperation.Create
            : MemoryWriteOperation.Edit;
        var invocation = ToolInvocationScope.Current;
        var request = new MemoryWriteRequest(
            store.Attachment.Id,
            Path.GetRelativePath(store.DirectoryPath, fullPath).Replace('\\', '/'),
            operation,
            before,
            deletes ? null : after,
            invocation?.ThreadId,
            invocation?.TurnId);
        foreach (var gate in gates?.Invoke() ?? [])
        {
            MemoryWriteDecision decision;
            try
            {
                using var timeout = new CancellationTokenSource(GateTimeout);
                decision = await gate.EvaluateAsync(request, timeout.Token).AsTask().WaitAsync(timeout.Token);
            }
            catch (Exception)
            {
                decision = MemoryWriteDecision.Reject("The shared memory could not accept the write right now.");
            }

            if (!decision.Admitted)
                return new MemoryWriteAdmission($"Error: {store.Attachment.Name} rejected this write: {decision.Reason ?? "no reason given"}", null);
        }

        return deletes ? new MemoryWriteAdmission(null, store) : MemoryWriteAdmission.Admitted;
    }
}
