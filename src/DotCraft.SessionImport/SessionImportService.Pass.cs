using DotCraft.Protocol;
using DotCraft.Sessions;
using Microsoft.Extensions.Logging;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.SessionImport;

public sealed partial class SessionImportService
{
    private static class OutcomeStatuses
    {
        public const string Imported = "imported";
        public const string Appended = "appended";
        public const string Deferred = "deferred";
        public const string Skipped = "skipped";
        public const string Failed = "failed";
    }

    private sealed record PassRequest(
        string ImportId,
        string Trigger,
        IReadOnlyList<ISessionImportSource> Sources,
        IReadOnlySet<string>? SessionIds);

    private PassRequest CreateSyncRequest()
    {
        var sources = _userSettings.Sources
            .Select(sourceId => _sources.FirstOrDefault(source => source.SourceId == sourceId))
            .OfType<ISessionImportSource>()
            .Where(static source => source.IsAvailable)
            .ToArray();
        return new PassRequest(SessionImportIdentity.NewImportId(), SyncTrigger, sources, SessionIds: null);
    }

    private Task StartPassLoop(PassRequest request)
    {
        var token = _passLifetime.Token;
        return Task.Run(() => RunPassLoopAsync(request, token), CancellationToken.None);
    }

    /// <summary>Runs a pass, then any sync pass queued meanwhile; the final completion is announced after the slot is released.</summary>
    private async Task RunPassLoopAsync(PassRequest request, CancellationToken token)
    {
        while (true)
        {
            var completed = await ExecutePassAsync(request, token).ConfigureAwait(false);
            bool finished;
            lock (_passLock)
            {
                finished = !_syncQueued || token.IsCancellationRequested;
                _syncQueued = false;
                if (finished)
                    _activePass = null;
                else
                    request = CreateSyncRequest();
            }

            if (completed is not null)
                Completed?.Invoke(completed);
            if (finished)
                return;
        }
    }

    private async Task<Contract.ImportSessionsCompletedNotification?> ExecutePassAsync(PassRequest request, CancellationToken ct)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var outcomes = new List<Contract.ImportSessionOutcome>();
        try
        {
            var context = await CreateContextAsync(ct).ConfigureAwait(false);
            foreach (var source in request.Sources)
            {
                var detected = await DetectSourceAsync(source, context, retainSessions: true, ct).ConfigureAwait(false);
                var selected = detected
                    .Where(entry => entry.Session is not null
                        && (request.SessionIds is null || request.SessionIds.Contains(entry.File.SourceId)))
                    .ToArray();
                for (var index = 0; index < selected.Length; index++)
                {
                    outcomes.Add(await ImportAsync(selected[index], context, ct).ConfigureAwait(false));
                    Progress?.Invoke(new Contract.ImportSessionsProgressNotification
                    {
                        ImportId = request.ImportId,
                        Source = source.SourceId,
                        Completed = index + 1,
                        Total = selected.Length
                    });
                }
            }

            await SaveDetectionUpdatesAsync(context).ConfigureAwait(false);
            if (request.Trigger == SyncTrigger)
                await UpdateLedgerAsync(context.Workspace, document => document.LastSyncAt = startedAt).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Session import pass {ImportId} stopped early", request.ImportId);
        }

        return new Contract.ImportSessionsCompletedNotification
        {
            ImportId = request.ImportId,
            Trigger = request.Trigger,
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            Outcomes = outcomes
        };
    }

    private async Task<Contract.ImportSessionOutcome> ImportAsync(DetectedSession detected, DetectionContext context, CancellationToken ct)
    {
        var session = detected.Session!;
        try
        {
            return detected.Record is null
                ? await ImportNewAsync(session, detected.File, context, ct).ConfigureAwait(false)
                : await AppendAsync(session, detected.File, detected.Record, context, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Session import failed for {Source} session {SessionId}", session.Source, session.SourceId);
            return Outcome(session, OutcomeStatuses.Failed, detected.Record?.ThreadId, "import_failed", "The session could not be imported.");
        }
    }

    private async Task<Contract.ImportSessionOutcome> ImportNewAsync(
        ImportedSession session,
        SessionImportCandidateFile file,
        DetectionContext context,
        CancellationToken ct)
    {
        var importedAt = DateTimeOffset.UtcNow;
        var result = await Sessions.ImportThreadAsync(new ThreadImportRequest
        {
            Identity = _identity,
            ThreadId = SessionImportIdentity.ThreadIdFor(session.Source, session.SourceId),
            DisplayName = session.Title,
            Cwd = WorkspacePathMatcher.IsSameDirectory(session.Cwd, _options.WorkspacePath) ? null : session.Cwd,
            Metadata = SessionImportIdentity.Metadata(session, importedAt),
            Turns = session.Turns,
            EstimatedTokens = ExternalSessionText.EstimateTokens(session.Turns)
        }, ct).ConfigureAwait(false);

        var thread = result.Thread;
        var record = result.AlreadyExisted
            ? new SessionImportLedgerRecord
            {
                Source = session.Source,
                SourceId = session.SourceId,
                SourcePath = session.SourcePath,
                ThreadId = thread.Id,
                ImportedAt = importedAt,
                TurnCount = thread.Turns.Count > 0
                    ? thread.Turns.Count
                    : context.Workspace.Threads.TryGetValue(thread.Id, out var summary) ? summary.TurnCount : 0,
                Title = thread.DisplayName ?? session.Title
            }
            : new SessionImportLedgerRecord
            {
                Source = session.Source,
                SourceId = session.SourceId,
                SourcePath = session.SourcePath,
                ThreadId = thread.Id,
                ContentSha256 = session.ContentSha256,
                SourceModifiedAt = file.ModifiedAt,
                ImportedAt = importedAt,
                TurnCount = session.Turns.Count,
                Title = session.Title
            };
        await UpdateLedgerAsync(context.Workspace, document => document.Upsert(record)).ConfigureAwait(false);
        return result.AlreadyExisted
            ? Outcome(session, OutcomeStatuses.Skipped, thread.Id, "import_thread_exists", "A thread for this session already exists.")
            : Outcome(session, OutcomeStatuses.Imported, thread.Id);
    }

    private async Task<Contract.ImportSessionOutcome> AppendAsync(
        ImportedSession session,
        SessionImportCandidateFile file,
        SessionImportLedgerRecord record,
        DetectionContext context,
        CancellationToken ct)
    {
        try
        {
            await Sessions.AppendImportedTurnsAsync(new ThreadImportAppendRequest
            {
                ThreadId = record.ThreadId,
                ExistingTurns = session.Turns.Take(record.TurnCount).ToArray(),
                NewTurns = session.Turns.Skip(record.TurnCount).ToArray(),
                EstimatedTokens = ExternalSessionText.EstimateTokens(session.Turns)
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            _logger.LogInformation(ex, "Session import deferred {Source} session {SessionId}", session.Source, session.SourceId);
            return Outcome(
                session,
                OutcomeStatuses.Deferred,
                record.ThreadId,
                ThreadImportRefusedException.ErrorCode,
                "The imported thread changed in DotCraft and no longer receives updates from its source.");
        }

        var updated = record with
        {
            SourcePath = session.SourcePath,
            ContentSha256 = session.ContentSha256,
            SourceModifiedAt = file.ModifiedAt,
            TurnCount = session.Turns.Count,
            Title = session.Title
        };
        await UpdateLedgerAsync(context.Workspace, document => document.Upsert(updated)).ConfigureAwait(false);
        return Outcome(session, OutcomeStatuses.Appended, record.ThreadId);
    }

    private static Contract.ImportSessionOutcome Outcome(
        ImportedSession session,
        string status,
        string? threadId,
        string? errorCode = null,
        string? error = null) => new()
    {
        Source = session.Source,
        SourceId = session.SourceId,
        Status = status,
        ThreadId = threadId is null ? default : Optional<string>.FromValue(threadId),
        Title = Optional<string>.FromValue(session.Title),
        ErrorCode = errorCode is null ? default : Optional<string>.FromValue(errorCode),
        Error = error is null ? default : Optional<string>.FromValue(error)
    };
}
