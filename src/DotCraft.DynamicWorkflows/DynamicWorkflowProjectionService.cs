using System.Text;
using System.Text.Json.Nodes;
using DotCraft.Sessions;

namespace DotCraft.DynamicWorkflows;

public sealed class DynamicWorkflowProjectionService(DynamicWorkflowStore store) : ISessionServiceConsumer
{
    private ISessionService? _sessionService;

    public void SetSessionService(ISessionService service) => _sessionService = service;

    public async Task<IReadOnlyList<DynamicWorkflowRunView>> ListAsync(
        string threadId,
        int limit,
        string? cursor,
        CancellationToken cancellationToken)
    {
        var after = DecodeCursor(cursor);
        var states = new List<DynamicWorkflowRun>();
        foreach (var runId in store.EnumerateRunIds())
        {
            var state = await store.ReadStateAsync(runId, cancellationToken).ConfigureAwait(false);
            if (state != null && string.Equals(state.ParentThreadId, threadId, StringComparison.Ordinal)) states.Add(state);
        }
        var ordered = states.OrderByDescending(static run => run.CreatedAt).ThenByDescending(static run => run.RunId, StringComparer.Ordinal);
        if (after != null)
            ordered = ordered.Where(run => run.CreatedAt < after.Value.CreatedAt
                || (run.CreatedAt == after.Value.CreatedAt && string.CompareOrdinal(run.RunId, after.Value.RunId) < 0))
                .OrderByDescending(static run => run.CreatedAt).ThenByDescending(static run => run.RunId, StringComparer.Ordinal);
        var views = new List<DynamicWorkflowRunView>();
        foreach (var state in ordered.Take(limit))
            views.Add(await ProjectAsync(state, cancellationToken).ConfigureAwait(false));
        return views;
    }

    public async Task<DynamicWorkflowRunView?> ReadAsync(string threadId, string runId, CancellationToken cancellationToken)
    {
        var state = await store.ReadStateAsync(runId, cancellationToken).ConfigureAwait(false);
        if (state == null || !string.Equals(state.ParentThreadId, threadId, StringComparison.Ordinal)) return null;
        return await ProjectAsync(state, cancellationToken).ConfigureAwait(false);
    }

    public static string EncodeCursor(DynamicWorkflowRunView run) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{run.CreatedAt:O}\n{run.RunId}"));

    internal static bool IsValidCursor(string cursor) => DecodeCursor(cursor) != null;

    private static (DateTimeOffset CreatedAt, string RunId)? DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return null;
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('\n', 2);
            return parts.Length == 2 && DateTimeOffset.TryParse(parts[0], out var at) ? (at, parts[1]) : null;
        }
        catch (FormatException) { return null; }
    }

    private async Task<DynamicWorkflowRunView> ProjectAsync(DynamicWorkflowRun state, CancellationToken cancellationToken)
    {
        var journal = await store.ReadJournalAsync(state.RunId, cancellationToken).ConfigureAwait(false);
        var description = state.Description;
        var declared = state.DeclaredPhases.ToList();
        var phaseDetails = new Dictionary<string, string?>(StringComparer.Ordinal);
        var discovered = new List<string>();
        var agents = new Dictionary<string, AgentAccumulator>(StringComparer.Ordinal);

        foreach (var entry in journal)
        {
            var type = entry["type"]?.GetValue<string>();
            var at = entry["at"]?.GetValue<DateTimeOffset>() ?? state.CreatedAt;
            var payload = entry["payload"] as JsonObject;
            if (type == "workflow.meta")
            {
                description = payload?["description"]?.GetValue<string>() ?? description;
                if (declared.Count == 0 && payload?["phases"] is JsonArray phases)
                    declared.AddRange(phases.Select(static phase => phase?.GetValue<string>()).OfType<string>());
                continue;
            }
            if (type == "workflow.phase")
            {
                var name = payload?["name"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    if (!declared.Contains(name, StringComparer.Ordinal) && !discovered.Contains(name, StringComparer.Ordinal)) discovered.Add(name);
                    phaseDetails[name] = payload?["detail"]?.GetValue<string>();
                }
                continue;
            }
            var operationId = payload?["operationId"]?.GetValue<string>();
            if (operationId == null || type == null || !type.StartsWith("agent.", StringComparison.Ordinal)) continue;
            if (!agents.TryGetValue(operationId, out var agent))
                agents[operationId] = agent = new AgentAccumulator(operationId, at);
            agent.Apply(type, payload!, at);
            if (!string.IsNullOrWhiteSpace(agent.Phase)
                && !declared.Contains(agent.Phase, StringComparer.Ordinal)
                && !discovered.Contains(agent.Phase, StringComparer.Ordinal)) discovered.Add(agent.Phase);
        }

        if (state.Status is DynamicWorkflowStatuses.Stopped or DynamicWorkflowStatuses.Cancelled)
        {
            var stoppedAt = state.CompletedAt ?? state.CreatedAt;
            foreach (var agent in agents.Values)
                agent.MarkStopped(stoppedAt);
        }

        foreach (var agent in agents.Values)
            await PopulateSessionMetricsAsync(agent, cancellationToken).ConfigureAwait(false);

        var orderedPhases = declared.Concat(discovered).Distinct(StringComparer.Ordinal).ToArray();
        var phaseViews = orderedPhases.Select(name =>
        {
            var members = agents.Values.Where(agent => string.Equals(agent.Phase, name, StringComparison.Ordinal)).Select(static agent => agent.ToView()).ToArray();
            return new DynamicWorkflowPhaseView
            {
                Name = name,
                Detail = phaseDetails.GetValueOrDefault(name),
                Status = DerivePhaseStatus(members),
                Agents = members
            };
        }).ToArray();
        var unphased = agents.Values.Where(static agent => string.IsNullOrWhiteSpace(agent.Phase)).Select(static agent => agent.ToView()).ToArray();
        var all = agents.Values.Select(static agent => agent.ToView()).ToArray();
        var totals = new DynamicWorkflowTotals
        {
            AgentCount = all.Length,
            QueuedCount = all.Count(static agent => agent.Status == "pending"),
            RunningCount = all.Count(static agent => agent.Status == "running"),
            CompletedCount = all.Count(static agent => agent.Status == "completed"),
            FailedCount = all.Count(static agent => agent.Status == "failed"),
            StoppedCount = all.Count(static agent => agent.Status == "stopped"),
            ReplayedCount = all.Count(static agent => agent.Replayed),
            InputTokens = all.Sum(static agent => agent.InputTokens),
            OutputTokens = all.Sum(static agent => agent.OutputTokens),
            ToolCallCount = all.Sum(static agent => agent.ToolCallCount)
        };
        return new DynamicWorkflowRunView
        {
            RunId = state.RunId,
            ThreadId = state.ParentThreadId,
            Name = state.Name,
            Description = description,
            Status = state.Status,
            CreatedAt = state.CreatedAt,
            StartedAt = state.StartedAt,
            CompletedAt = state.CompletedAt,
            ResumedFromRunId = state.ResumedFromRunId,
            Result = state.Result?.DeepClone(),
            Error = state.Error,
            Totals = totals,
            Controls = ControlsFor(state.Status),
            Phases = phaseViews,
            UnphasedAgents = unphased
        };
    }

    private async Task PopulateSessionMetricsAsync(AgentAccumulator agent, CancellationToken cancellationToken)
    {
        if (agent.ChildThreadId == null || _sessionService == null) return;
        try
        {
            var child = await _sessionService.GetThreadAsync(agent.ChildThreadId, cancellationToken).ConfigureAwait(false);
            agent.InputTokens = child.Turns.Sum(static turn => turn.TokenUsage?.InputTokens ?? 0);
            agent.OutputTokens = child.Turns.Sum(static turn => turn.TokenUsage?.OutputTokens ?? 0);
            agent.ToolCallCount = child.Turns.SelectMany(static turn => turn.Items).Count(static item =>
                item.AsToolCall != null || item.AsMcpToolCall != null || item.AsDynamicToolCall != null);
        }
        catch (KeyNotFoundException) { }
    }

    private static DynamicWorkflowControls ControlsFor(string status) => status switch
    {
        DynamicWorkflowStatuses.Running => new(true, true, false),
        DynamicWorkflowStatuses.Paused => new(false, true, true),
        DynamicWorkflowStatuses.Stopped or DynamicWorkflowStatuses.Failed or DynamicWorkflowStatuses.Succeeded => new(false, false, true),
        _ => new(false, false, false)
    };

    private static string DerivePhaseStatus(IReadOnlyList<DynamicWorkflowAgentView> agents)
    {
        if (agents.Count == 0) return "pending";
        if (agents.Any(static agent => agent.Status == "running")) return "running";
        if (agents.Any(static agent => agent.Status == "failed")) return "failed";
        if (agents.Any(static agent => agent.Status == "stopped")) return "stopped";
        return agents.All(static agent => agent.Status == "completed") ? "completed" : "pending";
    }

    private sealed class AgentAccumulator(string operationId, DateTimeOffset requestedAt)
    {
        public string OperationId { get; } = operationId;
        public string Label { get; private set; } = operationId;
        public string? Phase { get; private set; }
        public string Status { get; private set; } = "pending";
        public string? ChildThreadId { get; private set; }
        public long InputTokens { get; set; }
        public long OutputTokens { get; set; }
        public int ToolCallCount { get; set; }
        public DateTimeOffset RequestedAt { get; private set; } = requestedAt;
        public DateTimeOffset? StartedAt { get; private set; }
        public DateTimeOffset? CompletedAt { get; private set; }
        public bool Replayed { get; private set; }

        public void Apply(string type, JsonObject payload, DateTimeOffset at)
        {
            Label = payload["label"]?.GetValue<string>() ?? Label;
            Phase = payload["phase"]?.GetValue<string>() ?? Phase;
            ChildThreadId = payload["childThreadId"]?.GetValue<string>() ?? ChildThreadId;
            switch (type)
            {
                case "agent.requested": RequestedAt = at; break;
                case "agent.started": Status = "running"; StartedAt = at; break;
                case "agent.completed":
                    Status = "completed"; CompletedAt = at;
                    InputTokens = payload["inputTokens"]?.GetValue<long>() ?? InputTokens;
                    OutputTokens = payload["outputTokens"]?.GetValue<long>() ?? OutputTokens;
                    break;
                case "agent.failed": Status = "failed"; CompletedAt = at; break;
                case "agent.replayed":
                    Status = "completed"; Replayed = true; StartedAt ??= at; CompletedAt = at;
                    InputTokens = payload["inputTokens"]?.GetValue<long>() ?? InputTokens;
                    OutputTokens = payload["outputTokens"]?.GetValue<long>() ?? OutputTokens;
                    break;
            }
        }

        public void MarkStopped(DateTimeOffset at)
        {
            if (Status is not ("pending" or "running")) return;
            Status = "stopped";
            CompletedAt = at;
        }

        public DynamicWorkflowAgentView ToView() => new()
        {
            OperationId = OperationId,
            Label = Label,
            Phase = Phase,
            Status = Status,
            ChildThreadId = ChildThreadId,
            InputTokens = InputTokens,
            OutputTokens = OutputTokens,
            ToolCallCount = ToolCallCount,
            RequestedAt = RequestedAt,
            StartedAt = StartedAt,
            CompletedAt = CompletedAt,
            Replayed = Replayed
        };
    }
}
