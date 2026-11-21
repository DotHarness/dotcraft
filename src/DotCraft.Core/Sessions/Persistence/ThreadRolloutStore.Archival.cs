namespace DotCraft.Sessions;

internal sealed partial class ThreadRolloutStore
{
    public async Task<RolloutAppendResult> ArchiveStoredThreadAsync(
        string threadId, string existingPath, long previousOffset, CancellationToken ct)
    {
        var path = GetExpectedPath(threadId, archived: true);
        var record = new ThreadRolloutRecord
        {
            Kind = RolloutKinds.ThreadStatusChanged,
            ThreadStatusChanged = new RolloutThreadStatusChangedPayload
            {
                ThreadId = threadId,
                Status = ThreadStatus.Archived
            }
        };
        ct.ThrowIfCancellationRequested();
        File.Move(existingPath, path);
        try
        {
            return await AppendRecordsAsync(threadId, path, [record], CancellationToken.None);
        }
        catch
        {
            await _writer.CloseAsync(threadId, discardPending: true);
            if (new FileInfo(path).Length > previousOffset)
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read | FileShare.Delete);
                stream.SetLength(previousOffset);
                stream.Flush(flushToDisk: true);
            }
            File.Move(path, existingPath);
            throw;
        }
    }
}
