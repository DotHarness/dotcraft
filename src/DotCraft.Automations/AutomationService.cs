using DotCraft.Automations.Protocol;
using DotCraft.Configuration;
using Microsoft.Extensions.Logging;
using DotCraft.Workspaces;

namespace DotCraft.Automations;

/// <summary>Single lifecycle owner for scheduled agent work.</summary>
public sealed partial class AutomationService(AutomationsConfig config, DotCraftPaths paths, ILogger<AutomationService> logger)
{
    private readonly AutomationStore _store = new(paths.Data.Resolve("automations"));
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, AutomationDefinition> _definitions = new();
    private readonly Dictionary<string, Task> _running = new();
    private IAutomationSessionClient? _client;
    private CancellationTokenSource? _lifetime;
    private Task? _loop;
    private bool _loaded;
    public event Func<AutomationDefinition?, string, bool, Task>? Updated;
    public event Func<AutomationRun, Task>? RunUpdated;
    public Func<AutomationDefinition, AutomationRun, CancellationToken, Task>? DeliverAsync { get; set; }
    public void SetSessionClient(IAutomationSessionClient client) => _client = client;
    public async Task<IReadOnlyList<AutomationDefinition>> ListAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try { await LoadAsync(ct); return _definitions.Values.OrderByDescending(d => d.UpdatedAt).ToArray(); }
        finally { _gate.Release(); }
    }
    public async Task<AutomationDefinition> ReadAsync(string id, CancellationToken ct = default) =>
        (await ListAsync(ct)).FirstOrDefault(d => d.Id == id) ?? throw new KeyNotFoundException("automation.notFound");
    public async Task<AutomationDefinition> CreateAsync(AutomationInput input, AutomationOrigin? origin = null, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        AutomationDefinition definition;
        try
        {
            await LoadAsync(ct);
            var now = DateTimeOffset.UtcNow;
            definition = Build(input) with { Id = Guid.NewGuid().ToString("N"), Version = 1, Origin = origin, CreatedAt = now, UpdatedAt = now,
                NextRunAt = input.Status == "active" ? input.Schedule.Kind == "at" ? input.Schedule.At : input.Schedule.Next(now) : null };
            await _store.SaveAsync(definition, ct);
            _definitions.Add(definition.Id, definition);
        }
        finally { _gate.Release(); }
        await NotifyAsync(definition, definition.Id, false);
        return definition;
    }
    public async Task<AutomationDefinition> UpdateAsync(string id, int expectedVersion, AutomationInput input, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        AutomationDefinition definition;
        try
        {
            await LoadAsync(ct);
            var existing = Get(id);
            if (existing.Version != expectedVersion) throw new InvalidOperationException("automation.versionConflict");
            if (existing.Status == "completed") throw new InvalidOperationException("automation.completedReadOnly");
            var now = DateTimeOffset.UtcNow;
            definition = Build(input) with { Id = id, Version = existing.Version + 1, Origin = existing.Origin, CreatedAt = existing.CreatedAt, UpdatedAt = now,
                NextRunAt = input.Status != "active" ? null : input.Schedule.HasSameOccurrences(existing.Schedule) && existing.Status == "active" ? existing.NextRunAt
                    : input.Schedule.Kind == "at" ? input.Schedule.At : input.Schedule.Next(now) };
            await _store.SaveAsync(definition, ct);
            _definitions[id] = definition;
        }
        finally { _gate.Release(); }
        await NotifyAsync(definition, id, false);
        return definition;
    }
    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await LoadAsync(ct); _ = Get(id);
            if (_running.ContainsKey(id)) throw new InvalidOperationException("automation.running");
            _store.Delete(id); _definitions.Remove(id);
        }
        finally { _gate.Release(); }
        await NotifyAsync(null, id, true);
    }
    public Task<IReadOnlyList<AutomationRun>> ListRunsAsync(string id, CancellationToken ct = default) => _store.RunsAsync(id, ct);
    public IReadOnlyList<AutomationPreset> Presets() => [
        new("daily-summary", "Daily summary", "Every weekday morning, summarize important changes in this project.",
            new AutomationSchedule { Kind = "weekdays", Hour = 8, Minute = 0 }),
        new("weekly-review", "Weekly review", "Turn recent project work into a concise weekly status update.",
            new AutomationSchedule { Kind = "weekly", Hour = 16, Minute = 0, Days = [5] }),
        new("ci-monitor", "CI monitor", "Check failing builds and report changes that need attention.",
            new AutomationSchedule { Kind = "every", EveryMs = 3_600_000 }),
        new("follow-up", "Follow up", "Keep following this conversation and notify me when something important changes.",
            new AutomationSchedule { Kind = "weekdays", Hour = 9, Minute = 0 })];
    private async Task LoadAsync(CancellationToken ct)
    {
        if (_loaded) return;
        foreach (var definition in await _store.ListAsync(ct)) _definitions[definition.Id] = definition;
        _loaded = true;
    }
    private AutomationDefinition Get(string id) => _definitions.GetValueOrDefault(id) ?? throw new KeyNotFoundException("automation.notFound");
    private AutomationDefinition Build(AutomationInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Prompt)) throw new ArgumentException("automation.nameAndPromptRequired");
        if (input.Name.Length > 200 || input.Prompt.Length > 10000) throw new ArgumentException("automation.inputTooLong");
        if (input.Status is not ("active" or "paused")) throw new ArgumentException("automation.invalidStatus");
        if (input.ExecutionMode is not ("thread" or "independent")) throw new ArgumentException("automation.invalidMode");
        if (input.ExecutionMode == "thread" && string.IsNullOrWhiteSpace(input.TargetThreadId)) throw new ArgumentException("automation.targetRequired");
        if (input.WorkspaceMode is not (null or "project" or "worktree")) throw new ArgumentException("automation.invalidWorkspace");
        if (input.NotificationPolicy is not (null or "important" or "all" or "failures")) throw new ArgumentException("automation.invalidNotificationPolicy");
        if (input.ApprovalPolicy is not ("workspaceScope" or "fullAuto")) throw new ArgumentException("automation.invalidApprovalPolicy");
        if (input.Schedule == null) throw new ArgumentException("automation.scheduleRequired");
        input.Schedule.Validate();
        return new AutomationDefinition { Name = input.Name.Trim(), Prompt = input.Prompt, Status = input.Status, ExecutionMode = input.ExecutionMode,
            TargetThreadId = input.TargetThreadId, WorkspaceMode = input.WorkspaceMode ?? (Directory.Exists(Path.Combine(paths.WorkspacePath, ".git")) || File.Exists(Path.Combine(paths.WorkspacePath, ".git")) ? "worktree" : "project"),
            AgentProfileId = input.AgentProfileId, ApprovalPolicy = input.ApprovalPolicy, Schedule = input.Schedule,
            NotificationPolicy = input.NotificationPolicy ?? (input.ExecutionMode == "thread" ? "important" : "all") };
    }
    private async Task NotifyAsync(AutomationDefinition? definition, string id, bool removed)
    {
        if (Updated == null) return;
        foreach (Func<AutomationDefinition?, string, bool, Task> handler in Updated.GetInvocationList())
            try { await handler(definition, id, removed); } catch (Exception ex) { logger.LogWarning(ex, "Automation observer failed"); }
    }

    private static AutomationDefinition CompleteOneShot(AutomationDefinition definition) => definition with
    {
        Status = "completed",
        Version = definition.Version + 1,
        UpdatedAt = DateTimeOffset.UtcNow,
        NextRunAt = null
    };
    private async Task SaveRunAsync(AutomationRun run)
    {
        await _store.SaveRunAsync(run, CancellationToken.None);
        if (RunUpdated == null) return;
        foreach (Func<AutomationRun, Task> handler in RunUpdated.GetInvocationList())
            try { await handler(run); } catch (Exception ex) { logger.LogWarning(ex, "Automation run observer failed"); }
    }
}
