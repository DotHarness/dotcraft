using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Dreams;

namespace DotCraft.Protocol.AppServer;

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
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.DreamsStatus, HandleDreamsStatusAsync);
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.DreamsRun, HandleDreamsRunAsync);
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.DreamsCreate, HandleDreamsCreateAsync);
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.DreamsGet, HandleDreamsGetAsync);
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.DreamsList, HandleDreamsListAsync);
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.DreamsCancel, HandleDreamsCancelAsync);
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.DreamsArchive, HandleDreamsArchiveAsync);
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.DreamsApply, HandleDreamsApplyAsync);
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.DreamsDiscard, HandleDreamsDiscardAsync);
    }

    private Task<object?> HandleDreamsStatusAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        EnsureDreamsAvailable();
        ValidateEmptyObjectParams(msg, DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.DreamsStatus);
        return Task.FromResult<object?>(BuildDreamsStatusResult());
    }

    private async Task<object?> HandleDreamsRunAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        EnsureDreamsAvailable();
        ValidateEmptyObjectParams(msg, DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.DreamsRun);
        await dreamsService!.RequestRunAsync(cancellationToken: ct).ConfigureAwait(false);
        return BuildDreamsStatusResult();
    }

    private async Task<object?> HandleDreamsCreateAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        EnsureDreamsAvailable();
        var p = msg.Params.HasValue && msg.Params.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? new DreamsCreateParams()
            : AppServerParams.Get<DreamsCreateParams>(msg);
        if (p.ThreadLookbackCount.HasValue && p.ThreadLookbackCount.Value <= 0)
            throw AppServerErrors.InvalidParams("'threadLookbackCount' must be a positive integer.");

        var state = await dreamsService!.RequestRunAsync(
                new DreamsRunRequest(p.ThreadIds, p.ThreadLookbackCount, p.Instructions, p.Model),
                ct)
            .ConfigureAwait(false);
        return new DreamsRunResult
        {
            Run = ToDreamRunWire(state),
            ActiveDreamStoreId = dreamStore?.GetActiveStoreId()
        };
    }

    private Task<object?> HandleDreamsGetAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        EnsureDreamsAvailable();
        var p = AppServerParams.Get<DreamsRunIdParams>(msg);
        var state = dreamsService!.LoadRun(NormalizeDreamRunId(p.RunId));
        if (state == null)
            throw AppServerErrors.InvalidParams("Dream run not found.");
        return Task.FromResult<object?>(new DreamsRunResult
        {
            Run = ToDreamRunWire(state),
            ActiveDreamStoreId = dreamStore?.GetActiveStoreId(),
            Preview = BuildDreamsRunPreview(state)
        });
    }

    private Task<object?> HandleDreamsListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        EnsureDreamsAvailable();
        var p = msg.Params.HasValue && msg.Params.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? new DreamsListParams()
            : AppServerParams.Get<DreamsListParams>(msg);
        return Task.FromResult<object?>(new DreamsListResult
        {
            Runs = dreamsService!.ListRuns(p.IncludeArchived)
                .Select(ToDreamRunWire)
                .ToList()
        });
    }

    private async Task<object?> HandleDreamsCancelAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        EnsureDreamsAvailable();
        var p = AppServerParams.Get<DreamsRunIdParams>(msg);
        var state = await dreamsService!.CancelRunAsync(NormalizeDreamRunId(p.RunId), ct).ConfigureAwait(false);
        if (state == null)
            throw AppServerErrors.InvalidParams("Dream run not found.");
        return new DreamsRunResult { Run = ToDreamRunWire(state), ActiveDreamStoreId = dreamStore?.GetActiveStoreId() };
    }

    private Task<object?> HandleDreamsArchiveAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        EnsureDreamsAvailable();
        var p = AppServerParams.Get<DreamsRunIdParams>(msg);
        var state = dreamsService!.ArchiveRun(NormalizeDreamRunId(p.RunId));
        if (state == null)
            throw AppServerErrors.InvalidParams("Dream run not found.");
        return Task.FromResult<object?>(new DreamsRunResult { Run = ToDreamRunWire(state), ActiveDreamStoreId = dreamStore?.GetActiveStoreId() });
    }

    private Task<object?> HandleDreamsApplyAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        EnsureDreamsAvailable();
        var p = AppServerParams.Get<DreamsRunIdParams>(msg);
        DreamsRunState? state;
        try
        {
            state = dreamsService!.ApplyRun(NormalizeDreamRunId(p.RunId));
        }
        catch (InvalidOperationException ex)
        {
            throw AppServerErrors.InvalidParams(ex.Message);
        }
        if (state == null)
            throw AppServerErrors.InvalidParams("Dream run not found.");
        AppServerContextInvalidation.MarkMemory(contextPageManager);
        appConfigMonitor?.NotifyChanged(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.DreamsApply, [ConfigChangeRegions.Memory]);
        return Task.FromResult<object?>(new DreamsRunResult { Run = ToDreamRunWire(state), ActiveDreamStoreId = dreamStore?.GetActiveStoreId() });
    }

    private Task<object?> HandleDreamsDiscardAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        EnsureDreamsAvailable();
        var p = AppServerParams.Get<DreamsRunIdParams>(msg);
        var state = dreamsService!.DiscardRun(NormalizeDreamRunId(p.RunId));
        if (state == null)
            throw AppServerErrors.InvalidParams("Dream run not found.");
        return Task.FromResult<object?>(new DreamsRunResult { Run = ToDreamRunWire(state), ActiveDreamStoreId = dreamStore?.GetActiveStoreId() });
    }

    private void EnsureDreamsAvailable()
    {
        if (dreamsService == null || string.IsNullOrWhiteSpace(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.DreamsStatus);
    }

    private DreamsStatusResult BuildDreamsStatusResult()
    {
        var config = appConfigMonitor?.Current.Dreams ?? new DreamsConfig();
        var state = dreamsService!.LoadLatestState();
        var running = state?.Status == DreamsRunStatuses.Running && !state.EndedAt.HasValue;
        return new DreamsStatusResult
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
            LastRun = state == null ? null : ToDreamRunWire(state)
        };
    }

    private static DreamsRunStateWire ToDreamRunWire(DreamsRunState state) => new()
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
        EvidenceThreadIds = state.EvidenceThreadIds,
        WrittenPaths = state.WrittenPaths,
        ThreadId = state.ThreadId,
        TurnId = state.TurnId,
        TurnIds = state.TurnIds,
        Trigger = state.Trigger,
        Message = state.Message,
        Usage = state.Usage,
        InputManifestPath = state.InputManifestPath
    };

    private DreamsRunPreviewWire? BuildDreamsRunPreview(DreamsRunState state)
    {
        if (dreamStore == null || string.IsNullOrWhiteSpace(state.OutputStoreId))
            return null;

        var activeStoreId = dreamStore.GetActiveStoreId();
        return new DreamsRunPreviewWire
        {
            ActiveStoreId = activeStoreId,
            OutputStoreId = state.OutputStoreId,
            ActiveIndexMarkdown = string.IsNullOrWhiteSpace(activeStoreId) ? string.Empty : dreamStore.ReadIndex(activeStoreId),
            OutputIndexMarkdown = dreamStore.ReadIndex(state.OutputStoreId),
            ActiveTopicPaths = string.IsNullOrWhiteSpace(activeStoreId)
                ? []
                : dreamStore.ListTopicFiles(activeStoreId).Select(static topic => topic.Path).ToList(),
            OutputTopicPaths = dreamStore.ListTopicFiles(state.OutputStoreId).Select(static topic => topic.Path).ToList()
        };
    }

    private static string NormalizeDreamRunId(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
            throw AppServerErrors.InvalidParams("'runId' is required.");
        return runId.Trim();
    }

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
