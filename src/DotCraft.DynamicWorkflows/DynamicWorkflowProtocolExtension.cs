using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.AppServer;
using DotCraft.Protocol;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.DynamicWorkflows;

public sealed class DynamicWorkflowProtocolExtension(
    DynamicWorkflowService workflows,
    DynamicWorkflowProjectionService projection) : IAppServerContractExtension
{
    public IReadOnlyCollection<string> Methods { get; } =
    [
        "workflow/run/list", "workflow/run/read", "workflow/run/pause",
        "workflow/run/stop", "workflow/run/resume"
    ];

    public IReadOnlyCollection<IRpcMethodDescriptor> ContractMethods { get; } =
    [
        Contract.AppServerRpc.WorkflowRunList,
        Contract.AppServerRpc.WorkflowRunRead,
        Contract.AppServerRpc.WorkflowRunPause,
        Contract.AppServerRpc.WorkflowRunStop,
        Contract.AppServerRpc.WorkflowRunResume
    ];

    public void ContributeCapabilities(AppServerCapabilityBuilder builder) =>
        builder.SetExtension("dynamicWorkflows", new Contract.DynamicWorkflowCapabilities
        {
            Version = 1, List = true, Read = true, Pause = true, Stop = true, Resume = true, Notifications = true
        });

    public async Task<object?> HandleContractAsync(
        IRpcMethodDescriptor descriptor,
        object requestParams,
        AppServerIncomingMessage message,
        AppServerExtensionContext context)
    {
        try
        {
            return descriptor.Name switch
            {
                "workflow/run/list" => await ListAsync((Contract.WorkflowRunListParams)requestParams, context.CancellationToken),
                "workflow/run/read" => await ReadAsync((Contract.WorkflowRunParams)requestParams, context.CancellationToken),
                "workflow/run/pause" => await ControlAsync((Contract.WorkflowRunParams)requestParams, pause: true, context.CancellationToken),
                "workflow/run/stop" => await ControlAsync((Contract.WorkflowRunParams)requestParams, pause: false, context.CancellationToken),
                "workflow/run/resume" => await ResumeAsync((Contract.WorkflowRunResumeParams)requestParams, context.CancellationToken),
                _ => throw AppServerErrors.MethodNotFound(message.Method ?? descriptor.Name)
            };
        }
        catch (KeyNotFoundException)
        {
            throw NotFound();
        }
        catch (InvalidOperationException ex) when (descriptor.Name == "workflow/run/resume")
        {
            throw AppServerErrors.WorkflowRun("workflow_resume_unavailable", "Workflow run cannot be resumed.", ex.Message);
        }
        catch (InvalidOperationException ex) when (descriptor.Name is "workflow/run/pause" or "workflow/run/stop")
        {
            throw AppServerErrors.WorkflowRun("workflow_run_state_conflict", "Workflow run state does not allow this operation.", ex.Message);
        }
    }

    private async Task<Contract.WorkflowRunListResult> ListAsync(Contract.WorkflowRunListParams p, CancellationToken ct)
    {
        var limit = p.Limit.IsSet ? p.Limit.Value : 50;
        if (limit is < 1 or > 200) throw AppServerErrors.InvalidParams("limit must be between 1 and 200.");
        var cursor = p.Cursor.IsSet ? p.Cursor.Value : null;
        if (!string.IsNullOrWhiteSpace(cursor) && !DynamicWorkflowProjectionService.IsValidCursor(cursor))
            throw AppServerErrors.InvalidParams("cursor is invalid.");
        var runs = await projection.ListAsync(p.ThreadId, limit + 1, cursor, ct).ConfigureAwait(false);
        var page = runs.Take(limit).ToArray();
        return new Contract.WorkflowRunListResult
        {
            Runs = page.Select(ToSummary).ToArray(),
            NextCursor = runs.Count > limit ? DynamicWorkflowProjectionService.EncodeCursor(page[^1]) : default
        };
    }

    private async Task<Contract.WorkflowRunReadResult> ReadAsync(Contract.WorkflowRunParams p, CancellationToken ct) =>
        new() { Run = ToContract(await RequireOwnedAsync(p.ThreadId, p.RunId, ct).ConfigureAwait(false)) };

    private async Task<Contract.WorkflowRunReadResult> ControlAsync(Contract.WorkflowRunParams p, bool pause, CancellationToken ct)
    {
        var source = await RequireOwnedAsync(p.ThreadId, p.RunId, ct).ConfigureAwait(false);
        if (pause)
        {
            if (source.Status != DynamicWorkflowStatuses.Running && source.Status != DynamicWorkflowStatuses.Paused)
                throw new InvalidOperationException($"Cannot pause a {source.Status} workflow run.");
            await workflows.PauseAsync(p.RunId, ct).ConfigureAwait(false);
        }
        else
        {
            if (source.Status is not (DynamicWorkflowStatuses.Running or DynamicWorkflowStatuses.Paused or DynamicWorkflowStatuses.Stopped))
                throw new InvalidOperationException($"Cannot stop a {source.Status} workflow run.");
            await workflows.StopRunAsync(p.RunId, ct).ConfigureAwait(false);
        }
        var result = await ReadAsync(p, ct).ConfigureAwait(false);
        var expected = pause ? DynamicWorkflowStatuses.Paused : DynamicWorkflowStatuses.Stopped;
        if (!string.Equals(result.Run.Status, expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"Workflow transitioned to {result.Run.Status} before the requested control completed.");
        return result;
    }

    private async Task<Contract.WorkflowRunResumeResult> ResumeAsync(Contract.WorkflowRunResumeParams p, CancellationToken ct)
    {
        await RequireOwnedAsync(p.ThreadId, p.RunId, ct).ConfigureAwait(false);
        JsonNode? args = p.Args.IsSet && p.Args.Value is { } value ? JsonNode.Parse(value.GetRawText()) : null;
        var run = await workflows.ResumeFromClientAsync(p.RunId, p.ThreadId, args, ct).ConfigureAwait(false);
        var view = await RequireOwnedAsync(p.ThreadId, run.RunId, ct).ConfigureAwait(false);
        return new Contract.WorkflowRunResumeResult { SourceRunId = p.RunId, Run = ToContract(view) };
    }

    private async Task<DynamicWorkflowRunView> RequireOwnedAsync(string threadId, string runId, CancellationToken ct) =>
        await projection.ReadAsync(threadId, runId, ct).ConfigureAwait(false) ?? throw NotFound();

    private static AppServerException NotFound() =>
        AppServerErrors.WorkflowRun("workflow_run_not_found", "Workflow run was not found.");

    private static Contract.WorkflowRunSummary ToSummary(DynamicWorkflowRunView value) => new()
    {
        RunId = value.RunId, ThreadId = value.ThreadId, Name = value.Name, Description = value.Description,
        Status = value.Status, CreatedAt = value.CreatedAt,
        StartedAt = Optional(value.StartedAt), CompletedAt = Optional(value.CompletedAt),
        ResumedFromRunId = Optional(value.ResumedFromRunId), Totals = ToContract(value.Totals), Controls = ToContract(value.Controls)
    };

    private static Contract.WorkflowRunView ToContract(DynamicWorkflowRunView value) => new()
    {
        RunId = value.RunId, ThreadId = value.ThreadId, Name = value.Name, Description = value.Description,
        Status = value.Status, CreatedAt = value.CreatedAt,
        StartedAt = Optional(value.StartedAt), CompletedAt = Optional(value.CompletedAt), ResumedFromRunId = Optional(value.ResumedFromRunId),
        Totals = ToContract(value.Totals), Controls = ToContract(value.Controls),
        Phases = value.Phases.Select(ToContract).ToArray(), UnphasedAgents = value.UnphasedAgents.Select(ToContract).ToArray(),
        Result = value.Result == null ? default : Optional<JsonElement?>.FromValue(JsonSerializer.SerializeToElement(value.Result)),
        Error = Optional(value.Error)
    };

    private static Contract.WorkflowRunTotals ToContract(DynamicWorkflowTotals value) => new()
    {
        AgentCount = value.AgentCount, QueuedCount = value.QueuedCount, RunningCount = value.RunningCount,
        CompletedCount = value.CompletedCount, FailedCount = value.FailedCount, StoppedCount = value.StoppedCount,
        ReplayedCount = value.ReplayedCount, InputTokens = value.InputTokens, OutputTokens = value.OutputTokens,
        ToolCallCount = value.ToolCallCount
    };

    private static Contract.WorkflowRunControls ToContract(DynamicWorkflowControls value) =>
        new() { CanPause = value.CanPause, CanStop = value.CanStop, CanResume = value.CanResume };

    private static Contract.WorkflowPhaseView ToContract(DynamicWorkflowPhaseView value) => new()
    {
        Name = value.Name, Detail = Optional(value.Detail), Status = value.Status, Agents = value.Agents.Select(ToContract).ToArray()
    };

    private static Contract.WorkflowAgentView ToContract(DynamicWorkflowAgentView value) => new()
    {
        OperationId = value.OperationId, Label = value.Label, Phase = Optional(value.Phase), Status = value.Status,
        ChildThreadId = Optional(value.ChildThreadId), InputTokens = value.InputTokens, OutputTokens = value.OutputTokens,
        ToolCallCount = value.ToolCallCount, RequestedAt = value.RequestedAt, StartedAt = Optional(value.StartedAt),
        CompletedAt = Optional(value.CompletedAt), Replayed = value.Replayed
    };

    private static Optional<string> Optional(string? value) => value == null ? default : Optional<string>.FromValue(value);
    private static Optional<DateTimeOffset> Optional(DateTimeOffset? value) => value == null ? default : Optional<DateTimeOffset>.FromValue(value.Value);
}
