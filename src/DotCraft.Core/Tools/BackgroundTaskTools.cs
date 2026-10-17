using System.ComponentModel;
using System.Text.Json.Nodes;
using DotCraft.Sessions;
using DotCraft.Tools.BackgroundTerminals;

namespace DotCraft.Tools;

/// <summary>
/// Model-facing control of background tasks. On a Remote Tool Host, <paramref name="executionTerminals"/>
/// is the route's terminal service and only its shell tasks can be stopped.
/// </summary>
public sealed class BackgroundTaskTools(IBackgroundTerminalService? executionTerminals = null)
{
    private const int TerminalLabelMaxChars = 80;

    [Description("Stop a background task you started: a command from Exec, a subagent, or a Workflow run.")]
    [Tool]
    [ToolRpc]
    [ToolPresentation("core.task", Operation = "stop")]
    public async Task<string> TaskStop(
        [Description("Session ID from Exec, agent path from SpawnAgent, or run ID from Workflow.")] string taskId,
        CancellationToken cancellationToken = default)
    {
        var context = SubAgentSessionScope.Current;
        if (context?.SessionService is SessionService service)
            return FormatResult(await service.StopBackgroundTaskAsync(context, taskId, cancellationToken).ConfigureAwait(false));
        if (executionTerminals is null)
            throw new InvalidOperationException("TaskStop is available only inside a Session Core turn.");
        return FormatResult(await StopExecutionTerminalAsync(executionTerminals, taskId?.Trim() ?? string.Empty, cancellationToken)
            .ConfigureAwait(false));
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
        Latch = latch
    };

    internal static string ShortenCommand(string command)
    {
        var singleLine = string.Join(' ', command.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return singleLine.Length <= TerminalLabelMaxChars ? singleLine : singleLine[..(TerminalLabelMaxChars - 3)] + "...";
    }

    private static async Task<BackgroundTask> StopExecutionTerminalAsync(
        IBackgroundTerminalService terminals,
        string taskId,
        CancellationToken ct)
    {
        if (BackgroundTaskKinds.Of(taskId) != BackgroundTaskKinds.Shell)
            throw new KeyNotFoundException($"'{taskId}' matches no background task you own.");
        return ToTask(await terminals.StopAsync(taskId, ct).ConfigureAwait(false));
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
