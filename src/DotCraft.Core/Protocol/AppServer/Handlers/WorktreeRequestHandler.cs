using DotCraft.Configuration;
using Contract = DotCraft.Protocol.Contracts.AppServer;

namespace DotCraft.Protocol.AppServer;

internal sealed class WorktreeRequestHandler(
    ISessionService sessionService,
    AppServerResponseWriter responseWriter,
    AppServerThreadBinder threadBinder,
    WorkspaceConfigEditor workspaceConfig,
    IAppConfigMonitor? appConfigMonitor,
    string? hostWorkspacePath,
    Func<SessionThread, bool, bool, CancellationToken, Task<SessionWireThread>> projectThreadAsync) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.WorktreeCreateAndFork, HandleWorktreeCreateAndForkAsync);
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.WorktreeCreateAndStart, HandleWorktreeCreateAndStartAsync);
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.ThreadWorktreeHandoff, HandleThreadWorktreeHandoffAsync);
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.WorktreeList, HandleWorktreeListAsync);
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.WorktreeStatus, HandleWorktreeStatusAsync);
    }

    private async Task<object?> HandleWorktreeCreateAndForkAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<WorktreeCreateAndForkParams>(msg);
        threadBinder.ValidateRuntimeInputs(p.DynamicTools, p.AdditionalContext);
        ValidateRuntimeConfiguration(p.Config);

        var identity = p.Identity == null ? null : NormalizeIdentityWorkspace(p.Identity);
        var result = await sessionService.CreateWorktreeAndForkAsync(
            new WorktreeCreateAndForkOptions
            {
                SourceThreadId = p.SourceThreadId,
                ForkPoint = p.ForkPoint,
                Identity = identity,
                Config = p.Config,
                DisplayName = p.DisplayName,
                BranchName = p.BranchName,
                BaseRef = p.BaseRef,
                Path = p.Path,
                CopyDirtyChanges = p.CopyDirtyChanges ?? true
            },
            ct);

        var thread = result.Thread;
        await threadBinder.BindThreadRuntimeAsync(thread, p.DynamicTools, p.AdditionalContext, ct);

        var includeTurns = p.ExcludeTurns != true;
        var responseWire = await projectThreadAsync(thread, includeTurns, true, ct);
        var notificationWire = await projectThreadAsync(thread, false, false, ct);

        await responseWriter.SendNotificationAfterResponseAsync(
            msg.Id,
            new WorktreeCreateAndForkResponse { Thread = responseWire, Worktree = result.Worktree },
            Contract.AppServerRpc.ThreadStarted,
            AppServerContractMapper.ToContract(new ThreadStartedNotification { Thread = notificationWire }),
            ct);

        return null;
    }

    private async Task<object?> HandleWorktreeCreateAndStartAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<WorktreeCreateAndStartParams>(msg);
        threadBinder.ValidateRuntimeInputs(p.DynamicTools, p.AdditionalContext);
        ValidateRuntimeConfiguration(p.Config);

        var result = await sessionService.CreateWorktreeAndStartAsync(
            new WorktreeCreateAndStartOptions
            {
                Identity = NormalizeIdentityWorkspace(p.Identity),
                Config = p.Config,
                HistoryMode = ParseHistoryMode(p.HistoryMode),
                DisplayName = p.DisplayName,
                BranchName = p.BranchName,
                BaseRef = p.BaseRef,
                Path = p.Path,
                CopyDirtyChanges = p.CopyDirtyChanges ?? true
            },
            ct);

        var thread = result.Thread;
        await threadBinder.BindThreadRuntimeAsync(thread, p.DynamicTools, p.AdditionalContext, ct);

        var startedWire = await projectThreadAsync(thread, true, false, ct);
        await responseWriter.SendNotificationAfterResponseAsync(
            msg.Id,
            new WorktreeCreateAndStartResponse { Thread = startedWire, Worktree = result.Worktree },
            Contract.AppServerRpc.ThreadStarted,
            AppServerContractMapper.ToContract(new ThreadStartedNotification { Thread = startedWire }),
            ct);

        return null;
    }

    private async Task<object?> HandleThreadWorktreeHandoffAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<ThreadWorktreeHandoffParams>(msg);
        if (string.IsNullOrWhiteSpace(p.ThreadId))
            throw AppServerErrors.InvalidParams("'threadId' is required.");

        var result = await sessionService.HandoffThreadWorktreeAsync(
            new WorktreeHandoffOptions
            {
                ThreadId = p.ThreadId,
                Mode = p.Mode,
                BranchName = p.BranchName,
                BaseRef = p.BaseRef,
                Path = p.Path,
                CopyDirtyChanges = p.CopyDirtyChanges ?? true
            },
            ct);

        var thread = result.Thread;
        var wire = await projectThreadAsync(thread, false, false, ct);
        var response = new ThreadWorktreeHandoffResponse
        {
            Thread = wire,
            Mode = result.Mode,
            Worktree = result.Worktree,
            DirtyHandoff = result.DirtyHandoff
        };

        await responseWriter.SendNotificationAfterResponseAsync(
            msg.Id,
            response,
            Contract.AppServerRpc.ThreadUpdated,
            AppServerContractMapper.ToContract(new ThreadUpdatedNotification { Thread = wire }),
            ct);

        return null;
    }

    private async Task<object?> HandleWorktreeListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<WorktreeListParams>(msg);
        var identity = p.Identity == null ? null : NormalizeIdentityWorkspace(p.Identity);
        var data = await sessionService.ListWorktreesAsync(identity, ct);
        return new WorktreeListResult { Data = data.ToList() };
    }

    private async Task<object?> HandleWorktreeStatusAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<WorktreeStatusParams>(msg);
        if (string.IsNullOrWhiteSpace(p.ThreadId))
            throw AppServerErrors.InvalidParams("'threadId' is required.");

        return new WorktreeStatusResult
        {
            Status = await sessionService.GetWorktreeStatusAsync(p.ThreadId, ct)
        };
    }

    private void ValidateRuntimeConfiguration(ThreadConfiguration? config)
    {
        if (config == null)
            return;

        var currentConfig = appConfigMonitor?.Current ?? workspaceConfig.LoadCurrentMergedConfig();
        if (config.Reasoning != null)
        {
            AppServerRuntimeRequestValidator.ValidateReasoningForRuntime(
                currentConfig,
                config.ProviderId,
                config.Model,
                config.Reasoning);
        }

        if (config.ContextWindow != null)
        {
            AppServerRuntimeRequestValidator.ValidateContextWindowForRuntime(
                currentConfig,
                config.ProviderId,
                config.Model,
                config.ContextWindow);
        }

        if (config.ApprovalTimeoutSeconds is < 1 or > 86400)
            throw AppServerErrors.InvalidParams("'config.approvalTimeoutSeconds' must be between 1 and 86400.");
    }

    private SessionIdentity NormalizeIdentityWorkspace(SessionIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(identity.WorkspacePath) && !string.IsNullOrEmpty(hostWorkspacePath))
            return identity with { WorkspacePath = hostWorkspacePath };
        return identity;
    }

    private static HistoryMode ParseHistoryMode(string? value) =>
        value?.ToLowerInvariant() == "client"
            ? HistoryMode.Client
            : HistoryMode.Server;
}
