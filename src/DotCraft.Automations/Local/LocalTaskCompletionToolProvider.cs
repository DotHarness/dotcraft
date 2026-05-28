using System.ComponentModel;
using DotCraft.Abstractions;
using DotCraft.Automations.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DotCraft.Automations.Local;

/// <summary>
/// Injects <c>CompleteLocalTask</c> for local automation agent sessions (same role as GitHub <c>CompleteIssue</c>).
/// </summary>
public sealed class LocalTaskCompletionToolProvider(
    LocalTaskFileStore fileStore,
    ILogger<LocalTaskCompletionToolProvider> logger) : IAgentToolProvider
{
    public int Priority => 95;

    public IEnumerable<AITool> CreateTools(ToolProviderContext context)
    {
        var taskDir = context.AutomationTaskDirectory ?? TryResolveTaskDirectory(context.WorkspacePath);
        if (taskDir == null)
            yield break;

        yield return AIFunctionFactory.Create(
            async ([Description("Brief description of what was done to complete the task.")] string summary) =>
            {
                logger.LogInformation("Agent calling CompleteLocalTask for {TaskDir}: {Summary}", taskDir, summary);
                try
                {
                    var task = await fileStore.LoadAsync(taskDir, CancellationToken.None).ConfigureAwait(false);

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
                    await fileStore.SaveAsync(task, CancellationToken.None).ConfigureAwait(false);

                    return "Local task has been marked as completed. " +
                           "The orchestrator will stop the workflow after this turn.";
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to complete local task at {TaskDir}", taskDir);
                    return $"Warning: could not update task.md ({ex.Message}).";
                }
            },
            "CompleteLocalTask",
            "Call this when the task is fully complete. Sets task.md to completed and stores the summary. " +
            "Only call after you have finished all required work in the task workspace.");
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
