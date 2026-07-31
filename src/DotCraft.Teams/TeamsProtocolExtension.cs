using System.Text.Json;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;

namespace DotCraft.Teams;

/// <summary>
/// AppServer JSON-RPC surface for the Desktop Team.
/// </summary>
public sealed class TeamsProtocolExtension(TeamsService teamsService) : IAppServerProtocolExtension
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

    public void ContributeCapabilities(AppServerCapabilityBuilder builder)
    {
        if (!string.IsNullOrWhiteSpace(builder.WorkspaceCraftPath))
            builder.SetExtension("teams", new TeamsCapabilities { Team = true, Missions = true });
    }

    public async Task<object?> HandleAsync(AppServerIncomingMessage msg, AppServerExtensionContext context)
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
                return await teamsService.ViewTeamAsync(context.SessionService, workspaceCraftPath, ct);

            case MissionCreate:
            {
                var p = GetParams<TeamsMissionCreateParams>(msg);
                var result = await teamsService.CreateMissionAsync(
                    context.SessionService,
                    workspacePath,
                    workspaceCraftPath,
                    p,
                    ct);
                return await SendNotificationAfterResponseAsync(
                    msg,
                    context,
                    result,
                    TeamChanged,
                    new TeamsTeamChangedNotification
                    {
                        Reason = "missionCreated",
                        MissionId = result.Mission.MissionId
                    });
            }

            case MissionCancel:
            {
                var p = GetParams<TeamsMissionCancelParams>(msg);
                var result = await teamsService.CancelMissionAsync(context.SessionService, workspaceCraftPath, p, ct);
                return await SendNotificationAfterResponseAsync(
                    msg,
                    context,
                    result,
                    TeamChanged,
                    new TeamsTeamChangedNotification
                    {
                        Reason = "missionCancelled",
                        MissionId = p.MissionId
                    });
            }

            case MissionArchive:
            {
                var p = GetParams<TeamsMissionArchiveParams>(msg);
                var result = await teamsService.ArchiveMissionAsync(context.SessionService, workspaceCraftPath, p, ct);
                return await SendNotificationAfterResponseAsync(
                    msg,
                    context,
                    result,
                    TeamChanged,
                    new TeamsTeamChangedNotification
                    {
                        Reason = "missionArchived",
                        MissionId = p.MissionId
                    });
            }

            case MemberOpenThread:
            {
                var p = GetParams<TeamsMemberOpenThreadParams>(msg);
                return teamsService.OpenMemberThread(workspaceCraftPath, p);
            }

            default:
                throw AppServerErrors.MethodNotFound(method);
        }
    }

    private static async Task<object?> SendNotificationAfterResponseAsync(
        AppServerIncomingMessage msg,
        AppServerExtensionContext context,
        object result,
        string notificationMethod,
        object notificationParams)
    {
        await context.Transport.WriteMessageAsync(
            AppServerRequestHandler.BuildResponse(msg.Id, result),
            context.CancellationToken);
        await context.Transport.WriteMessageAsync(
            new
            {
                jsonrpc = "2.0",
                method = notificationMethod,
                @params = notificationParams
            },
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

    private static T GetParams<T>(AppServerIncomingMessage msg)
        where T : new()
    {
        if (!msg.Params.HasValue || msg.Params.Value.ValueKind == JsonValueKind.Null)
            return new T();

        try
        {
            return JsonSerializer.Deserialize<T>(
                msg.Params.Value.GetRawText(),
                SessionWireJsonOptions.Default) ?? new T();
        }
        catch (JsonException ex)
        {
            throw AppServerErrors.InvalidParams($"Failed to deserialize params: {ex.Message}");
        }
    }
}
