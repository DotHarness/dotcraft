namespace DotCraft.Sessions;

internal sealed partial class ThreadRolloutStore
{
    public async Task<RolloutAppendResult> ArchiveStoredThreadAsync(
        string threadId, string existingPath, CancellationToken ct)
    {
        var path = GetExpectedPath(threadId, archived: true);
        File.Move(existingPath, path);
        var record = new ThreadRolloutRecord
        {
            Kind = RolloutKinds.ThreadStatusChanged,
            ThreadStatusChanged = new RolloutThreadStatusChangedPayload
            {
                ThreadId = threadId,
                Status = ThreadStatus.Archived
            }
        };
        return await AppendRecordsAsync(threadId, path, [record], ct);
    }
}
