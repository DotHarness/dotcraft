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

    public async Task<IReadOnlyList<BackgroundTask>> ListBackgroundTasksAsync(string threadId, CancellationToken ct = default) =>
        await BackgroundTasks.ListAsync(await UserTaskCallerAsync(threadId, ct).ConfigureAwait(false), ct).ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task<BackgroundTask> StopBackgroundTaskAsync(string threadId, string taskId, CancellationToken ct = default) =>
        await BackgroundTasks.StopAsync(
            await UserTaskCallerAsync(threadId, ct).ConfigureAwait(false),
            taskId,
            ct,
            BackgroundTaskRegistry.UserStopOrigin).ConfigureAwait(false);

    private async Task<BackgroundTaskCaller> UserTaskCallerAsync(string threadId, CancellationToken ct)
    {
        var thread = await GetOrLoadThreadAsync(threadId, ct).ConfigureAwait(false);
        return new BackgroundTaskCaller(thread, thread.Source.SubAgent?.RootThreadId ?? thread.Id);
    }

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

        public async Task<IReadOnlyList<BackgroundTask>> ListAsync(BackgroundTaskCaller caller, CancellationToken ct)
        {
            if (owner.BackgroundTerminalService is not { } terminals)
                return [];
            var listed = await terminals.ListAsync(caller.Thread.Id, ct).ConfigureAwait(false);
            return listed.Select(terminal => BackgroundTaskTools.ToTask(
                terminal,
                terminal.Status == BackgroundTerminalStatus.Running
                    ? owner._terminalStopLatches.GetOrAdd(terminal.SessionId, static _ => new BackgroundTaskLatch())
                    : null)).ToArray();
        }

        public Task StopAsync(BackgroundTask task, CancellationToken ct) =>
            owner.BackgroundTerminalService!.StopAsync(task.TaskId, ct);
    }
}
