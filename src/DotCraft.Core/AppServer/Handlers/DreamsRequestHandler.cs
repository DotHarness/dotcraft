using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Dreams;
using Contract = DotCraft.Protocol.AppServer;
using DreamsRunState = DotCraft.Dreams.DreamsRunState;

namespace DotCraft.AppServer;

/// <summary>
/// Handles the <c>dreams/*</c> wire methods (spec: core/dreams): status, run, create, get, list,
/// cancel, archive, apply, discard. Dreams-local mapping/validation helpers stay with the handler;
/// cross-domain memory-page invalidation goes through
/// <see cref="AppServerContextInvalidation"/>.
/// </summary>
internal sealed class DreamsRequestHandler(
    DreamsService? dreamsService,
    DreamStore? dreamStore,
    IAppConfigMonitor? appConfigMonitor,
    string? workspaceCraftPath,
    IContextPageManager? contextPageManager) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(global::DotCraft.Protocol.AppServer.AppServerRpc.DreamsStatus, HandleDreamsStatusAsync);
        table.Map(global::DotCraft.Protocol.AppServer.AppServerRpc.DreamsRun, HandleDreamsRunAsync);
        table.Map(global::DotCraft.Protocol.AppServer.AppServerRpc.DreamsCreate, HandleDreamsCreateAsync);
        table.Map(global::DotCraft.Protocol.AppServer.AppServerRpc.DreamsGet, HandleDreamsGetAsync);
        table.Map(global::DotCraft.Protocol.AppServer.AppServerRpc.DreamsList, HandleDreamsListAsync);
        table.Map(global::DotCraft.Protocol.AppServer.AppServerRpc.DreamsCancel, HandleDreamsCancelAsync);
        table.Map(global::DotCraft.Protocol.AppServer.AppServerRpc.DreamsArchive, HandleDreamsArchiveAsync);
        table.Map(global::DotCraft.Protocol.AppServer.AppServerRpc.DreamsApply, HandleDreamsApplyAsync);
        table.Map(global::DotCraft.Protocol.AppServer.AppServerRpc.DreamsDiscard, HandleDreamsDiscardAsync);
    }

    private Task<object?> HandleDreamsStatusAsync(AppServerTypedRequest<Contract.DreamsStatusParams> request, CancellationToken ct)
    {
        _ = ct;
        EnsureDreamsAvailable();
        ValidateEmptyObjectParams(request.Message, DotCraft.Protocol.AppServer.AppServerMethodNames.DreamsStatus);
        return Task.FromResult<object?>(BuildDreamsStatusResult());
    }

    private async Task<object?> HandleDreamsRunAsync(AppServerTypedRequest<Contract.DreamsRunParams> request, CancellationToken ct)
    {
        EnsureDreamsAvailable();
        ValidateEmptyObjectParams(request.Message, DotCraft.Protocol.AppServer.AppServerMethodNames.DreamsRun);
        await dreamsService!.RequestRunAsync(cancellationToken: ct).ConfigureAwait(false);
        return BuildDreamsStatusResult();
    }

    private async Task<object?> HandleDreamsCreateAsync(AppServerTypedRequest<Contract.DreamsCreateParams> request, CancellationToken ct)
    {
        EnsureDreamsAvailable();
        var p = request.Params;
        var threadLookbackCount = ValueOrDefault(p.ThreadLookbackCount);
        if (threadLookbackCount.HasValue && threadLookbackCount.Value <= 0)
            throw AppServerErrors.InvalidParams("'threadLookbackCount' must be a positive integer.");

        var state = await dreamsService!.RequestRunAsync(
                new DreamsRunRequest(
                    ValueOrDefault(p.ThreadIds)?.ToList(),
                    threadLookbackCount,
                    ValueOrDefault(p.Instructions),
                    ValueOrDefault(p.Model)),
                ct)
            .ConfigureAwait(false);
        return new Contract.DreamsRunResult
        {
            Run = ToDreamRunContract(state),
            ActiveDreamStoreId = dreamStore?.GetActiveStoreId()
        };
    }

    private Task<object?> HandleDreamsGetAsync(AppServerTypedRequest<Contract.DreamsRunIdParams> request, CancellationToken ct)
    {
        _ = ct;
        EnsureDreamsAvailable();
        var p = request.Params;
        var state = dreamsService!.LoadRun(NormalizeDreamRunId(ValueOrDefault(p.RunId)));
        if (state == null)
            throw AppServerErrors.InvalidParams("Dream run not found.");
        return Task.FromResult<object?>(new Contract.DreamsRunResult
        {
            Run = ToDreamRunContract(state),
            ActiveDreamStoreId = dreamStore?.GetActiveStoreId(),
            Preview = BuildDreamsRunPreview(state)
        });
    }

    private Task<object?> HandleDreamsListAsync(AppServerTypedRequest<Contract.DreamsListParams> request, CancellationToken ct)
    {
        _ = ct;
        EnsureDreamsAvailable();
        var p = request.Params;
        return Task.FromResult<object?>(new Contract.DreamsListResult
        {
            Runs = new DotCraft.Protocol.Optional<IReadOnlyList<Contract.DreamsRunState>>(
                dreamsService!.ListRuns(ValueOrDefault(p.IncludeArchived))
                    .Select(ToDreamRunContract)
                    .ToArray())
        });
    }

    private async Task<object?> HandleDreamsCancelAsync(AppServerTypedRequest<Contract.DreamsRunIdParams> request, CancellationToken ct)
    {
        EnsureDreamsAvailable();
        var p = request.Params;
        var state = await dreamsService!.CancelRunAsync(NormalizeDreamRunId(ValueOrDefault(p.RunId)), ct).ConfigureAwait(false);
        if (state == null)
            throw AppServerErrors.InvalidParams("Dream run not found.");
        return new Contract.DreamsRunResult { Run = ToDreamRunContract(state), ActiveDreamStoreId = dreamStore?.GetActiveStoreId() };
    }

    private Task<object?> HandleDreamsArchiveAsync(AppServerTypedRequest<Contract.DreamsRunIdParams> request, CancellationToken ct)
    {
        _ = ct;
        EnsureDreamsAvailable();
        var p = request.Params;
        var state = dreamsService!.ArchiveRun(NormalizeDreamRunId(ValueOrDefault(p.RunId)));
        if (state == null)
            throw AppServerErrors.InvalidParams("Dream run not found.");
        return Task.FromResult<object?>(new Contract.DreamsRunResult { Run = ToDreamRunContract(state), ActiveDreamStoreId = dreamStore?.GetActiveStoreId() });
    }

    private Task<object?> HandleDreamsApplyAsync(AppServerTypedRequest<Contract.DreamsRunIdParams> request, CancellationToken ct)
    {
        _ = ct;
        EnsureDreamsAvailable();
        var p = request.Params;
        DreamsRunState? state;
        try
        {
            state = dreamsService!.ApplyRun(NormalizeDreamRunId(ValueOrDefault(p.RunId)));
        }
        catch (InvalidOperationException ex)
        {
            throw AppServerErrors.InvalidParams(ex.Message);
        }
        if (state == null)
            throw AppServerErrors.InvalidParams("Dream run not found.");
        AppServerContextInvalidation.MarkMemory(contextPageManager);
        appConfigMonitor?.NotifyChanged(DotCraft.Protocol.AppServer.AppServerMethodNames.DreamsApply, [ConfigChangeRegions.Memory]);
        return Task.FromResult<object?>(new Contract.DreamsRunResult { Run = ToDreamRunContract(state), ActiveDreamStoreId = dreamStore?.GetActiveStoreId() });
    }

    private Task<object?> HandleDreamsDiscardAsync(AppServerTypedRequest<Contract.DreamsRunIdParams> request, CancellationToken ct)
    {
        _ = ct;
        EnsureDreamsAvailable();
        var p = request.Params;
        var state = dreamsService!.DiscardRun(NormalizeDreamRunId(ValueOrDefault(p.RunId)));
        if (state == null)
            throw AppServerErrors.InvalidParams("Dream run not found.");
        return Task.FromResult<object?>(new Contract.DreamsRunResult { Run = ToDreamRunContract(state), ActiveDreamStoreId = dreamStore?.GetActiveStoreId() });
    }

    private void EnsureDreamsAvailable()
    {
        if (dreamsService == null || string.IsNullOrWhiteSpace(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(DotCraft.Protocol.AppServer.AppServerMethodNames.DreamsStatus);
    }

    private Contract.DreamsStatusResult BuildDreamsStatusResult()
    {
        var config = appConfigMonitor?.Current.Dreams ?? new DreamsConfig();
        var state = dreamsService!.LoadLatestState();
        var running = state?.Status == DreamsRunStatuses.Running && !state.EndedAt.HasValue;
        return new Contract.DreamsStatusResult
        {
            Enabled = config.Enabled,
            Interval = FormatTimeSpanForWire(config.Interval),
            ThreadLookbackCount = config.ThreadLookbackCount,
            AutoApply = config.AutoApply,
            HistoryTailChars = config.HistoryTailChars,
            MinCompletedTurnsSinceLastRun = config.MinCompletedTurnsSinceLastRun,
            NextRunAt = state?.NextRunAt,
            Running = running,
            ActiveDreamStoreId = dreamStore?.GetActiveStoreId(),
            LastRun = state == null ? null : ToDreamRunContract(state)
        };
    }

    private static Contract.DreamsRunState ToDreamRunContract(DreamsRunState state) => new()
    {
        Id = state.Id,
        Status = state.Status,
        StartedAt = state.StartedAt,
        EndedAt = state.EndedAt,
        ProcessedThreadCount = state.ProcessedThreadCount,
        CandidateThreadCount = state.CandidateThreadCount,
        DreamWritten = state.DreamWritten,
        HistoryWritten = state.HistoryWritten,
        TopicFilesWritten = state.TopicFilesWritten,
        TopicFilesDeleted = state.TopicFilesDeleted,
        EvidenceSearchCount = state.EvidenceSearchCount,
        EvidenceReadCount = state.EvidenceReadCount,
        OutputStoreId = state.OutputStoreId,
        ReviewStatus = state.ReviewStatus,
        AutoApplied = state.AutoApplied,
        ErrorType = state.ErrorType,
        EvidenceThreadIds = new DotCraft.Protocol.Optional<IReadOnlyList<string>>(state.EvidenceThreadIds),
        WrittenPaths = new DotCraft.Protocol.Optional<IReadOnlyList<string>>(state.WrittenPaths),
        ThreadId = state.ThreadId,
        TurnId = state.TurnId,
        TurnIds = new DotCraft.Protocol.Optional<IReadOnlyList<string>>(state.TurnIds),
        Trigger = state.Trigger,
        Message = state.Message,
        Usage = state.Usage is null ? null : AppServerContractMapper.ToContract(state.Usage),
        InputManifestPath = state.InputManifestPath
    };

    private Contract.DreamsRunPreview? BuildDreamsRunPreview(DreamsRunState state)
    {
        if (dreamStore == null || string.IsNullOrWhiteSpace(state.OutputStoreId))
            return null;

        var activeStoreId = dreamStore.GetActiveStoreId();
        return new Contract.DreamsRunPreview
        {
            ActiveStoreId = activeStoreId,
            OutputStoreId = state.OutputStoreId,
            ActiveIndexMarkdown = string.IsNullOrWhiteSpace(activeStoreId) ? string.Empty : dreamStore.ReadIndex(activeStoreId),
            OutputIndexMarkdown = dreamStore.ReadIndex(state.OutputStoreId),
            ActiveTopicPaths = new DotCraft.Protocol.Optional<IReadOnlyList<string>>(
                string.IsNullOrWhiteSpace(activeStoreId)
                    ? []
                    : dreamStore.ListTopicFiles(activeStoreId).Select(static topic => topic.Path).ToArray()),
            OutputTopicPaths = new DotCraft.Protocol.Optional<IReadOnlyList<string>>(
                dreamStore.ListTopicFiles(state.OutputStoreId).Select(static topic => topic.Path).ToArray())
        };
    }

    private static string NormalizeDreamRunId(string? runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
            throw AppServerErrors.InvalidParams("'runId' is required.");
        return runId.Trim();
    }

    private static T? ValueOrDefault<T>(DotCraft.Protocol.Optional<T> value) =>
        value.IsSet ? value.Value : default;

    private static void ValidateEmptyObjectParams(AppServerIncomingMessage msg, string method)
    {
        if (msg.Params.HasValue
            && msg.Params.Value.ValueKind is not JsonValueKind.Null
                and not JsonValueKind.Object
                and not JsonValueKind.Undefined)
        {
            throw AppServerErrors.InvalidParams($"{method} accepts omitted, null, or empty-object params.");
        }
    }

    private static string FormatTimeSpanForWire(TimeSpan value)
    {
        var normalized = value <= TimeSpan.Zero ? TimeSpan.FromHours(24) : value;
        var totalSeconds = (long)Math.Round(normalized.TotalSeconds);
        var hours = totalSeconds / 3600;
        var minutes = (totalSeconds % 3600) / 60;
        var seconds = totalSeconds % 60;
        return $"{hours:00}:{minutes:00}:{seconds:00}";
    }
}
