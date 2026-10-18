using System.Globalization;
using DotCraft.Commands.Core;
using DotCraft.Sessions;
using DotCraft.Text;

namespace DotCraft.Commands.Handlers;

public sealed class StatusCommandHandler : ICommandHandler
{
    public string[] Commands => ["/status"];

    public async Task<CommandResult> HandleAsync(CommandContext context, ICommandResponder responder)
    {
        if (context.SessionService is not { } sessions)
            return CommandResult.HandledResult(FallbackText.CommandServiceUnavailable);

        var thread = await sessions.GetThreadAsync(context.SessionId);
        var active = CommandThreads.ActiveTurn(thread);
        var tasks = await sessions.ListBackgroundTasksAsync(thread.Id);
        var usage = sessions.TryGetContextUsageSnapshot(thread.Id);
        var pendingApprovals = active == null
            ? 0
            : Math.Max(0, active.Items.Count(static item => item.Type == ItemType.ApprovalRequest)
                - active.Items.Count(static item => item.Type == ItemType.ApprovalResponse));

        string[] lines =
        [
            FallbackText.CommandStatusTitle(CommandThreads.Title(thread.DisplayName, thread.Id)),
            FallbackText.CommandStatusState(State(active)),
            FallbackText.CommandStatusModel(ModelCommandHandler.CurrentModel(context.Config, thread) ?? FallbackText.CommandStatusUnknown),
            FallbackText.CommandStatusMode(ModeCommandHandler.CurrentMode(thread)),
            FallbackText.CommandStatusContext(
                usage == null ? FallbackText.CommandStatusUnknown : FormatTokens(usage.Tokens),
                usage == null ? FallbackText.CommandStatusUnknown : FormatTokens(usage.ContextWindow)),
            FallbackText.CommandStatusQueued(thread.QueuedInputs.Count),
            FallbackText.CommandStatusTasks(tasks.Count(static task => task.Status == BackgroundTaskStatus.Running)),
            FallbackText.CommandStatusApprovals(pendingApprovals)
        ];
        return CommandResult.HandledResult(string.Join('\n', lines));
    }

    private static string State(SessionTurn? active) => active?.Status switch
    {
        TurnStatus.Running => FallbackText.CommandStatusStateRunning,
        TurnStatus.WaitingApproval => FallbackText.CommandStatusStateWaitingApproval,
        TurnStatus.WaitingInput => FallbackText.CommandStatusStateWaitingInput,
        _ => FallbackText.CommandStatusStateIdle
    };

    private static string FormatTokens(long tokens) => tokens.ToString("N0", CultureInfo.InvariantCulture);
}
