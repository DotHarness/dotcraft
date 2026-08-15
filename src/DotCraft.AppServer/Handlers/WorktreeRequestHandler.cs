using DotCraft.Configuration;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using SessionThread = DotCraft.Sessions.SessionThread;

namespace DotCraft.AppServer;

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
        table.Map(Protocol.AppServer.AppServerRpc.WorktreeCreateAndFork, HandleWorktreeCreateAndForkAsync);
        table.Map(Protocol.AppServer.AppServerRpc.WorktreeCreateAndStart, HandleWorktreeCreateAndStartAsync);
        table.Map(Protocol.AppServer.AppServerRpc.ThreadWorktreeHandoff, HandleThreadWorktreeHandoffAsync);
        table.Map(Protocol.AppServer.AppServerRpc.WorktreeList, HandleWorktreeListAsync);
        table.Map(Protocol.AppServer.AppServerRpc.WorktreeStatus, HandleWorktreeStatusAsync);
    }

    private async Task<object?> HandleWorktreeCreateAndForkAsync(
        AppServerTypedRequest<Contract.WorktreeCreateAndForkParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        if (p.ExtensionData?.ContainsKey("excludeTurns") == true)
            throw AppServerErrors.InvalidParams("'excludeTurns' is no longer supported.");
        var dynamicTools = WorktreeContractMapper.ToDynamicTools(ValueOrDefault(p.DynamicTools));
        var additionalContext = WorktreeContractMapper.ToAdditionalContext(ValueOrDefault(p.AdditionalContext));
        var config = ValueOrDefault(p.Config) is { } contractConfig
            ? ThreadConfigurationContractMapper.FromContract(contractConfig)
            : null;
        threadBinder.ValidateRuntimeInputs(dynamicTools, additionalContext);
        ValidateRuntimeConfiguration(config);

        var identity = WorktreeContractMapper.ToDomain(ValueOrDefault(p.Identity));
        identity = identity == null ? null : NormalizeIdentityWorkspace(identity);
        var result = await sessionService.CreateWorktreeAndForkAsync(
            new WorktreeCreateAndForkOptions
            {
                SourceThreadId = ValueOrDefault(p.SourceThreadId) ?? string.Empty,
                ForkPoint = WorktreeContractMapper.ToDomain(ValueOrDefault(p.ForkPoint)),
                Identity = identity,
                Config = config,
                DisplayName = ValueOrDefault(p.DisplayName),
                BranchName = ValueOrDefault(p.BranchName),
                BaseRef = ValueOrDefault(p.BaseRef),
                Path = ValueOrDefault(p.Path),
                CopyDirtyChanges = ValueOrDefault(p.CopyDirtyChanges) ?? true
            },
            ct);

        var thread = result.Thread;
        await threadBinder.BindThreadRuntimeAsync(thread, dynamicTools, additionalContext, ct);

        var responseWire = await projectThreadAsync(thread, false, false, ct);
        var notificationWire = responseWire;

        await responseWriter.SendNotificationAfterResponseAsync(
            request.Message.Id,
            new Contract.WorktreeCreateAndForkResult
            {
                Thread = AppServerContractMapper.ToContract(responseWire),
                Worktree = WorktreeContractMapper.ToContract(result.Worktree)
            },
            Contract.AppServerRpc.ThreadStarted,
            new Contract.ThreadNotification { Thread = AppServerContractMapper.ToContract(notificationWire) },
            ct);

        return null;
    }

    private async Task<object?> HandleWorktreeCreateAndStartAsync(
        AppServerTypedRequest<Contract.WorktreeCreateAndStartParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        var dynamicTools = WorktreeContractMapper.ToDynamicTools(ValueOrDefault(p.DynamicTools));
        var additionalContext = WorktreeContractMapper.ToAdditionalContext(ValueOrDefault(p.AdditionalContext));
        var config = ValueOrDefault(p.Config) is { } contractConfig
            ? ThreadConfigurationContractMapper.FromContract(contractConfig)
            : null;
        threadBinder.ValidateRuntimeInputs(dynamicTools, additionalContext);
        ValidateRuntimeConfiguration(config);

        var identity = WorktreeContractMapper.ToDomain(ValueOrDefault(p.Identity))
                       ?? throw AppServerErrors.InvalidParams("'identity' is required.");

        var result = await sessionService.CreateWorktreeAndStartAsync(
            new WorktreeCreateAndStartOptions
            {
                Identity = NormalizeIdentityWorkspace(identity),
                Config = config,
                HistoryMode = ParseHistoryMode(ValueOrDefault(p.HistoryMode)),
                DisplayName = ValueOrDefault(p.DisplayName),
                BranchName = ValueOrDefault(p.BranchName),
                BaseRef = ValueOrDefault(p.BaseRef),
                Path = ValueOrDefault(p.Path),
                CopyDirtyChanges = ValueOrDefault(p.CopyDirtyChanges) ?? true
            },
            ct);

        var thread = result.Thread;
        await threadBinder.BindThreadRuntimeAsync(thread, dynamicTools, additionalContext, ct);

        var startedWire = await projectThreadAsync(thread, true, false, ct);
        await responseWriter.SendNotificationAfterResponseAsync(
            request.Message.Id,
            new Contract.WorktreeCreateAndStartResult
            {
                Thread = AppServerContractMapper.ToContract(startedWire),
                Worktree = WorktreeContractMapper.ToContract(result.Worktree)
            },
            Contract.AppServerRpc.ThreadStarted,
            new Contract.ThreadNotification { Thread = AppServerContractMapper.ToContract(startedWire) },
            ct);

        return null;
    }

    private async Task<object?> HandleThreadWorktreeHandoffAsync(
        AppServerTypedRequest<Contract.ThreadWorktreeHandoffParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        var threadId = ValueOrDefault(p.ThreadId);
        if (string.IsNullOrWhiteSpace(threadId))
            throw AppServerErrors.InvalidParams("'threadId' is required.");

        var result = await sessionService.HandoffThreadWorktreeAsync(
            new WorktreeHandoffOptions
            {
                ThreadId = threadId,
                Mode = ValueOrDefault(p.Mode) ?? WorktreeHandoffModes.Worktree,
                BranchName = ValueOrDefault(p.BranchName),
                BaseRef = ValueOrDefault(p.BaseRef),
                Path = ValueOrDefault(p.Path),
                CopyDirtyChanges = ValueOrDefault(p.CopyDirtyChanges) ?? true
            },
            ct);

        var thread = result.Thread;
        var wire = await projectThreadAsync(thread, false, false, ct);
        var response = new Contract.ThreadWorktreeHandoffResponse
        {
            Thread = AppServerContractMapper.ToContract(wire),
            Mode = result.Mode,
            Worktree = new Protocol.Optional<Contract.ThreadWorktreeInfo?>(
                result.Worktree is null ? null : WorktreeContractMapper.ToContract(result.Worktree)),
            DirtyHandoff = result.DirtyHandoff is null
                ? default
                : new Protocol.Optional<Contract.ThreadWorktreeDirtyHandoffInfo?>(
                    WorktreeContractMapper.ToContract(result.DirtyHandoff))
        };

        await responseWriter.SendNotificationAfterResponseAsync(
            request.Message.Id,
            response,
            Contract.AppServerRpc.ThreadUpdated,
            new Contract.ThreadNotification { Thread = AppServerContractMapper.ToContract(wire) },
            ct);

        return null;
    }

    private async Task<object?> HandleWorktreeListAsync(
        AppServerTypedRequest<Contract.WorktreeListParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        var identity = WorktreeContractMapper.ToDomain(ValueOrDefault(p.Identity));
        identity = identity == null ? null : NormalizeIdentityWorkspace(identity);
        var data = await sessionService.ListWorktreesAsync(identity, ct);
        return new Contract.WorktreeListResult { Data = data.Select(WorktreeContractMapper.ToContract).ToArray() };
    }

    private async Task<object?> HandleWorktreeStatusAsync(
        AppServerTypedRequest<Contract.WorktreeStatusParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        var threadId = ValueOrDefault(p.ThreadId);
        if (string.IsNullOrWhiteSpace(threadId))
            throw AppServerErrors.InvalidParams("'threadId' is required.");

        return new Contract.WorktreeStatusResult
        {
            Status = WorktreeContractMapper.ToContract(await sessionService.GetWorktreeStatusAsync(threadId, ct))
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

    private static T? ValueOrDefault<T>(Protocol.Optional<T> value) =>
        value.IsSet ? value.Value : default;
}
