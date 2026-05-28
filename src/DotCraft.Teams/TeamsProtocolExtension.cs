using System.Text.Json;
using DotCraft.AppBinding;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;

namespace DotCraft.Teams;

/// <summary>
/// AppServer JSON-RPC surface for the Desktop Team.
/// </summary>
public sealed class TeamsProtocolExtension(
    TeamsService teamsService,
    AppBindingService appBindingService) : IAppServerProtocolExtension
{
    private const string TeamView = "teams/team/view";
    private const string TeamEnable = "teams/team/enable";
    private const string MissionCreate = "teams/mission/create";
    private const string MissionCancel = "teams/mission/cancel";
    private const string MissionArchive = "teams/mission/archive";
    private const string MemberOpenThread = "teams/member/openThread";
    private const string TeamChanged = "teams/team/changed";

    public IReadOnlyCollection<string> Methods { get; } =
    [
        TeamView,
        TeamEnable,
        MissionCreate,
        MissionCancel,
        MissionArchive,
        MemberOpenThread
    ];

    public void ContributeCapabilities(AppServerCapabilityBuilder builder)
    {
        if (!string.IsNullOrWhiteSpace(builder.WorkspaceCraftPath))
            builder.SetExtension("teams", new { team = true, missions = true });
    }

    public async Task<object?> HandleAsync(AppServerIncomingMessage msg, AppServerExtensionContext context)
    {
        var method = msg.Method ?? string.Empty;
        var workspaceCraftPath = RequireWorkspaceCraftPath(method, context);
        var workspacePath = RequireHostWorkspacePath(method, context);
        var ct = context.CancellationToken;
        teamsService.SetRuntimeServices(appBindingService, context.SessionService);
        if (!teamsService.IsAgentTeamsPluginEnabled(workspacePath, workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(method);

        switch (method)
        {
            case TeamView:
                return await teamsService.ViewTeamAsync(context.SessionService, workspaceCraftPath, ct);

            case TeamEnable:
            {
                _ = GetParams<TeamsTeamEnableParams>(msg);
                var result = await teamsService.EnableTeamAsync(
                    appBindingService,
                    context.SessionService,
                    workspacePath,
                    workspaceCraftPath,
                    ct);
                return await SendNotificationAfterResponseAsync(msg, context, result, TeamChanged, new { reason = "enabled" });
            }

            case MissionCreate:
            {
                var p = GetParams<TeamsMissionCreateParams>(msg);
                var result = await teamsService.CreateMissionAsync(
                    appBindingService,
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
                    new { reason = "missionCreated", missionId = result.Mission.MissionId });
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
                    new { reason = "missionCancelled", missionId = p.MissionId });
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
                    new { reason = "missionArchived", missionId = p.MissionId });
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
