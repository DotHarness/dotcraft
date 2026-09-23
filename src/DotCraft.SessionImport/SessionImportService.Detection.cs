using DotCraft.Sessions;
using Microsoft.Extensions.Logging;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.SessionImport;

public sealed partial class SessionImportService
{
    private sealed record DetectionContext(
        SessionImportWorkspaceState Workspace,
        SessionImportLedgerDocument Ledger,
        SessionImportScope Scope)
    {
        public List<SessionImportLedgerRecord> RecomputedHashes { get; } = [];

        public List<SessionImportLedgerRecord> RefreshedModifiedTimes { get; } = [];
    }

    private sealed record DetectedSession(
        SessionImportCandidateFile File,
        Contract.ImportSessionCandidate Candidate,
        SessionImportLedgerRecord? Record,
        ImportedSession? Session);

    private async Task<DetectionContext> CreateContextAsync(CancellationToken ct)
    {
        var workspace = await SessionImportWorkspaceState.LoadAsync(Sessions, _identity, ct).ConfigureAwait(false);
        var ledger = await ReadLedgerAsync(workspace, ct).ConfigureAwait(false);
        var scope = new SessionImportScope
        {
            WorkspaceRoot = _options.WorkspacePath,
            Now = DateTimeOffset.UtcNow,
            MaxAge = TimeSpan.FromDays(_options.Config.MaxSessionAgeDays),
            MaxSessions = _options.Config.MaxSessionsPerSource,
            ExternalCliSessionIds = workspace.ExternalCliSessionIds
        };
        return new DetectionContext(workspace, ledger, scope);
    }

    private async Task<IReadOnlyList<DetectedSession>> DetectSourceAsync(
        ISessionImportSource source,
        DetectionContext context,
        bool retainSessions,
        CancellationToken ct)
    {
        IReadOnlyList<SessionImportCandidateFile> files;
        try
        {
            files = await source.EnumerateCandidatesAsync(context.Scope, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Session import could not list {Source} sessions", source.SourceId);
            return [];
        }

        var detected = new List<DetectedSession>();
        foreach (var file in files)
        {
            var record = context.Ledger.Find(source.SourceId, file.SourceId);
            if (record is { ContentSha256: not null, SourceModifiedAt: { } modifiedAt }
                && SessionImportLedger.SameInstant(modifiedAt, file.ModifiedAt))
            {
                continue;
            }

            ImportedSession? session;
            try
            {
                session = await source.LoadAsync(file, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Session import skipped {Source} session {SessionId} for this pass", source.SourceId, file.SourceId);
                continue;
            }

            if (session is null)
                continue;

            var state = await ClassifyAsync(file, session, record, context, ct).ConfigureAwait(false);
            var candidate = new Contract.ImportSessionCandidate
            {
                Source = session.Source,
                SourceId = session.SourceId,
                SourcePath = session.SourcePath,
                Title = session.Title,
                Cwd = session.Cwd,
                UpdatedAt = session.UpdatedAt,
                TurnCount = session.Turns.Count,
                State = state
            };
            detected.Add(new DetectedSession(file, candidate, record, retainSessions && IsImportable(state) ? session : null));
        }

        return detected.OrderByDescending(static entry => entry.Candidate.UpdatedAt).ToArray();
    }

    private async Task<string> ClassifyAsync(
        SessionImportCandidateFile file,
        ImportedSession session,
        SessionImportLedgerRecord? record,
        DetectionContext context,
        CancellationToken ct)
    {
        if (record is null)
            return CandidateStates.New;

        if (record.ContentSha256 is null)
        {
            if (session.Turns.Count > record.TurnCount)
                return await CanExtendAsync(record, context, ct).ConfigureAwait(false) ? CandidateStates.Changed : CandidateStates.Deferred;
            context.RecomputedHashes.Add(record with
            {
                SourcePath = session.SourcePath,
                ContentSha256 = session.ContentSha256,
                SourceModifiedAt = file.ModifiedAt
            });
            return CandidateStates.Current;
        }

        if (string.Equals(record.ContentSha256, session.ContentSha256, StringComparison.OrdinalIgnoreCase))
        {
            context.RefreshedModifiedTimes.Add(record with { SourceModifiedAt = file.ModifiedAt });
            return CandidateStates.Current;
        }

        return session.Turns.Count >= record.TurnCount && await CanExtendAsync(record, context, ct).ConfigureAwait(false)
            ? CandidateStates.Changed
            : CandidateStates.Deferred;
    }

    private async Task<bool> CanExtendAsync(SessionImportLedgerRecord record, DetectionContext context, CancellationToken ct)
    {
        if (!context.Workspace.Threads.TryGetValue(record.ThreadId, out var summary)
            || summary.Status != ThreadStatus.Active
            || summary.Runtime is { Running: true } or { Busy: true }
            || summary.TurnCount != record.TurnCount)
        {
            return false;
        }

        return await HasOnlyImportedTurnsAsync(summary, ct).ConfigureAwait(false);
    }

    private async Task<bool> HasOnlyImportedTurnsAsync(ThreadSummary summary, CancellationToken ct)
    {
        if (summary.TurnCount == 0)
            return true;
        try
        {
            var page = await Sessions
                .ListThreadTurnsAsync(summary.Id, null, summary.TurnCount, ThreadHistorySortDirection.Ascending, ct)
                .ConfigureAwait(false);
            return page.Data.All(static turn =>
                string.Equals(turn.OriginChannel, ThreadImportConstants.ChannelName, StringComparison.Ordinal));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Session import could not read the turns of thread {ThreadId}", summary.Id);
            return false;
        }
    }

    private async Task<SessionImportLedgerDocument> ReadLedgerAsync(SessionImportWorkspaceState workspace, CancellationToken ct)
    {
        await _ledgerGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _ledger.TryRead() ?? workspace.RebuildLedger();
        }
        finally
        {
            _ledgerGate.Release();
        }
    }

    /// <summary>Re-reads, changes, and atomically rewrites the ledger so concurrent detections and passes merge per record.</summary>
    private async Task UpdateLedgerAsync(SessionImportWorkspaceState workspace, Action<SessionImportLedgerDocument> update)
    {
        await _ledgerGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var document = _ledger.TryRead() ?? workspace.RebuildLedger();
            update(document);
            _ledger.Write(document);
        }
        finally
        {
            _ledgerGate.Release();
        }
    }

    private async Task SaveDetectionUpdatesAsync(DetectionContext context)
    {
        if (context.RecomputedHashes.Count == 0 && context.RefreshedModifiedTimes.Count == 0)
            return;
        try
        {
            await UpdateLedgerAsync(context.Workspace, document =>
            {
                foreach (var record in context.RecomputedHashes)
                {
                    if (document.Find(record.Source, record.SourceId) is { ContentSha256: null })
                        document.Upsert(record);
                }

                foreach (var refreshed in context.RefreshedModifiedTimes)
                {
                    if (document.Find(refreshed.Source, refreshed.SourceId) is { } current
                        && string.Equals(current.ContentSha256, refreshed.ContentSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        document.Upsert(current with { SourceModifiedAt = refreshed.SourceModifiedAt });
                    }
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Session import could not update the import ledger");
        }
    }
}
