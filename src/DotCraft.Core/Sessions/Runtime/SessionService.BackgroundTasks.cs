using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private readonly ConcurrentDictionary<string, BackgroundTaskLatch> _terminalStopLatches = new(StringComparer.Ordinal);
    private BackgroundTaskRegistry? _backgroundTasks;

    internal BackgroundTaskRegistry BackgroundTasks =>
        LazyInitializer.EnsureInitialized(ref _backgroundTasks, () => new BackgroundTaskRegistry(
            [new ShellTaskKind(this), new SubAgentTaskKind(this), .. backgroundTaskKinds ?? []]));

    internal Task<BackgroundTask> StopBackgroundTaskAsync(SubAgentSessionContext context, string taskId, CancellationToken ct) =>
        BackgroundTasks.StopAsync(new BackgroundTaskCaller(context.ParentThread, context.RootThreadId), taskId, ct);

    private const string TaskPresentationId = "core.task";
    private const string TaskKindPresentationOption = "kind";

    private static string? ResolveTaskPresentationKind(ToolPresentationDescriptor? presentation, JsonObject? arguments) =>
        presentation?.Id.Value == TaskPresentationId
        && arguments?["taskId"] is JsonValue value
        && value.TryGetValue<string>(out var taskId)
        && !string.IsNullOrWhiteSpace(taskId)
            ? BackgroundTaskKinds.Of(taskId.Trim())
            : null;

    private sealed class ShellTaskKind(SessionService owner) : IBackgroundTaskKind
    {
        public string Kind => BackgroundTaskKinds.Shell;

        public async Task<BackgroundTask?> FindAsync(BackgroundTaskCaller caller, string taskId, CancellationToken ct)
        {
            if (owner.BackgroundTerminalService is not { } terminals)
                return null;
            BackgroundTerminalSnapshot terminal;
            try
            {
                terminal = await terminals.ReadAsync(taskId, maxOutputChars: 0, ct: ct).ConfigureAwait(false);
            }
            catch (KeyNotFoundException)
            {
                return null;
            }
            if (!string.Equals(terminal.ThreadId, caller.Thread.Id, StringComparison.Ordinal))
                return null;

            return BackgroundTaskTools.ToTask(
                terminal,
                terminal.Status == BackgroundTerminalStatus.Running
                    ? owner._terminalStopLatches.GetOrAdd(terminal.SessionId, static _ => new BackgroundTaskLatch())
                    : null);
        }

        public Task StopAsync(BackgroundTask task, CancellationToken ct) =>
            owner.BackgroundTerminalService!.StopAsync(task.TaskId, ct);
    }
}
