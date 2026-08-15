using DotCraft.Protocol;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.AppServer;

namespace DotCraft.Teams;

/// <summary>
/// AppServer JSON-RPC surface for the Desktop Team.
/// </summary>
public sealed class TeamsProtocolExtension(TeamsService teamsService) : IAppServerContractExtension
{
    private const string TeamView = "teams/team/view";
    private const string MissionCreate = "teams/mission/create";
    private const string MissionCancel = "teams/mission/cancel";
    private const string MissionArchive = "teams/mission/archive";
    private const string MemberOpenThread = "teams/member/openThread";
    private const string TeamChanged = "teams/team/changed";

    public IReadOnlyCollection<string> Methods { get; } =
    [
        TeamView,
        MissionCreate,
        MissionCancel,
        MissionArchive,
        MemberOpenThread
    ];

    public IReadOnlyCollection<IRpcMethodDescriptor> ContractMethods { get; } =
    [
        Contract.AppServerRpc.TeamsTeamView,
        Contract.AppServerRpc.TeamsMissionCreate,
        Contract.AppServerRpc.TeamsMissionCancel,
        Contract.AppServerRpc.TeamsMissionArchive,
        Contract.AppServerRpc.TeamsMemberOpenThread
    ];

    public void ContributeCapabilities(AppServerCapabilityBuilder builder)
    {
        if (!string.IsNullOrWhiteSpace(builder.WorkspaceCraftPath))
            builder.SetExtension("teams", new Contract.TeamsCapabilities { Team = true, Missions = true });
    }

    public async Task<object?> HandleContractAsync(
        IRpcMethodDescriptor _,
        object requestParams,
        AppServerIncomingMessage msg,
        AppServerExtensionContext context)
    {
        var method = msg.Method ?? string.Empty;
        var workspaceCraftPath = RequireWorkspaceCraftPath(method, context);
        var workspacePath = RequireHostWorkspacePath(method, context);
        var ct = context.CancellationToken;
        if (!teamsService.IsAgentTeamsPluginEnabled(workspacePath, workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(method);

        switch (method)
        {
            case TeamView:
                return TeamsContractMapper.ToContract(
                    await teamsService.ViewTeamAsync(context.SessionService, workspaceCraftPath, ct));

            case MissionCreate:
            {
                var p = TeamsContractMapper.FromContract((Contract.TeamsMissionCreateParams)requestParams);
                var result = await teamsService.CreateMissionAsync(
                    context.SessionService,
                    workspacePath,
                    workspaceCraftPath,
                    p,
                    ct);
                return await SendNotificationAfterResponseAsync(
                    msg,
                    context,
                    TeamsContractMapper.ToContract(result),
                    Contract.AppServerRpc.TeamsMissionCreate,
                    Contract.AppServerRpc.TeamsTeamChanged,
                    new Contract.TeamsTeamChangedNotification
                    {
                        Reason = "missionCreated",
                        MissionId = result.Mission.MissionId
                    });
            }

            case MissionCancel:
            {
                var p = TeamsContractMapper.FromContract((Contract.TeamsMissionCancelParams)requestParams);
                var result = await teamsService.CancelMissionAsync(context.SessionService, workspaceCraftPath, p, ct);
                return await SendNotificationAfterResponseAsync(
                    msg,
                    context,
                    TeamsContractMapper.ToContract(result),
                    Contract.AppServerRpc.TeamsMissionCancel,
                    Contract.AppServerRpc.TeamsTeamChanged,
                    new Contract.TeamsTeamChangedNotification
                    {
                        Reason = "missionCancelled",
                        MissionId = p.MissionId
                    });
            }

            case MissionArchive:
            {
                var p = TeamsContractMapper.FromContract((Contract.TeamsMissionArchiveParams)requestParams);
                var result = await teamsService.ArchiveMissionAsync(context.SessionService, workspaceCraftPath, p, ct);
                return await SendNotificationAfterResponseAsync(
                    msg,
                    context,
                    TeamsContractMapper.ToContract(result),
                    Contract.AppServerRpc.TeamsMissionArchive,
                    Contract.AppServerRpc.TeamsTeamChanged,
                    new Contract.TeamsTeamChangedNotification
                    {
                        Reason = "missionArchived",
                        MissionId = p.MissionId
                    });
            }

            case MemberOpenThread:
            {
                var p = TeamsContractMapper.FromContract((Contract.TeamsMemberOpenThreadParams)requestParams);
                return TeamsContractMapper.ToContract(teamsService.OpenMemberThread(workspaceCraftPath, p));
            }

            default:
                throw AppServerErrors.MethodNotFound(method);
        }
    }

    private static async Task<object?> SendNotificationAfterResponseAsync(
        AppServerIncomingMessage msg,
        AppServerExtensionContext context,
        object contractResult,
        IRpcMethodDescriptor requestDescriptor,
        IRpcMethodDescriptor notificationDescriptor,
        object notificationParams)
    {
        if (!requestDescriptor.ResultType.IsInstanceOfType(contractResult))
            throw new InvalidOperationException(
                $"{requestDescriptor.Name} returned {contractResult.GetType().FullName}, expected {requestDescriptor.ResultType.FullName}.");
        if (!notificationDescriptor.ParamsType.IsInstanceOfType(notificationParams))
            throw new InvalidOperationException(
                $"{notificationDescriptor.Name} received {notificationParams.GetType().FullName}, expected {notificationDescriptor.ParamsType.FullName}.");

        await context.Transport.WriteMessageAsync(
            AppServerRequestHandler.BuildResponse(msg.Id, contractResult),
            context.CancellationToken);
        await context.Transport.NotifyContractAsync(
            notificationDescriptor.Name,
            notificationParams,
            context.CancellationToken);
        return null;
    }

    private static string RequireWorkspaceCraftPath(string method, AppServerExtensionContext context)
    {
        if (string.IsNullOrWhiteSpace(context.WorkspaceCraftPath))
            throw AppServerErrors.MethodNotFound(method);
        return context.WorkspaceCraftPath!;
    }

    private static string RequireHostWorkspacePath(string method, AppServerExtensionContext context)
    {
        if (string.IsNullOrWhiteSpace(context.HostWorkspacePath))
            throw AppServerErrors.MethodNotFound(method);
        return context.HostWorkspacePath!;
    }

}
