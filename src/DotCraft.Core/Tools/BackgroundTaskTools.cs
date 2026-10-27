using System.ComponentModel;
using System.Text.Json.Nodes;
using DotCraft.Sessions;
using DotCraft.Tools.BackgroundTerminals;

namespace DotCraft.Tools;

/// <summary>Model-facing control of background tasks, including shells started on a Remote Tool Host route.</summary>
public sealed class BackgroundTaskTools
{
    private const int TerminalLabelMaxChars = 80;

    [Description("Stop a background task you started: a command from Exec, a subagent, or a Workflow run.")]
    [Tool]
    [ToolPresentation("core.task", Operation = "stop")]
    public async Task<string> TaskStop(
        [Description("Session ID from Exec, agent path from SpawnAgent, or run ID from Workflow.")] string taskId,
        CancellationToken cancellationToken = default)
    {
        var context = SubAgentSessionScope.Current;
        if (context?.SessionService is not SessionService service)
            throw new InvalidOperationException("TaskStop is available only inside a Session Core turn.");
        return FormatResult(await service.StopBackgroundTaskAsync(context, taskId, cancellationToken).ConfigureAwait(false));
    }

    internal static BackgroundTask ToTask(BackgroundTerminalSnapshot terminal, BackgroundTaskLatch? latch = null) => new()
    {
        TaskId = terminal.SessionId,
        Kind = BackgroundTaskKinds.Shell,
        OwnerThreadId = terminal.ThreadId,
        TurnId = terminal.TurnId,
        CallId = terminal.CallId,
        Label = ShortenCommand(terminal.Command),
        Status = terminal.Status switch
        {
            BackgroundTerminalStatus.Running => BackgroundTaskStatus.Running,
            BackgroundTerminalStatus.Completed => BackgroundTaskStatus.Completed,
            BackgroundTerminalStatus.Killed => BackgroundTaskStatus.Stopped,
            _ => BackgroundTaskStatus.Failed
        },
        KindStatus = terminal.Status,
        OutputPath = terminal.OutputPath,
        StartedAt = terminal.StartedAt,
        EndedAt = terminal.CompletedAt,
        Latch = latch
    };

    internal static string ShortenCommand(string command)
    {
        var singleLine = string.Join(' ', command.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return singleLine.Length <= TerminalLabelMaxChars ? singleLine : singleLine[..(TerminalLabelMaxChars - 3)] + "...";
    }

    private static string FormatResult(BackgroundTask task)
    {
        var result = new JsonObject
        {
            ["taskId"] = task.TaskId,
            ["kind"] = task.Kind,
            ["label"] = task.Label,
            ["status"] = task.Status
        };
        if (!string.IsNullOrEmpty(task.LastAgentMessage))
            result["lastAgentMessage"] = task.LastAgentMessage;
        return result.ToJsonString();
    }
}
