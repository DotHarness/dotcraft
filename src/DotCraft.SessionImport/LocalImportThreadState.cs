using System.Globalization;
using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Persistence;
using DotCraft.Sessions;
using Microsoft.Data.Sqlite;

namespace DotCraft.SessionImport;

internal sealed class LocalImportThreadState(string workspacePath, string dataPath)
{
    private readonly SessionImportLedgerDocument? _ledger = new SessionImportLedger(dataPath).TryRead();

    public string WorkspacePath { get; } = workspacePath;
    public string DataPath { get; } = dataPath;

    public async Task<SessionImportLedgerRecord?> FindAsync(string source, string sourceId, CancellationToken ct)
    {
        if (_ledger?.Find(source, sourceId) is { } record)
            return record;
        var threadId = SessionImportIdentity.ThreadIdFor(source, sourceId);
        var thread = await ReadThreadAsync(threadId, ct).ConfigureAwait(false);
        if (thread == null
            || !thread.Metadata.TryGetValue(SessionImportIdentity.SourceMetadataKey, out var importedSource)
            || importedSource != source
            || !thread.Metadata.TryGetValue(SessionImportIdentity.SessionIdMetadataKey, out var importedId)
            || !string.Equals(importedId, sourceId, StringComparison.OrdinalIgnoreCase))
            return null;

        var importedAt = thread.Metadata.TryGetValue(SessionImportIdentity.ImportedAtMetadataKey, out var raw)
            && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed : thread.CreatedAt;
        return new SessionImportLedgerRecord
        {
            Source = source,
            SourceId = sourceId,
            ThreadId = thread.Id,
            ImportedAt = importedAt,
            TurnCount = thread.TurnCount,
            Title = thread.DisplayName
        };
    }

    public async Task<bool> CanExtendAsync(SessionImportLedgerRecord record, CancellationToken ct)
    {
        var thread = await ReadThreadAsync(record.ThreadId, ct).ConfigureAwait(false);
        return thread is { Status: ThreadStatus.Active }
            && thread.TurnCount == record.TurnCount
            && thread.Turns.All(turn => turn.OriginChannel == ThreadImportConstants.ChannelName);
    }

    private async Task<SessionThread?> ReadThreadAsync(string threadId, CancellationToken ct)
    {
        if (threadId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        foreach (var group in new[] { "active", "archived" })
        {
            var path = Path.Combine(DataPath, "threads", group, threadId + ".jsonl");
            try
            {
                if (File.Exists(path))
                {
                    AtomicConfigDocument.RejectLinks(path);
                    return await new SessionRolloutReader().ReadAsync(path, ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (LocalImportConfiguration.IsUnavailable(ex)) { }
        }
        return null;
    }
}
