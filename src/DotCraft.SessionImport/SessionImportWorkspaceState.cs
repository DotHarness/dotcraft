using System.Globalization;
using System.Text.Json;
using DotCraft.Sessions;

namespace DotCraft.SessionImport;

internal sealed class SessionImportWorkspaceState
{
    private const string ExternalCliSessionsMetadataKey = "dotcraft.externalCliSessions";

    private SessionImportWorkspaceState(IReadOnlyList<ThreadSummary> summaries)
    {
        var threads = new Dictionary<string, ThreadSummary>(StringComparer.OrdinalIgnoreCase);
        foreach (var summary in summaries)
            threads[summary.Id] = summary;
        Threads = threads;
        ExternalCliSessionIds = summaries
            .SelectMany(static summary => ReadExternalCliSessionIds(summary.Metadata))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyDictionary<string, ThreadSummary> Threads { get; }

    public IReadOnlySet<string> ExternalCliSessionIds { get; }

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

    private static IEnumerable<string> ReadExternalCliSessionIds(IReadOnlyDictionary<string, string> metadata)
    {
        if (!metadata.TryGetValue(ExternalCliSessionsMetadataKey, out var raw))
            return [];
        try
        {
            return JsonSerializer.Deserialize<ExternalCliSession[]>(raw, JsonSerializerOptions.Web)?
                .Select(static session => session.SessionId)
                .OfType<string>()
                .ToArray() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed record ExternalCliSession(string? SessionId);
}
