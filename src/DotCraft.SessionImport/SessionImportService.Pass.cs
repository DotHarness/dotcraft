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
        Contract.ImportSelection Selection,
        IReadOnlyList<Contract.ImportItemReference>? Items);

    private PassRequest CreateSyncRequest()
    {
        var settings = _settingsStore.ReadUserSettings();
        var sources = settings.Sources
            .Select(sourceId => _sources.FirstOrDefault(source => source.SourceId == sourceId))
            .OfType<ISessionImportSource>()
            .ToArray();
        return new PassRequest(SessionImportIdentity.NewImportId(), SyncTrigger, sources, settings.Selection, null);
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

    private async Task<Contract.ImportCompletedNotification?> ExecutePassAsync(PassRequest request, CancellationToken ct)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var outcomes = new List<Contract.ImportOutcome>();
        try
        {
            using var globalSync = request.Trigger == SyncTrigger ? _history.BeginGlobalSync(SyncInterval) : null;
            var context = await CreateContextAsync(ct).ConfigureAwait(false);
            foreach (var source in request.Sources)
            {
                var detected = source.IsAvailable && ImportCategories.Includes(request.Selection, "sessions", "workspace")
                    ? await DetectSourceAsync(source, context, retainSessions: true, ct).ConfigureAwait(false) : [];
                var selected = detected
                    .Where(entry => entry.Session is not null
                        && (request.Items is null || request.Items.Any(i => i.Source == source.SourceId && i.SourceId == entry.File.SourceId)))
                    .ToArray();
                var setup = (_setup?.Scan(source.SourceId) ?? []).Where(item =>
                    ImportCategories.Includes(request.Selection, item.Candidate.Category, item.Candidate.Scope)
                    && (item.Candidate.Scope != "user" || request.Trigger != SyncTrigger || globalSync != null)
                    && (request.Items == null ? item.Candidate.State == "new" : request.Items.Any(i => i.Source == source.SourceId && i.SourceId == item.Candidate.SourceId))).ToArray();
                for (var index = 0; index < selected.Length; index++)
                {
                    var expected = request.Items?.FirstOrDefault(i => i.Source == source.SourceId && i.SourceId == selected[index].File.SourceId);
                    outcomes.Add(expected != null && expected.Fingerprint != selected[index].Candidate.Fingerprint
                        ? SetupImportService.Outcome(selected[index].Candidate, "failed", "import_source_changed")
                        : await ImportAsync(selected[index], context, ct).ConfigureAwait(false));
                    Progress?.Invoke(new Contract.ImportProgressNotification
                    {
                        ImportId = request.ImportId,
                        Source = source.SourceId,
                        Completed = index + 1,
                        Total = selected.Length + setup.Length
                    });
                }
                for (var index = 0; index < setup.Length; index++)
                {
                    var item = setup[index];
                    var expected = request.Items?.FirstOrDefault(i => i.Source == source.SourceId && i.SourceId == item.Candidate.SourceId)?.Fingerprint ?? item.Candidate.Fingerprint;
                    outcomes.Add(await _setup!.InstallAsync(item, expected, ct).ConfigureAwait(false));
                    Progress?.Invoke(new Contract.ImportProgressNotification { ImportId = request.ImportId, Source = source.SourceId, Completed = selected.Length + index + 1, Total = selected.Length + setup.Length });
                }
            }
            if (request.Items != null)
                foreach (var missing in request.Items.Where(i => !outcomes.Any(o => o.Source == i.Source && o.SourceId == i.SourceId)))
                    outcomes.Add(new Contract.ImportOutcome { Source = missing.Source, SourceId = missing.SourceId, Status = "failed", ErrorCode = "import_source_changed", Error = ImportDiagnostics.Fallback("import_source_changed") });

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
            outcomes.Add(new Contract.ImportOutcome { Source = request.Sources.FirstOrDefault()?.SourceId ?? "", SourceId = "pass", Status = "failed", ErrorCode = "import_pass_failed", Error = ImportDiagnostics.Fallback("import_pass_failed") });
        }

        var result = new Contract.ImportCompletedNotification
        {
            ImportId = request.ImportId,
            Trigger = request.Trigger,
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            Outcomes = outcomes
        };
        try { _history.Save(result); }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Import history could not be written for {ImportId}", request.ImportId);
            outcomes.Add(new Contract.ImportOutcome { Source = "", SourceId = "history", Status = "failed", ErrorCode = "import_history_write_failed", Error = ImportDiagnostics.Fallback("import_history_write_failed") });
        }
        return result;
    }

    private async Task<Contract.ImportOutcome> ImportAsync(DetectedSession detected, DetectionContext context, CancellationToken ct)
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

    private async Task<Contract.ImportOutcome> ImportNewAsync(
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

    private async Task<Contract.ImportOutcome> AppendAsync(
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

    private static Contract.ImportOutcome Outcome(
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
