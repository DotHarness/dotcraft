using System.Globalization;
using DotCraft.Sessions;

namespace DotCraft.SessionImport;

internal sealed class SessionImportWorkspaceState
{
    private SessionImportWorkspaceState(IReadOnlyList<ThreadSummary> summaries)
    {
        var threads = new Dictionary<string, ThreadSummary>(StringComparer.OrdinalIgnoreCase);
        foreach (var summary in summaries)
            threads[summary.Id] = summary;
        Threads = threads;
    }

    public IReadOnlyDictionary<string, ThreadSummary> Threads { get; }

    public static async Task<SessionImportWorkspaceState> LoadAsync(
        ISessionService sessions,
        SessionIdentity identity,
        CancellationToken ct)
    {
        var summaries = await sessions.FindThreadsAsync(
            identity,
            includeArchived: true,
            crossChannelOrigins: null,
            ct: ct,
            includeSubAgents: true,
            scope: ThreadDiscoveryScope.Workspace).ConfigureAwait(false);
        return new SessionImportWorkspaceState(summaries);
    }

    public SessionImportLedgerDocument RebuildLedger()
    {
        var document = new SessionImportLedgerDocument();
        foreach (var summary in Threads.Values)
        {
            if (!summary.Metadata.TryGetValue(SessionImportIdentity.SourceMetadataKey, out var source)
                || !SessionImportSources.IsKnown(source)
                || !summary.Metadata.TryGetValue(SessionImportIdentity.SessionIdMetadataKey, out var sourceId)
                || string.IsNullOrWhiteSpace(sourceId))
            {
                continue;
            }

            var importedAt = summary.Metadata.TryGetValue(SessionImportIdentity.ImportedAtMetadataKey, out var raw)
                && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                    ? parsed
                    : summary.CreatedAt;
            document.Upsert(new SessionImportLedgerRecord
            {
                Source = source,
                SourceId = sourceId,
                ThreadId = summary.Id,
                ImportedAt = importedAt,
                TurnCount = summary.TurnCount,
                Title = summary.DisplayName
            });
        }

        return document;
    }
}
