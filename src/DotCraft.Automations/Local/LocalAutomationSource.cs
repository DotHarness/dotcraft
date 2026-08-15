using DotCraft.Agents;
using DotCraft.Tools;
using Microsoft.Extensions.Logging;

namespace DotCraft.Automations.Local;

/// <summary>
/// File-based local automation tasks under <see cref="LocalTaskFileStore.TasksRoot"/>.
/// Implements <see cref="IDisposable"/> to release the FileSystemWatcher resource.
/// </summary>
public sealed class LocalAutomationSource(
    LocalTaskFileStore fileStore,
    LocalWorkflowLoader workflowLoader,
    ILoggerFactory loggerFactory,
    ILogger<LocalAutomationSource> logger)
    : IDisposable
{
    // Picks up new task.md files without restarting the host.
    private readonly IDisposable _newTaskWatch =
        fileStore.WatchForNewTasks(t => logger.LogInformation("New local task discovered: {TaskId}", t.Id));

    private bool _disposed;

    /// <summary>
    /// Disposes the FileSystemWatcher used for new task detection.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _newTaskWatch?.Dispose();
    }

    public string ToolProfileName => "local-task";

    public void RegisterToolProfile(IToolProfileRegistry registry)
    {
        var completionLogger = loggerFactory.CreateLogger<LocalTaskCompletionToolSource>();
        IReadOnlyList<IToolSource> sources =
        [
            new LocalTaskCompletionToolSource(fileStore, completionLogger)
        ];
        registry.Register(ToolProfileName, sources);
    }

    public async Task<IReadOnlyList<AutomationTask>> GetPendingTasksAsync(CancellationToken ct)
    {
        var all = await fileStore.LoadAllAsync(ct);
        return all.Where(t => t.Status == AutomationTaskStatus.Pending).Cast<AutomationTask>().ToList();
    }

    public async Task<IReadOnlyList<AutomationTask>> GetAllTasksAsync(CancellationToken ct)
    {
        var all = await fileStore.LoadAllAsync(ct);
        return all.Cast<AutomationTask>().ToList();
    }

    public Task<AutomationWorkflowDefinition> GetWorkflowAsync(AutomationTask task, CancellationToken ct) =>
        workflowLoader.LoadAsync((LocalAutomationTask)task, ct);

    public Task OnStatusChangedAsync(AutomationTask task, AutomationTaskStatus newStatus, CancellationToken ct)
    {
        task.Status = newStatus;
        return fileStore.SaveAsync((LocalAutomationTask)task, ct);
    }

    public Task OnAgentCompletedAsync(AutomationTask task, string agentSummary, CancellationToken ct)
    {
        var local = (LocalAutomationTask)task;
        // Preserve summary from task.md (e.g. CompleteLocalTask) when the turn extract is empty.
        if (!string.IsNullOrWhiteSpace(agentSummary))
            local.AgentSummary = agentSummary;
        return fileStore.SaveAsync(local, ct);
    }

    public async Task<bool> ShouldStopWorkflowAfterTurnAsync(AutomationTask task, CancellationToken ct)
    {
        if (task is not LocalAutomationTask local)
            return false;

        var reloaded = await fileStore.LoadAsync(local.TaskDirectory, ct);
        local.Status = reloaded.Status;
        local.AgentSummary = reloaded.AgentSummary;
        local.ThreadId = reloaded.ThreadId;
        local.Description = reloaded.Description;
        local.CreatedAt = reloaded.CreatedAt;
        local.UpdatedAt = reloaded.UpdatedAt;

        return local.Status == AutomationTaskStatus.Completed;
    }

    /// <summary>
    /// Deletes the task directory under <see cref="LocalTaskFileStore.TasksRoot"/>.
    /// </summary>
    public Task DeleteTaskAsync(string taskId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return DeleteTaskCoreAsync(taskId, ct);
    }

    private async Task DeleteTaskCoreAsync(string taskId, CancellationToken ct)
    {
        var local = await FindTaskByIdAsync(taskId, ct)
            ?? throw new KeyNotFoundException($"Local task '{taskId}' was not found.");

        if (local.Status == AutomationTaskStatus.Running)
        {
            throw new InvalidOperationException(
                $"Task '{taskId}' cannot be deleted while the agent is running.");
        }

        var dir = Path.GetFullPath(local.TaskDirectory);
        var root = Path.GetFullPath(fileStore.TasksRoot);
        if (!dir.StartsWith(root, StringComparison.OrdinalIgnoreCase) || dir.Length <= root.Length)
            throw new InvalidOperationException("Invalid task directory.");

        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);

        await Task.CompletedTask;
    }

    private async Task<LocalAutomationTask?> FindTaskByIdAsync(string taskId, CancellationToken ct)
    {
        var all = await fileStore.LoadAllAsync(ct);
        return all.FirstOrDefault(t => string.Equals(t.Id, taskId, StringComparison.Ordinal));
    }

}
