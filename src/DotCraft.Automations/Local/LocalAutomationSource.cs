using System.Diagnostics;
using System.Text.RegularExpressions;
using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Automations.Abstractions;
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
        var completionLogger = loggerFactory.CreateLogger<LocalTaskCompletionToolProvider>();
        IReadOnlyList<IAgentToolProvider> providers =
        [
            new LocalTaskCompletionToolProvider(fileStore, completionLogger)
        ];
        registry.Register(ToolProfileName, providers);
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

    private async Task RunShellHookAsync(string workingDirectory, string command, CancellationToken ct)
    {
        // Security: Validate command to prevent injection attacks.
        // Commands should come from trusted workflow files, but we add basic validation.
        if (string.IsNullOrWhiteSpace(command))
        {
            logger.LogWarning("Empty command passed to hook");
            return;
        }

        // Log the command for audit trail
        logger.LogInformation("Executing hook command in {Dir}: {Command}", workingDirectory, command);

        try
        {
            Directory.CreateDirectory(workingDirectory);
            var psi = new ProcessStartInfo
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            if (OperatingSystem.IsWindows())
            {
                psi.FileName = "cmd.exe";
                // Security: Basic sanitization - reject commands with obvious injection patterns
                if (ContainsDangerousPatterns(command))
                {
                    logger.LogError("Hook command rejected due to potentially dangerous pattern: {Command}", command);
                    return;
                }
                psi.Arguments = "/c " + command;
            }
            else
            {
                psi.FileName = "/bin/sh";
                // Security: Basic sanitization for shell commands
                if (ContainsDangerousPatterns(command))
                {
                    logger.LogError("Hook command rejected due to potentially dangerous pattern: {Command}", command);
                    return;
                }
                psi.Arguments = "-c " + command;
            }

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                logger.LogWarning("Failed to start hook process for command: {Command}", command);
                return;
            }

            // Read stdout/stderr concurrently with process lifetime so pipe buffers cannot deadlock the child.
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (!string.IsNullOrEmpty(stdout))
                logger.LogDebug("Hook stdout: {Stdout}", stdout);

            if (proc.ExitCode != 0)
            {
                logger.LogWarning("Hook exited with code {Code}: {Err}", proc.ExitCode, stderr);
            }
            else
            {
                logger.LogInformation("Hook completed successfully");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Hook execution failed for command: {Command}", command);
        }
    }

    /// <summary>
    /// Checks for potentially dangerous patterns in shell commands.
    /// This is a basic check - workflow files should only come from trusted sources.
    /// </summary>
    private static bool ContainsDangerousPatterns(string command)
    {
        // Check for common injection patterns
        var dangerousPatterns = new[]
        {
            // Command chaining with && || ; 
            // Note: We allow basic chaining for legitimate use cases, but log for audit
            // Network exfiltration attempts
            @"curl\s+.*\|",
            @"wget\s+.*\|",
            @"nc\s+-",
            @"netcat",
            // Privilege escalation
            @"sudo\s+chmod\s+u?[sx]",
            @"chmod\s+[ou]?[sx]",
            // Fork bombs
            @":\(\)\{.*\}:",
            // Environment manipulation for malicious purposes
            @"export\s+PATH=/",
            @"export\s+LD_PRELOAD",
            // Redirecting sensitive files
            @"/etc/(passwd|shadow|sudoers)",
            // PowerShell download and execute (on Windows)
            @"powershell.*-e",
            @"powershell.*downloadstring",
            @"powershell.*invoke-expression",
            @"powershell.*iex",
            @"powershell.*net\.webclient"
        };

        foreach (var pattern in dangerousPatterns)
        {
            if (Regex.IsMatch(command, pattern, RegexOptions.IgnoreCase))
                return true;
        }

        return false;
    }
}
