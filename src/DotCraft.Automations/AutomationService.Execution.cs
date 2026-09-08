using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Sessions;
using Microsoft.Extensions.Logging;
using DotCraft.Channels;
using System.Collections.Concurrent;

namespace DotCraft.Automations;

public sealed partial class AutomationService
{
    public IAppConfigMonitor? AppConfigMonitor { get; set; }
    private readonly SemaphoreSlim _executionSlots = new(Math.Max(1, config.MaxConcurrentTasks));
    private readonly ConcurrentDictionary<string, AutomationOutcome> _outcomes = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _threadGates = new();
    private sealed class AutomationOutcome
    {
        public string? TurnId;
        public string? Summary;
        public string? Memory;
        public bool? Important;
    }
    /// <summary>Records optional semantics for the current attempt without setting execution status.</summary>
    public void ReportOutcome(string threadId, string turnId, string? summary, bool? important, string? memory)
    {
        if (!_outcomes.TryGetValue(threadId, out var outcome) || outcome.TurnId != turnId)
            throw new InvalidOperationException("automation.noActiveRun");
        outcome.Summary = summary; outcome.Important = important; outcome.Memory = memory;
    }
    private async Task ExecuteAsync(AutomationDefinition definition, AutomationRun run, bool manual, CancellationToken ct)
    {
        var acquired = false;
        var outcome = new AutomationOutcome();
        SemaphoreSlim? threadGate = null;
        try
        {
            await SaveRunAsync(run);
            if (!manual && (await ReadAsync(definition.Id, ct)).Status != "active")
                throw new OperationCanceledException("automation.pausedBeforeDispatch");
            var client = _client!;
            string threadId;
            if (definition.ExecutionMode == "thread")
            {
                threadId = definition.TargetThreadId!;
                var targetGate = _threadGates.GetOrAdd(threadId, _ => new SemaphoreSlim(1, 1));
                await targetGate.WaitAsync(ct); threadGate = targetGate;
                while (true)
                {
                    var thread = await client.TryGetThreadAsync(threadId, ct);
                    if (thread == null || thread.Status == ThreadStatus.Archived) throw new InvalidOperationException("automation.targetUnavailable");
                    if (!thread.Turns.Any(t => t.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput)) break;
                    if (!manual && (await ReadAsync(definition.Id, ct)).Status != "active") throw new OperationCanceledException("automation.pausedBeforeDispatch");
                    await Task.Delay(TimeSpan.FromSeconds(2), ct);
                }
            }
            else
            {
                await _executionSlots.WaitAsync(ct); acquired = true;
                var threadConfig = new ThreadConfiguration();
                if (!string.IsNullOrWhiteSpace(definition.AgentProfileId))
                {
                    var profiles = new AgentProfileStore(client.DataPath);
                    var profile = profiles.Read(definition.AgentProfileId);
                    threadConfig = profile.ProviderPreference == null
                        ? profiles.ResolveProfileConfiguration(definition.AgentProfileId)
                        : profiles.ResolveProfileConfiguration(definition.AgentProfileId, AppConfigMonitor?.Current
                            ?? throw new InvalidOperationException("automation.profileConfigurationUnavailable"));
                    threadConfig.AgentProfileId = definition.AgentProfileId;
                }
                threadConfig.Mode = "agent";
                threadConfig.ApprovalPolicy = ApprovalPolicy.AutoApprove;
                threadConfig.RequireApprovalOutsideWorkspace = definition.ApprovalPolicy != "fullAuto";
                threadId = await client.CreateThreadAsync("automations", "run-" + run.Id, threadConfig, ct, definition.Name);
                run = run with { ThreadId = threadId };
                await SaveRunAsync(run);
                if (definition.WorkspaceMode == "worktree")
                    run = run with { Worktree = await client.EnsureRunWorktreeAsync(threadId, run.Id, ct) };
            }
            if (!acquired) { await _executionSlots.WaitAsync(ct); acquired = true; }
            run = run with { ThreadId = threadId, Status = "running", StartedAt = DateTimeOffset.UtcNow };
            _outcomes[threadId] = outcome;
            await SaveRunAsync(run);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (config.TurnTimeout > TimeSpan.Zero) timeout.CancelAfter(config.TurnTimeout);
            var memoryPath = Path.Combine(_store.DirectoryFor(definition.Id), "memory.md");
            var memory = File.Exists(memoryPath) ? await File.ReadAllTextAsync(memoryPath, ct) : "";
            var prompt = definition.Prompt + "\n\nYou may call Automation(action: report) to record a summary, whether anything important changed, and replacement memory for the next run. Ordinary turn completion ends this run."
                + (string.IsNullOrWhiteSpace(memory) ? "" : "\n\nPrevious automation memory:\n" + memory);
            using var channelScope = definition.Origin == null ? null : ChannelSessionScope.Set(new ChannelSessionInfo
            { Channel = definition.Origin.Channel, UserId = definition.Origin.UserId, GroupId = definition.Origin.GroupId, DefaultDeliveryTarget = definition.Origin.DeliveryTarget });
            var completed = false;
            await foreach (var evt in client.SubmitTurnAsync(threadId, prompt, timeout.Token,
                new TurnTriggerInfo { Kind = "automation", Label = definition.Name, RefId = definition.Id },
                async token =>
                {
                    if (!manual && (await ReadAsync(definition.Id, token)).Status != "active")
                        throw new OperationCanceledException("automation.pausedBeforeDispatch");
                }))
            {
                if (evt.TurnId != null && evt.TurnId != run.TurnId)
                { run = run with { TurnId = evt.TurnId }; outcome.TurnId = evt.TurnId; await SaveRunAsync(run); }
                if (evt.EventType == SessionEventType.TurnFailed) throw new InvalidOperationException(evt.TurnFailedPayload?.Error ?? "automation.turnFailed");
                if (evt.EventType == SessionEventType.TurnCancelled) throw new OperationCanceledException("automation.turnCancelled");
                if (evt.EventType == SessionEventType.TurnCompleted)
                {
                    completed = true;
                    var summary = evt.TurnPayload?.Items.LastOrDefault(i => i.Type == ItemType.AgentMessage)?.AsAgentMessage?.Text;
                    run = run with { Summary = summary, Status = "succeeded" };
                }
            }
            if (!completed) throw new InvalidOperationException("automation.turnEndedWithoutResult");
            run = run with { Summary = outcome.Summary ?? run.Summary };
            if (outcome.Memory != null) await File.WriteAllTextAsync(memoryPath, outcome.Memory, ct);
            else if (!string.IsNullOrWhiteSpace(run.Summary)) await File.WriteAllTextAsync(memoryPath, run.Summary, ct);
        }
        catch (OperationCanceledException ex)
        { run = run with { Status = ct.IsCancellationRequested ? "interrupted" : "cancelled", Error = ex.Message }; }
        catch (Exception ex)
        { logger.LogWarning(ex, "Automation {Id} failed", definition.Id); run = run with { Status = "failed", Error = ex.Message }; }
        finally
        {
            run = run with { CompletedAt = DateTimeOffset.UtcNow };
            try
            {
                await SaveRunAsync(run);
                var deliver = DeliverAsync != null && (run.Status != "succeeded" || definition.NotificationPolicy != "failures"
                    && (definition.NotificationPolicy != "important" || outcome.Important != false));
                if (deliver)
                {
                    try
                    {
                        using var deliveryTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                        await DeliverAsync!(definition, run, deliveryTimeout.Token).WaitAsync(deliveryTimeout.Token);
                        run = run with { DeliveryStatus = "sent" };
                    }
                    catch (Exception ex) { run = run with { DeliveryStatus = "failed", DeliveryError = ex.Message }; }
                }
                else run = run with { DeliveryStatus = "skipped" };
                await SaveRunAsync(run);
                await _gate.WaitAsync(CancellationToken.None);
                try
                {
                    if (!manual && definition.Schedule.Kind == "at" && _definitions.TryGetValue(definition.Id, out var current)
                        && current.Status == "active" && current.NextRunAt == null && current.Schedule.HasSameOccurrences(definition.Schedule))
                    {
                        var ended = CompleteOneShot(current);
                        await _store.SaveAsync(ended, CancellationToken.None); _definitions[ended.Id] = ended;
                    }
                }
                finally { _gate.Release(); }
                await NotifyAsync(await ReadAsync(definition.Id), definition.Id, false);
            }
            catch (Exception ex) { logger.LogError(ex, "Failed to persist automation completion {Id}", definition.Id); }
            finally
            {
                await _gate.WaitAsync(CancellationToken.None);
                try { _running.Remove(definition.Id); }
                finally { _gate.Release(); }
                if (run.ThreadId != null) _outcomes.TryRemove(run.ThreadId, out _);
                if (acquired) _executionSlots.Release();
                threadGate?.Release();
            }
        }
    }
}
