using System.ComponentModel;
using DotCraft.Automations;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DotCraft.Automations.Local;

/// <summary>
/// Injects <c>CompleteLocalTask</c> for local automation agent sessions (same role as GitHub <c>CompleteIssue</c>).
/// </summary>
public sealed class LocalTaskCompletionToolSource(
    LocalTaskFileStore fileStore,
    ILogger<LocalTaskCompletionToolSource> logger) : AIFunctionToolSource
{
    /// <inheritdoc />
    public override string SourceId => "local-task-completion";

    /// <inheritdoc />
    public override int Priority => 95;

    /// <inheritdoc />
    protected override IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext context)
    {
        var taskDir = TryResolveTaskDirectory(context.WorkspacePath);
        if (taskDir == null)
            yield break;

        var methods = new LocalTaskCompletionToolMethods(fileStore, logger, taskDir);
        yield return DotCraft.GeneratedTools.Automations.GeneratedToolFunctions.LocalTaskCompletionToolMethods_CompleteLocalTask(methods);
    }

    private static string? TryResolveTaskDirectory(string workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
            return null;

        string fullWorkspace;
        try
        {
            fullWorkspace = Path.GetFullPath(workspacePath.Trim());
        }
        catch
        {
            return null;
        }

        var parent = Directory.GetParent(fullWorkspace);
        if (parent == null)
            return null;

        var taskDir = parent.FullName;
        var taskFile = Path.Combine(taskDir, "task.md");
        return File.Exists(taskFile) ? taskDir : null;
    }
}

internal sealed class LocalTaskCompletionToolMethods(
    LocalTaskFileStore fileStore,
    ILogger logger,
    string taskDir)
{
    [GeneratedTool]
    [Description("Call this when the task is fully complete. Sets task.md to completed and stores the summary. Only call after you have finished all required work in the task workspace.")]
    public async Task<string> CompleteLocalTask(
        [Description("Brief description of what was done to complete the task.")] string summary,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Agent calling CompleteLocalTask for {TaskDir}: {Summary}", taskDir, summary);
        try
        {
            var task = await fileStore.LoadAsync(taskDir, cancellationToken).ConfigureAwait(false);

            if (task.Status == AutomationTaskStatus.Completed)
                return "Local task is already marked as completed.";

            if (task.Status != AutomationTaskStatus.Running)
            {
                return $"Cannot complete task in status {task.Status}. " +
                       "CompleteLocalTask is only available while the task is running.";
            }

            if (!string.IsNullOrWhiteSpace(summary))
                task.AgentSummary = summary.Trim();

            task.Status = AutomationTaskStatus.Completed;
            await fileStore.SaveAsync(task, cancellationToken).ConfigureAwait(false);

            return "Local task has been marked as completed. " +
                   "The orchestrator will stop the workflow after this turn.";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to complete local task at {TaskDir}", taskDir);
            return $"Warning: could not update task.md ({ex.Message}).";
        }
    }
}
