namespace DotCraft.Sessions;

internal sealed partial class ThreadRolloutStore
{
    public async Task<RolloutAppendResult> ArchiveStoredThreadAsync(
        SessionThread thread,
        string existingPath,
        CancellationToken ct)
    {
        await _writer.CloseAsync(thread.Id, ct);
        var path = MoveToTargetPath(thread.Id, archived: true, existingPath);
        var record = new ThreadRolloutRecord
        {
            Kind = RolloutKinds.ThreadStatusChanged,
            Timestamp = thread.LastActiveAt,
            ThreadStatusChanged = new RolloutThreadStatusChangedPayload
            {
                ThreadId = thread.Id,
                Status = ThreadStatus.Archived,
                LastActiveAt = thread.LastActiveAt
            }
        };
        return await AppendRecordsAsync(thread.Id, path, [record], ct);
    }
}
