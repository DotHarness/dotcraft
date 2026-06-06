using System.Collections.Concurrent;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DotCraft.Abstractions;
using DotCraft.AppBinding;
using DotCraft.Configuration;
using DotCraft.Plugins;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using Microsoft.Extensions.AI;

namespace DotCraft.Teams;

/// <summary>
/// Managed App Binding runtime and state owner for the DotCraft Team.
/// </summary>
public sealed class TeamsService(IAppConfigMonitor? appConfigMonitor = null) : IManagedAppBindingRuntime, IThreadRuntimeSignalObserver
{
    private const string CreateTeamToolName = "CreateTeam";

    private static readonly IReadOnlyList<string> TeamScopes =
    [
        "team.read",
        "mission.manage",
        "task.dispatch",
        "message.send",
        "artifact.publish"
    ];

    private static readonly HashSet<string> LeaderToolNames = new(StringComparer.Ordinal)
    {
        "CreateMissionPlan",
        "AssignTask",
        "ListTeamMembers",
        "ReadMissionState",
        "ReadMemberStatus",
        "SendMessage",
        "MarkMissionDone"
    };

    private static readonly HashSet<string> TeammateToolNames = new(StringComparer.Ordinal)
    {
        "ListTeamMembers",
        "ReadMissionState",
        "ReadMemberStatus",
        "SendMessage",
        "ReportProgress",
        "PublishArtifact",
        "MarkTaskDone"
    };

    private static readonly HashSet<string> ExternalThreadToolNames = new(StringComparer.Ordinal)
    {
        CreateTeamToolName
    };

    private static readonly IReadOnlySet<string> CatalogSurfaceSet = new HashSet<string>(StringComparer.Ordinal)
    {
        AppBindingCatalogSurfaces.Welcome,
        AppBindingCatalogSurfaces.ThreadBinding
    };

    private const string LeaderAvatarAccent = "#4f7cf6";
    private const string LegacyLeaderAvatarAccent = "#ef4444";

    private readonly ConcurrentDictionary<string, TeamsStateStore> _stores = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _schedulerLocks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ManagedDynamicToolRegistry<TeamsService> DynamicTools = new(TeamsConstants.ToolNamespace);
    private static readonly Regex ArtifactReferencePattern = new(@"\bartifact_[A-Za-z0-9_-]+\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TaskAliasPattern = new(@"^t[1-9][0-9]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ArtifactAliasPattern = new(@"^a[1-9][0-9]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ArtifactAliasReferencePattern = new(@"(?<![A-Za-z0-9_])a[1-9][0-9]*(?![A-Za-z0-9_])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private AppBindingService? _appBindingService;
    private ISessionService? _sessionService;

    public AppDescriptor Descriptor { get; } = BuildDescriptor();

    public string? OwningPluginId => PluginIds.AgentTeams;

    public IReadOnlySet<string> CatalogSurfaces => CatalogSurfaceSet;

    public bool RequiresExternalConnection => false;

    public IReadOnlyList<DynamicToolSpec> ToolSpecs =>
        DynamicTools.ToolSpecs
            .Where(tool => !ExternalThreadToolNames.Contains(tool.Name))
            .ToList();

    public bool AllowDirectMutatingToolExposure => true;

    public AppDescriptor GetCatalogDescriptor(string surface) =>
        IsExternalThreadCatalogSurface(surface) ? BuildThreadBindingDescriptor() : Descriptor;

    public IReadOnlyList<DynamicToolSpec> GetToolSpecsForSurface(string surface) =>
        string.Equals(AppBindingCatalogSurfaces.Normalize(surface), ManagedAppBindingToolSurfaces.ThreadBinding, StringComparison.Ordinal)
            ? DynamicTools.ToolSpecs.Where(tool => ExternalThreadToolNames.Contains(tool.Name)).ToList()
            : ToolSpecs;

    /// <summary>
    /// Returns whether the workspace has installed and enabled the product plugin
    /// that exposes the Team card board and runtime surface.
    /// </summary>
    public bool IsAgentTeamsPluginEnabled(string workspacePath, string workspaceCraftPath)
    {
        if (appConfigMonitor == null)
            return true;

        return PluginRuntimeConfigurator.IsPluginInstalledAndEnabled(
            appConfigMonitor.Current,
            workspacePath,
            workspaceCraftPath,
            PluginIds.AgentTeams);
    }

    public void SetRuntimeServices(AppBindingService appBindingService, ISessionService sessionService)
    {
        _appBindingService = appBindingService;
        _sessionService = sessionService;
    }

    public async Task<TeamsTeamViewResult> ViewTeamAsync(
        ISessionService sessionService,
        string workspaceCraftPath,
        CancellationToken ct)
    {
        var state = GetStore(workspaceCraftPath).Update(write =>
        {
            NormalizeLegacyState(write);
            EnsureMissionScratchpads(write, workspaceCraftPath);
            return write;
        });
        return await BuildViewAsync(sessionService, state, ct);
    }

    public async Task<TeamsTeamViewResult> EnableTeamAsync(
        AppBindingService appBindingService,
        ISessionService sessionService,
        string workspacePath,
        string workspaceCraftPath,
        CancellationToken ct)
    {
        SetRuntimeServices(appBindingService, sessionService);
        var state = await EnsureTeamAsync(appBindingService, sessionService, workspacePath, workspaceCraftPath, ct);
        return await BuildViewAsync(sessionService, state, ct);
    }

    public async Task<TeamsMissionCreateResult> CreateMissionAsync(
        AppBindingService appBindingService,
        ISessionService sessionService,
        string workspacePath,
        string workspaceCraftPath,
        TeamsMissionCreateParams p,
        CancellationToken ct,
        TeamsMissionOrigin? origin = null)
    {
        if (string.IsNullOrWhiteSpace(p.Title))
            throw AppServerErrors.InvalidParams("'title' is required.");
        if (string.IsNullOrWhiteSpace(p.Prompt))
            throw AppServerErrors.InvalidParams("'prompt' is required.");

        SetRuntimeServices(appBindingService, sessionService);
        var state = await EnsureTeamAsync(appBindingService, sessionService, workspacePath, workspaceCraftPath, ct);
        var leader = RequireMember(state, "leader");
        var now = DateTimeOffset.UtcNow;
        var mission = new MissionRecord
        {
            MissionId = $"mission_{Guid.NewGuid():N}",
            Title = p.Title.Trim(),
            Prompt = p.Prompt.Trim(),
            Status = TeamMissionStatuses.Planning,
            CreatedAt = now,
            UpdatedAt = now,
            OriginThreadId = origin?.ThreadId,
            OriginBindingId = origin?.BindingId
        };
        EnsureMissionScratchpad(mission, workspaceCraftPath);

        var saved = GetStore(workspaceCraftPath).Update(write =>
        {
            write.Missions.Add(mission);
            write.Team.Enabled = true;
            write.Team.UpdatedAt = now;
            return write;
        });

        var leaderMissionThread = await EnsureMissionMemberThreadAsync(
            appBindingService,
            sessionService,
            workspacePath,
            workspaceCraftPath,
            saved,
            mission.MissionId,
            leader,
            ct);
        mission.LeaderThreadId = leaderMissionThread.ThreadId;
        saved = GetStore(workspaceCraftPath).Update(write =>
        {
            RequireMission(write, mission.MissionId).LeaderThreadId = leaderMissionThread.ThreadId;
            write.Team.UpdatedAt = DateTimeOffset.UtcNow;
            return write;
        });

        var input = BuildLeaderMissionInput(mission);
        var queued = await EnqueueForMissionThreadAsync(
            appBindingService,
            sessionService,
            workspaceCraftPath,
            leaderMissionThread,
            input,
            triggerLabel: $"Mission: {mission.Title}",
            triggerRefId: mission.MissionId,
            ct);
        var latest = GetStore(workspaceCraftPath).Snapshot();
        return new TeamsMissionCreateResult
        {
            Mission = mission,
            QueuedInput = queued,
            Team = await BuildViewAsync(sessionService, latest, ct)
        };
    }

    public async Task<TeamsTeamViewResult> CancelMissionAsync(
        ISessionService sessionService,
        string workspaceCraftPath,
        TeamsMissionCancelParams p,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(p.MissionId))
            throw AppServerErrors.InvalidParams("'missionId' is required.");

        var state = GetStore(workspaceCraftPath).Update(write =>
        {
            var mission = write.Missions.FirstOrDefault(m => string.Equals(m.MissionId, p.MissionId, StringComparison.Ordinal))
                          ?? throw AppServerErrors.InvalidParams($"Mission '{p.MissionId}' was not found.");
            var now = DateTimeOffset.UtcNow;
            mission.Status = "cancelled";
            mission.UpdatedAt = now;
            foreach (var task in write.Tasks.Where(t => string.Equals(t.MissionId, mission.MissionId, StringComparison.Ordinal)))
            {
                if (task.Status is not "done")
                {
                    task.Status = "cancelled";
                    task.UpdatedAt = now;
                }
            }

            foreach (var missionThread in write.MissionThreads.Where(t => string.Equals(t.MissionId, mission.MissionId, StringComparison.Ordinal)))
            {
                missionThread.Status = "cancelled";
                missionThread.CurrentTaskId = null;
                missionThread.QueuedInputId = null;
                missionThread.UpdatedAt = now;
            }

            write.Team.UpdatedAt = now;
            return write;
        });
        await StopMissionThreadsAsync(sessionService, state.MissionThreads.Where(t => string.Equals(t.MissionId, p.MissionId, StringComparison.Ordinal)), ct);
        RefreshContexts(workspaceCraftPath, state);
        return await BuildViewAsync(sessionService, state, ct);
    }

    public async Task<TeamsTeamViewResult> ArchiveMissionAsync(
        ISessionService sessionService,
        string workspaceCraftPath,
        TeamsMissionArchiveParams p,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(p.MissionId))
            throw AppServerErrors.InvalidParams("'missionId' is required.");

        var state = GetStore(workspaceCraftPath).Update(write =>
        {
            var mission = write.Missions.FirstOrDefault(m => string.Equals(m.MissionId, p.MissionId, StringComparison.Ordinal))
                          ?? throw AppServerErrors.InvalidParams($"Mission '{p.MissionId}' was not found.");
            if (!IsTerminalMissionStatus(mission.Status))
                throw AppServerErrors.InvalidParams($"Mission '{p.MissionId}' must be done or cancelled before it can be archived.");

            var now = DateTimeOffset.UtcNow;
            mission.ArchivedAt ??= now;
            mission.UpdatedAt = now;
            foreach (var missionThread in write.MissionThreads.Where(t => string.Equals(t.MissionId, mission.MissionId, StringComparison.Ordinal)))
            {
                missionThread.ArchivedAt ??= now;
                missionThread.Status = "archived";
                missionThread.UpdatedAt = now;
            }

            write.Team.UpdatedAt = now;
            return write;
        });
        foreach (var missionThread in state.MissionThreads.Where(t => string.Equals(t.MissionId, p.MissionId, StringComparison.Ordinal)))
        {
            if (!string.IsNullOrWhiteSpace(missionThread.ThreadId))
                await sessionService.ArchiveThreadAsync(missionThread.ThreadId, ct);
        }

        RefreshContexts(workspaceCraftPath, state);
        return await BuildViewAsync(sessionService, state, ct);
    }

    public TeamsMemberOpenThreadResult OpenMemberThread(string workspaceCraftPath, TeamsMemberOpenThreadParams p)
    {
        var state = GetStore(workspaceCraftPath).Snapshot();
        MissionThreadRecord? missionThread;
        if (!string.IsNullOrWhiteSpace(p.TaskId))
        {
            var task = RequireTask(state, p.TaskId.Trim());
            missionThread = FindMissionThread(state, task.MissionId, task.AssigneeMemberId);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(p.MissionId))
                throw AppServerErrors.InvalidParams("'missionId' or 'taskId' is required.");
            if (string.IsNullOrWhiteSpace(p.MemberId))
                throw AppServerErrors.InvalidParams("'memberId' is required when 'taskId' is not provided.");
            _ = RequireMember(state, p.MemberId.Trim());
            missionThread = FindMissionThread(state, p.MissionId.Trim(), p.MemberId.Trim());
        }

        if (missionThread == null || string.IsNullOrWhiteSpace(missionThread.ThreadId))
            throw AppServerErrors.InvalidParams("The requested Mission teammate thread was not found.");
        return new TeamsMemberOpenThreadResult { ThreadId = missionThread.ThreadId };
    }

    public void OnThreadRuntimeSignal(string workspaceCraftPath, string threadId, SessionThreadRuntimeSignal signal)
    {
        _ = HandleThreadRuntimeSignalAsync(workspaceCraftPath, threadId, signal, CancellationToken.None);
    }

    public async Task HandleThreadRuntimeSignalAsync(
        string workspaceCraftPath,
        string threadId,
        SessionThreadRuntimeSignal signal,
        CancellationToken ct = default)
    {
        var sessionService = _sessionService;
        if (sessionService == null || string.IsNullOrWhiteSpace(workspaceCraftPath) || string.IsNullOrWhiteSpace(threadId))
            return;

        var snapshot = GetStore(workspaceCraftPath).Snapshot();
        var missionThread = snapshot.MissionThreads.FirstOrDefault(t => string.Equals(t.ThreadId, threadId, StringComparison.Ordinal));
        if (missionThread == null)
            return;
        var memberId = missionThread.MemberId;

        if (signal == SessionThreadRuntimeSignal.TurnStarted)
        {
            UpdateMissionThreadRuntimeState(workspaceCraftPath, threadId, "running");
            RefreshContexts(workspaceCraftPath, GetStore(workspaceCraftPath).Snapshot());
            return;
        }

        if (signal == SessionThreadRuntimeSignal.ApprovalRequested)
        {
            UpdateMissionThreadRuntimeState(workspaceCraftPath, threadId, "approval");
            RefreshContexts(workspaceCraftPath, GetStore(workspaceCraftPath).Snapshot());
            return;
        }

        if (signal == SessionThreadRuntimeSignal.UserInputRequested)
        {
            UpdateMissionThreadRuntimeState(workspaceCraftPath, threadId, "input");
            RefreshContexts(workspaceCraftPath, GetStore(workspaceCraftPath).Snapshot());
            return;
        }

        if (signal is SessionThreadRuntimeSignal.ApprovalResolved or SessionThreadRuntimeSignal.UserInputResolved)
        {
            UpdateMissionThreadRuntimeState(workspaceCraftPath, threadId, "running");
            RefreshContexts(workspaceCraftPath, GetStore(workspaceCraftPath).Snapshot());
            return;
        }

        if (signal is not (SessionThreadRuntimeSignal.TurnCompleted
            or SessionThreadRuntimeSignal.TurnCompletedAwaitingPlanConfirmation
            or SessionThreadRuntimeSignal.TurnFailed
            or SessionThreadRuntimeSignal.TurnCancelled))
        {
            return;
        }

        await MarkMissionThreadIdleOrQueuedAsync(sessionService, workspaceCraftPath, threadId, signal, ct);
        await TryStartNextForMemberAsync(sessionService, workspaceCraftPath, memberId, ct);
        var appBindingService = _appBindingService;
        if (appBindingService != null)
            await RunMissionSchedulerAsync(appBindingService, sessionService, ResolveWorkspacePath(workspaceCraftPath), workspaceCraftPath, missionThread.MissionId, ct);
    }

    public async ValueTask<DynamicToolCallResult> InvokeToolAsync(
        ManagedAppBindingToolCallContext context,
        JsonObject arguments,
        CancellationToken cancellationToken)
    {
        if (!IsAgentTeamsPluginEnabled(context.WorkspacePath, context.WorkspaceCraftPath))
            return Fail("AgentTeamsPluginDisabled", "The Agent Teams plugin is not installed or enabled for this workspace.");

        try
        {
            if (!DynamicTools.ContainsTool(context.ToolName))
                return Fail("UnknownTool", $"Teams tool '{context.ToolName}' is not supported.");
            return await DynamicTools.InvokeAsync(this, context, arguments, cancellationToken);
        }
        catch (AppServerException ex)
        {
            return Fail("InvalidArguments", FormatAppServerException(ex));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Fail("TeamsRuntimeError", ex.Message);
        }
    }

    private async Task<TeamsStateDocument> EnsureTeamAsync(
        AppBindingService appBindingService,
        ISessionService sessionService,
        string workspacePath,
        string workspaceCraftPath,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var store = GetStore(workspaceCraftPath);
        var state = store.Update(write =>
        {
            if (write.Team.CreatedAt == default)
                write.Team.CreatedAt = now;
            write.Team.Enabled = true;
            write.Team.UpdatedAt = now;
            EnsureDefaultMembers(write);
            NormalizeLegacyState(write);
            EnsureMissionScratchpads(write, workspaceCraftPath);
            return write;
        });
        return await RepairExistingMissionThreadsAsync(
            appBindingService,
            sessionService,
            workspaceCraftPath,
            state,
            ct);
    }

    private async Task<MissionThreadRecord> EnsureMissionMemberThreadAsync(
        AppBindingService appBindingService,
        ISessionService sessionService,
        string workspacePath,
        string workspaceCraftPath,
        TeamsStateDocument state,
        string missionId,
        TeamMemberRecord member,
        CancellationToken ct)
    {
        var mission = RequireMission(state, missionId);
        var existing = FindMissionThread(state, missionId, member.MemberId);
        if (existing != null && !string.IsNullOrWhiteSpace(existing.ThreadId))
        {
            try
            {
                var existingThread = await sessionService.GetThreadAsync(existing.ThreadId, ct);
                if (existingThread.Status == ThreadStatus.Archived && existing.ArchivedAt == null)
                {
                    await sessionService.UnarchiveThreadAsync(existingThread.Id, ct);
                    existingThread = await sessionService.GetThreadAsync(existingThread.Id, ct);
                }

                existing.GrantId = string.IsNullOrWhiteSpace(existing.GrantId)
                    ? $"teams-grant-{missionId}-{member.MemberId}"
                    : existing.GrantId;
                var binding = appBindingService.EnsureManagedBinding(
                    workspaceCraftPath,
                    existingThread.Id,
                    TeamsConstants.AppId,
                    TeamsConstants.UserId,
                    existing.GrantId,
                    TeamScopes,
                    BuildToolSpecsForMember(member));
                existing.BindingId = binding.BindingId;
                var repaired = GetStore(workspaceCraftPath).Update(write =>
                {
                    var current = RequireMissionThread(write, missionId, member.MemberId);
                    current.ThreadId = existingThread.Id;
                    current.BindingId = existing.BindingId;
                    current.GrantId = existing.GrantId;
                    current.UpdatedAt = DateTimeOffset.UtcNow;
                    write.Team.UpdatedAt = current.UpdatedAt;
                    return write;
                });
                var repairedThread = RequireMissionThread(repaired, missionId, member.MemberId);
                UpsertMissionThreadContextBlocks(appBindingService, workspaceCraftPath, repaired, repairedThread, member);
                var configUpdated = await EnsureMissionThreadRoleInstructionsAsync(sessionService, existingThread.Id, member, ct);
                if (!configUpdated)
                    await RefreshMissionThreadAgentAsync(sessionService, repairedThread.ThreadId, ct);
                return repairedThread;
            }
            catch
            {
                existing.ThreadId = string.Empty;
                existing.BindingId = string.Empty;
            }
        }

        var thread = await sessionService.CreateThreadAsync(
            new SessionIdentity
            {
                ChannelName = TeamsConstants.ChannelName,
                UserId = TeamsConstants.UserId,
                ChannelContext = $"{missionId}:{member.MemberId}",
                WorkspacePath = workspacePath
            },
            new ThreadConfiguration
            {
                Mode = "agent",
                RoleInstructions = BuildMissionThreadRoleInstructions(member)
            },
            displayName: $"DotCraft {member.DisplayName} - {mission.Title}",
            ct: ct);

        var now = DateTimeOffset.UtcNow;
        var missionThread = existing ?? new MissionThreadRecord
        {
            MissionId = missionId,
            MemberId = member.MemberId,
            CreatedAt = now
        };
        missionThread.ThreadId = thread.Id;
        missionThread.GrantId = string.IsNullOrWhiteSpace(missionThread.GrantId)
            ? $"teams-grant-{missionId}-{member.MemberId}"
            : missionThread.GrantId;
        missionThread.Status = string.IsNullOrWhiteSpace(missionThread.Status) ? "idle" : missionThread.Status;
        missionThread.UpdatedAt = now;

        var managedBinding = appBindingService.EnsureManagedBinding(
            workspaceCraftPath,
            missionThread.ThreadId,
            TeamsConstants.AppId,
            TeamsConstants.UserId,
            missionThread.GrantId,
            TeamScopes,
            BuildToolSpecsForMember(member));
        missionThread.BindingId = managedBinding.BindingId;

        var saved = GetStore(workspaceCraftPath).Update(write =>
        {
            var current = FindMissionThread(write, missionId, member.MemberId);
            if (current == null)
            {
                write.MissionThreads.Add(missionThread);
            }
            else
            {
                current.ThreadId = missionThread.ThreadId;
                current.BindingId = missionThread.BindingId;
                current.GrantId = missionThread.GrantId;
                current.Status = missionThread.Status;
                current.UpdatedAt = missionThread.UpdatedAt;
            }

            var currentMission = RequireMission(write, missionId);
            if (string.Equals(member.MemberId, "leader", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(currentMission.LeaderThreadId))
                currentMission.LeaderThreadId = missionThread.ThreadId;
            write.Team.UpdatedAt = now;
            return write;
        });
        var savedThread = FindMissionThread(saved, missionId, member.MemberId) ?? missionThread;
        UpsertMissionThreadContextBlocks(appBindingService, workspaceCraftPath, saved, savedThread, member);
        await RefreshMissionThreadAgentAsync(sessionService, savedThread.ThreadId, ct);
        return savedThread;
    }

    private async Task<TeamsStateDocument> RepairExistingMissionThreadsAsync(
        AppBindingService appBindingService,
        ISessionService sessionService,
        string workspaceCraftPath,
        TeamsStateDocument state,
        CancellationToken ct)
    {
        var repaired = state;
        foreach (var missionThread in state.MissionThreads
                     .Where(thread => thread.ArchivedAt == null
                                      && !string.IsNullOrWhiteSpace(thread.ThreadId))
                     .ToList())
        {
            var member = repaired.Members.FirstOrDefault(item =>
                string.Equals(item.MemberId, missionThread.MemberId, StringComparison.OrdinalIgnoreCase));
            if (member == null)
                continue;

            SessionThread thread;
            try
            {
                thread = await sessionService.GetThreadAsync(missionThread.ThreadId, ct);
                if (thread.Status == ThreadStatus.Archived)
                {
                    await sessionService.UnarchiveThreadAsync(thread.Id, ct);
                    thread = await sessionService.GetThreadAsync(thread.Id, ct);
                }
            }
            catch
            {
                continue;
            }

            var grantId = string.IsNullOrWhiteSpace(missionThread.GrantId)
                ? $"teams-grant-{missionThread.MissionId}-{missionThread.MemberId}"
                : missionThread.GrantId;
            var binding = appBindingService.EnsureManagedBinding(
                workspaceCraftPath,
                thread.Id,
                TeamsConstants.AppId,
                TeamsConstants.UserId,
                grantId,
                TeamScopes,
                BuildToolSpecsForMember(member));

            var now = DateTimeOffset.UtcNow;
            repaired = GetStore(workspaceCraftPath).Update(write =>
            {
                var current = FindMissionThread(write, missionThread.MissionId, missionThread.MemberId);
                if (current == null || current.ArchivedAt != null)
                    return write;

                current.ThreadId = thread.Id;
                current.BindingId = binding.BindingId;
                current.GrantId = grantId;
                current.UpdatedAt = now;
                write.Team.UpdatedAt = now;
                return write;
            });
            var repairedThread = FindMissionThread(repaired, missionThread.MissionId, missionThread.MemberId);
            if (repairedThread == null)
                continue;

            UpsertMissionThreadContextBlocks(appBindingService, workspaceCraftPath, repaired, repairedThread, member);
            var configUpdated = await EnsureMissionThreadRoleInstructionsAsync(sessionService, thread.Id, member, ct);
            if (!configUpdated)
                await RefreshMissionThreadAgentAsync(sessionService, thread.Id, ct);
        }

        return GetStore(workspaceCraftPath).Snapshot();
    }

    private static async Task<bool> EnsureMissionThreadRoleInstructionsAsync(
        ISessionService sessionService,
        string threadId,
        TeamMemberRecord member,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(threadId))
            return false;

        var thread = await sessionService.GetThreadAsync(threadId, ct);
        var config = thread.Configuration ?? new ThreadConfiguration { Mode = "agent" };
        var expected = BuildMissionThreadRoleInstructions(member);
        var needsUpdate = !string.Equals(config.RoleInstructions, expected, StringComparison.Ordinal)
                          || !string.IsNullOrWhiteSpace(config.AgentInstructions)
                          || config.OverrideBasePrompt;
        if (!needsUpdate)
            return false;

        config.RoleInstructions = expected;
        config.AgentInstructions = null;
        config.OverrideBasePrompt = false;
        await sessionService.UpdateThreadConfigurationAsync(threadId, config, ct);
        return true;
    }

    private static async Task RefreshMissionThreadAgentAsync(
        ISessionService sessionService,
        string threadId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(threadId))
            return;

        if (sessionService is IThreadAgentRefreshService refreshService)
            await refreshService.RefreshThreadAgentAsync(threadId, ct);
    }

    private void UpsertMissionThreadContextBlocks(
        AppBindingService appBindingService,
        string workspaceCraftPath,
        TeamsStateDocument state,
        MissionThreadRecord missionThread,
        TeamMemberRecord member)
    {
        if (string.IsNullOrWhiteSpace(missionThread.BindingId) || string.IsNullOrWhiteSpace(missionThread.GrantId))
            return;

        var roleContent = BuildRoleContext(member);
        var missionContent = BuildMissionContext(state, missionThread, member);
        var policyContent = "Coordinate through explicit Teams tools. App Context contains fixed role and mission context only; use ListTeamMembers, ReadMissionState, and ReadMemberStatus for live state. Do not treat digests or Teams messages as user-authored conversation history.";
        appBindingService.UpsertManagedContextBlock(workspaceCraftPath, new AppBindingContextUpsertParams
        {
            BindingId = missionThread.BindingId,
            AppId = TeamsConstants.AppId,
            GrantId = missionThread.GrantId,
            BlockId = "role",
            Kind = AppContextBlockKinds.Role,
            Title = $"{member.DisplayName} Role",
            Content = roleContent,
            Order = 10,
            Version = ComputeContextBlockVersion(roleContent)
        });
        appBindingService.UpsertManagedContextBlock(workspaceCraftPath, new AppBindingContextUpsertParams
        {
            BindingId = missionThread.BindingId,
            AppId = TeamsConstants.AppId,
            GrantId = missionThread.GrantId,
            BlockId = "mission",
            Kind = AppContextBlockKinds.Mission,
            Title = "Current Mission",
            Content = missionContent,
            Order = 20,
            Version = ComputeContextBlockVersion(missionContent)
        });
        appBindingService.UpsertManagedContextBlock(workspaceCraftPath, new AppBindingContextUpsertParams
        {
            BindingId = missionThread.BindingId,
            AppId = TeamsConstants.AppId,
            GrantId = missionThread.GrantId,
            BlockId = "policy",
            Kind = AppContextBlockKinds.Policy,
            Title = "Teams Runtime Policy",
            Content = policyContent,
            Order = 30,
            Version = ComputeContextBlockVersion(policyContent)
        });
    }

    private async Task<QueuedTurnInput> EnqueueForMissionThreadAsync(
        AppBindingService appBindingService,
        ISessionService sessionService,
        string workspaceCraftPath,
        MissionThreadRecord missionThread,
        TeamQueuedInput input,
        string triggerLabel,
        string triggerRefId,
        CancellationToken ct)
    {
        var modelPart = new SessionWireInputPart { Type = "text", Text = input.ModelText };
        var displayPart = new SessionWireInputPart { Type = "text", Text = input.DisplayText };
        using (TurnTriggerScope.Set(new TurnTriggerInfo
               {
                   Kind = "team",
                   Label = triggerLabel,
                   RefId = triggerRefId
               }))
        {
            var queued = await sessionService.EnqueueTurnInputAsync(
                missionThread.ThreadId,
                [new TextContent(input.ModelText)],
                sender: null,
                ct,
                new SessionInputSnapshot
                {
                    NativeInputParts = [displayPart],
                    MaterializedInputParts = [modelPart],
                    DisplayText = input.DisplayText
                });
            appBindingService.RecordThreadInputEnqueued(
                workspaceCraftPath,
                missionThread.BindingId,
                queued.Id,
                "team",
                triggerLabel,
                triggerRefId);
            var state = GetStore(workspaceCraftPath).Update(write =>
            {
                var current = RequireMissionThread(write, missionThread.MissionId, missionThread.MemberId);
                current.QueuedInputId = queued.Id;
                if (!IsBusyMissionThreadStatus(current.Status))
                    current.Status = "queued";
                current.UpdatedAt = DateTimeOffset.UtcNow;
                write.Team.UpdatedAt = current.UpdatedAt;
                return write;
            });
            RefreshContexts(workspaceCraftPath, state);
            await TryStartNextForMemberAsync(sessionService, workspaceCraftPath, missionThread.MemberId, ct);
            return queued;
        }
    }

    [DynamicTool(CreateTeamToolName, Order = 0)]
    [Description("Start an asynchronous DotCraft Team mission from the current thread.")]
    private async Task<DynamicToolCallResult> CreateTeamToolAsync(
        ManagedAppBindingToolCallContext context,
        CancellationToken ct,
        [Description("Short title for the Team mission.")] string title,
        [Description("Mission prompt for the Team to execute.")] string prompt)
    {
        var appBindingService = context.AppBindingService ?? _appBindingService
            ?? throw AppServerErrors.InvalidParams("Teams runtime services are not available.");
        var sessionService = context.SessionService ?? _sessionService
            ?? throw AppServerErrors.InvalidParams("Teams session service is not available.");

        var existingMissionThread = GetStore(context.WorkspaceCraftPath).Snapshot().MissionThreads
            .Any(thread => string.Equals(thread.ThreadId, context.ThreadId, StringComparison.Ordinal));
        if (existingMissionThread)
            throw AppServerErrors.InvalidParams("Team Mission threads cannot create nested Team missions.");

        SetRuntimeServices(appBindingService, sessionService);
        var result = await CreateMissionAsync(
            appBindingService,
            sessionService,
            context.WorkspacePath,
            context.WorkspaceCraftPath,
            new TeamsMissionCreateParams { Title = title, Prompt = prompt },
            ct,
            new TeamsMissionOrigin(context.ThreadId, context.BindingId));
        var queuedInput = result.QueuedInput
            ?? throw new InvalidOperationException("Team mission did not enqueue Leader input.");

        return Ok("Team mission started.", new
        {
            missionId = result.Mission.MissionId,
            title = result.Mission.Title,
            leaderThreadId = result.Mission.LeaderThreadId,
            queuedInputId = queuedInput.Id,
            status = result.Mission.Status
        });
    }

    [DynamicTool("CreateMissionPlan", Order = 10)]
    [Description("Record a mission plan before assigning work.")]
    private async Task<DynamicToolCallResult> CreateMissionPlanToolAsync(
        ManagedAppBindingToolCallContext context,
        CancellationToken ct,
        [Description("Concise plan for the current mission.")] string plan)
    {
        var missionId = string.Empty;
        var state = GetStore(context.WorkspaceCraftPath).Update(write =>
        {
            var callerThread = RequireMissionCaller(write, context);
            missionId = callerThread.MissionId;
            var mission = RequireMission(write, missionId);
            EnsureMissionCanReceiveWork(mission);
            if (!string.Equals(callerThread.MemberId, "leader", StringComparison.OrdinalIgnoreCase))
                throw AppServerErrors.InvalidParams($"Mission '{missionId}' can only be planned by its Leader thread.");
            mission.Plan = plan;
            if (mission.Status == TeamMissionStatuses.Planning)
                mission.Status = TeamMissionStatuses.Active;
            mission.UpdatedAt = DateTimeOffset.UtcNow;
            write.Team.UpdatedAt = mission.UpdatedAt;
            return write;
        });
        RefreshContexts(context.WorkspaceCraftPath, state);
        await RunMissionSchedulerAsync(
            _appBindingService ?? throw new InvalidOperationException("Teams App Binding service is not available."),
            _sessionService ?? throw new InvalidOperationException("Teams Session service is not available."),
            context.WorkspacePath,
            context.WorkspaceCraftPath,
            missionId,
            ct);
        return Ok("Mission plan recorded.", state.Missions.First(m => string.Equals(m.MissionId, missionId, StringComparison.Ordinal)));
    }

    [DynamicTool("AssignTask", Order = 20)]
    [Description("Create a Teams task and let the scheduler dispatch it when ready.")]
    private async Task<DynamicToolCallResult> AssignTaskToolAsync(
        ManagedAppBindingToolCallContext context,
        CancellationToken ct,
        [Description("Assignee member id, role, or display name.")] string assignee,
        [Description("Task title.")] string title,
        [Description("Task prompt for the member.")] string prompt,
        [Description("Upstream task aliases or canonical task ids that must be done before this task can run.")] List<string>? dependsOnTaskIds = null,
        [Description("Task kind, such as work or review.")] string? kind = null,
        [Description("Whether dependencies release this task to Leader synthesis before teammate dispatch.")] bool? requiresLeaderSynthesis = null)
    {
        var appBindingService = _appBindingService ?? throw new InvalidOperationException("Teams App Binding service is not available.");
        var sessionService = _sessionService ?? throw new InvalidOperationException("Teams Session service is not available.");
        var needsLeaderSynthesis = requiresLeaderSynthesis ?? false;
        dependsOnTaskIds ??= [];
        var missionId = string.Empty;
        var now = DateTimeOffset.UtcNow;
        TeamMemberRecord member = new();
        TeamTaskRecord task = new();
        var state = GetStore(context.WorkspaceCraftPath).Update(write =>
        {
            var callerThread = RequireMissionCaller(write, context);
            missionId = callerThread.MissionId;
            var mission = RequireMission(write, missionId);
            EnsureMissionCanReceiveWork(mission);
            if (!string.Equals(callerThread.MemberId, "leader", StringComparison.OrdinalIgnoreCase))
                throw AppServerErrors.InvalidParams($"Mission '{missionId}' can only dispatch tasks from its Leader thread.");
            member = ResolveAssignee(write, assignee);
            var taskKind = kind ?? (string.Equals(member.Role, "reviewer", StringComparison.OrdinalIgnoreCase)
                || string.Equals(member.MemberId, "reviewer", StringComparison.OrdinalIgnoreCase)
                    ? "review"
                    : "work");
            EnsureMissionScopedAliases(write);
            var resolvedDependencyIds = new List<string>();
            foreach (var dependencyId in dependsOnTaskIds.Where(item => !string.IsNullOrWhiteSpace(item)))
            {
                var dependency = RequireMissionTaskReference(write, missionId, dependencyId);
                resolvedDependencyIds.Add(dependency.TaskId);
            }

            task = new TeamTaskRecord
            {
                TaskId = $"task_{Guid.NewGuid():N}",
                Alias = NextTaskAlias(write, missionId),
                MissionId = missionId,
                AssigneeMemberId = member.MemberId,
                Title = title,
                Prompt = prompt,
                Status = TeamTaskStatuses.Pending,
                Kind = NormalizeTaskKind(taskKind),
                RequiredForMission = true,
                RequiresLeaderSynthesis = needsLeaderSynthesis,
                DependsOnTaskIds = resolvedDependencyIds,
                LatestUpdate = $"Assigned to {member.DisplayName}.",
                CreatedAt = now,
                UpdatedAt = now,
                Digest = $"Assigned to {member.DisplayName}."
            };
            write.Tasks.Add(task);
            if (mission.Status is TeamMissionStatuses.Planning or TeamMissionStatuses.AwaitingLeaderReview)
                mission.Status = TeamMissionStatuses.Active;
            mission.LeaderContinuationQueuedInputId = null;
            mission.UpdatedAt = now;
            UpsertDigest(write, member.MemberId, $"New task: {task.Title}");
            write.Team.UpdatedAt = now;
            return write;
        });

        RefreshContexts(context.WorkspaceCraftPath, state);
        await RunMissionSchedulerAsync(
            appBindingService,
            sessionService,
            context.WorkspacePath,
            context.WorkspaceCraftPath,
            missionId,
            ct);
        var latest = GetStore(context.WorkspaceCraftPath).Snapshot();
        return Ok($"Task assigned to {member.DisplayName}.", RequireTask(latest, task.TaskId));
    }

    [DynamicTool("ListTeamMembers", Order = 30)]
    [Description("Read Team roster and teammate availability summaries.")]
    private DynamicToolCallResult ListTeamMembersTool(
        ManagedAppBindingToolCallContext context)
    {
        var state = GetStore(context.WorkspaceCraftPath).Snapshot();
        var callerThread = RequireMissionCaller(state, context);
        var missionId = callerThread.MissionId;

        var members = state.Members
            .OrderBy(member => member.MemberId == "leader" ? 0 : 1)
            .ThenBy(member => member.DisplayName, StringComparer.Ordinal)
            .Select(member => BuildMemberStatusSummary(state, member, missionId))
            .ToList();
        return Ok("Team members loaded.", new { missionId, members });
    }

    [DynamicTool("ReadMissionState", Order = 40)]
    [Description("Read Mission-scoped task, thread, digest, artifact, and message summaries.")]
    private DynamicToolCallResult ReadMissionStateTool(
        ManagedAppBindingToolCallContext context)
    {
        var state = GetStore(context.WorkspaceCraftPath).Snapshot();
        var callerThread = RequireMissionCaller(state, context);
        var mission = RequireMission(state, callerThread.MissionId);
        return Ok("Mission state loaded.", BuildMissionStateSummary(state, mission));
    }

    [DynamicTool("ReadMemberStatus", Order = 50)]
    [Description("Read one teammate's current status and recent progress.")]
    private DynamicToolCallResult ReadMemberStatusTool(
        ManagedAppBindingToolCallContext context,
        [Description("Member id, role, or display name.")] string memberId)
    {
        var state = GetStore(context.WorkspaceCraftPath).Snapshot();
        var callerThread = RequireMissionCaller(state, context);
        var missionId = callerThread.MissionId;
        var member = RequireMember(state, memberId);
        return Ok("Member status loaded.", BuildMemberStatusSummary(state, member, missionId, includeHistory: true));
    }

    [DynamicTool("SendMessage", Order = 60)]
    [Description("Send a lightweight Mission-scoped message to the Leader or a participating teammate.")]
    private async Task<DynamicToolCallResult> SendMessageToolAsync(
        ManagedAppBindingToolCallContext context,
        CancellationToken ct,
        [Description("Target member id, role, display name, or 'leader'.")] string to,
        [Description("Message for the teammate.")] string message,
        [Description("Optional related task alias or canonical task id.")] string? taskId = null)
    {
        var appBindingService = _appBindingService ?? throw new InvalidOperationException("Teams App Binding service is not available.");
        var sessionService = _sessionService ?? throw new InvalidOperationException("Teams Session service is not available.");
        var missionId = string.Empty;
        TeamMessageRecord messageRecord = new();
        var targetMember = new TeamMemberRecord();
        MissionThreadRecord callerThread = new();

        var state = GetStore(context.WorkspaceCraftPath).Update(write =>
        {
            callerThread = RequireMissionCaller(write, context);
            missionId = callerThread.MissionId;
            var mission = RequireMission(write, missionId);
            EnsureMissionCanReceiveWork(mission);

            targetMember = RequireMember(write, to);
            EnsureMissionScopedAliases(write);
            var targetThread = FindMissionThread(write, missionId, targetMember.MemberId);
            var targetHasMissionWork = write.Tasks.Any(task =>
                string.Equals(task.MissionId, missionId, StringComparison.Ordinal)
                && string.Equals(task.AssigneeMemberId, targetMember.MemberId, StringComparison.OrdinalIgnoreCase));
            var targetParticipates = string.Equals(targetMember.MemberId, "leader", StringComparison.OrdinalIgnoreCase)
                                     || targetHasMissionWork
                                     || targetThread is { ThreadId: { Length: > 0 }, ArchivedAt: null };
            if (!targetParticipates)
                throw AppServerErrors.InvalidParams($"Member '{targetMember.MemberId}' has no Mission thread for mission '{missionId}'. Assign a task to this teammate before sending an actionable message.");

            var relatedTask = ResolveMessageTask(write, missionId, callerThread, targetMember.MemberId, taskId);
            var artifactIds = ExtractMentionedArtifactIds(write, missionId, message);
            var messageKind = InferMessageKind(write, callerThread, targetMember.MemberId, relatedTask);

            var now = DateTimeOffset.UtcNow;
            var messageId = $"msg_{Guid.NewGuid():N}";
            messageRecord = new TeamMessageRecord
            {
                MessageId = messageId,
                MissionId = missionId,
                FromMemberId = callerThread.MemberId,
                ToMemberId = targetMember.MemberId,
                TaskId = relatedTask?.TaskId,
                Content = message,
                Kind = messageKind,
                RequiresAction = true,
                Status = TeamMessageStatuses.Recorded,
                ArtifactIds = artifactIds,
                CreatedAt = now
            };
            write.Messages.Add(messageRecord);
            if (relatedTask != null
                && relatedTask.RequiresLeaderSynthesis
                && string.Equals(callerThread.MemberId, "leader", StringComparison.OrdinalIgnoreCase)
                && string.Equals(relatedTask.AssigneeMemberId, targetMember.MemberId, StringComparison.OrdinalIgnoreCase))
            {
                relatedTask.SynthesisMessageId = messageId;
                relatedTask.UpdatedAt = now;
            }

            mission.LeaderContinuationQueuedInputId = null;
            mission.UpdatedAt = now;
            UpsertDigest(write, targetMember.MemberId, $"{RequireMember(write, callerThread.MemberId).DisplayName} message: {Truncate(message, 180)}");
            write.Team.UpdatedAt = now;
            return write;
        });

        RefreshContexts(context.WorkspaceCraftPath, state);
        await RunMissionSchedulerAsync(
            appBindingService,
            sessionService,
            context.WorkspacePath,
            context.WorkspaceCraftPath,
            missionId,
            ct);
        return Ok("Team message recorded.", messageRecord);
    }

    [DynamicTool("ReportProgress", Order = 80)]
    [Description("Record progress for a Teams task.")]
    private async Task<DynamicToolCallResult> ReportProgressToolAsync(
        ManagedAppBindingToolCallContext context,
        CancellationToken ct,
        [Description("Progress summary.")] string summary,
        [Description("Progress status: running or blocked.")] string? status = null,
        [Description("Task aliases or canonical task ids this task is blocked on.")] List<string>? blockedOnTaskIds = null)
    {
        var appBindingService = _appBindingService ?? throw new InvalidOperationException("Teams App Binding service is not available.");
        var sessionService = _sessionService ?? throw new InvalidOperationException("Teams Session service is not available.");
        var progressStatus = NormalizeProgressStatus(status ?? TeamTaskStatuses.Running);
        var effectiveBlockedReason = progressStatus == TeamTaskStatuses.Blocked ? summary : null;
        blockedOnTaskIds ??= [];
        var missionId = string.Empty;
        var taskId = string.Empty;
        var state = GetStore(context.WorkspaceCraftPath).Update(write =>
        {
            var callerThread = RequireMissionCaller(write, context);
            var task = RequireCallerTask(write, callerThread);
            taskId = task.TaskId;
            missionId = task.MissionId;
            EnsureTaskMissionCanReceiveWork(write, task);
            EnsureMissionScopedAliases(write);
            var resolvedBlockedOnTaskIds = new List<string>();
            foreach (var blockedOnTaskId in blockedOnTaskIds.Where(item => !string.IsNullOrWhiteSpace(item)))
            {
                var blockedOnTask = RequireMissionTaskReference(write, task.MissionId, blockedOnTaskId);
                resolvedBlockedOnTaskIds.Add(blockedOnTask.TaskId);
            }

            task.Status = progressStatus;
            task.Digest = summary;
            task.LatestUpdate = summary;
            task.BlockedReason = progressStatus == TeamTaskStatuses.Blocked ? effectiveBlockedReason : null;
            task.BlockedOnTaskIds = progressStatus == TeamTaskStatuses.Blocked ? resolvedBlockedOnTaskIds : [];
            if (progressStatus == TeamTaskStatuses.Blocked)
            {
                task.QueuedInputId = null;
                ClearCompletionRecovery(task);
            }
            task.UpdatedAt = DateTimeOffset.UtcNow;
            var member = RequireMember(write, task.AssigneeMemberId);
            var missionThread = FindMissionThread(write, task.MissionId, member.MemberId);
            if (missionThread != null)
            {
                missionThread.Status = progressStatus;
                missionThread.CurrentTaskId = task.TaskId;
                missionThread.UpdatedAt = task.UpdatedAt;
            }
            UpsertDigest(write, member.MemberId, summary);
            write.Team.UpdatedAt = task.UpdatedAt;
            return write;
        });
        RefreshContexts(context.WorkspaceCraftPath, state);
        await RunMissionSchedulerAsync(appBindingService, sessionService, context.WorkspacePath, context.WorkspaceCraftPath, missionId, ct);
        return Ok("Progress recorded.", RequireTask(GetStore(context.WorkspaceCraftPath).Snapshot(), taskId));
    }

    [DynamicTool("PublishArtifact", Order = 90)]
    [Description("Publish an artifact reference for a Teams task.")]
    private async Task<DynamicToolCallResult> PublishArtifactToolAsync(
        ManagedAppBindingToolCallContext context,
        CancellationToken ct,
        [Description("Artifact title.")] string title,
        [Description("Artifact path or URI.")] string pathOrUri,
        [Description("Short reusable artifact summary.")] string? summary = null,
        [Description("Optional related task alias or canonical task id when publishing for a specific assigned task.")] string? taskId = null)
    {
        var appBindingService = _appBindingService ?? throw new InvalidOperationException("Teams App Binding service is not available.");
        var sessionService = _sessionService ?? throw new InvalidOperationException("Teams Session service is not available.");
        ArtifactRefRecord artifact = new();
        var missionId = string.Empty;
        var state = GetStore(context.WorkspaceCraftPath).Update(write =>
        {
            var callerThread = RequireMissionCaller(write, context);
            EnsureMissionScopedAliases(write);
            var task = RequireCallerTask(write, callerThread, taskId);
            missionId = task.MissionId;
            EnsureTaskMissionCanReceiveWork(write, task);
            var classification = InferArtifactClassification(pathOrUri);

            artifact = new ArtifactRefRecord
            {
                ArtifactId = $"artifact_{Guid.NewGuid():N}",
                Alias = NextArtifactAlias(write, missionId),
                TaskId = task.TaskId,
                SourceTaskId = task.TaskId,
                MemberId = task.AssigneeMemberId,
                Title = title,
                Uri = pathOrUri,
                Kind = classification.Kind,
                Format = classification.Format,
                Summary = summary,
                Description = summary ?? string.Empty,
                CreatedAt = DateTimeOffset.UtcNow
            };
            write.Artifacts.Add(artifact);
            write.Team.UpdatedAt = artifact.CreatedAt;
            return write;
        });
        RefreshContexts(context.WorkspaceCraftPath, state);
        await RunMissionSchedulerAsync(appBindingService, sessionService, context.WorkspacePath, context.WorkspaceCraftPath, missionId, ct);
        return Ok("Artifact published.", artifact);
    }

    [DynamicTool("MarkTaskDone", Order = 100)]
    [Description("Mark a Teams task complete.")]
    private async Task<DynamicToolCallResult> MarkTaskDoneToolAsync(
        ManagedAppBindingToolCallContext context,
        CancellationToken ct,
        [Description("Completion summary for the current task.")] string summary)
    {
        var appBindingService = _appBindingService ?? throw new InvalidOperationException("Teams App Binding service is not available.");
        var sessionService = _sessionService ?? throw new InvalidOperationException("Teams Session service is not available.");
        var completionSummary = summary;
        var missionId = string.Empty;
        var taskId = string.Empty;
        var state = GetStore(context.WorkspaceCraftPath).Update(write =>
        {
            var callerThread = RequireMissionCaller(write, context);
            var task = RequireCallerTask(write, callerThread);
            taskId = task.TaskId;
            missionId = task.MissionId;
            EnsureTaskMissionCanReceiveWork(write, task);
            task.Status = TeamTaskStatuses.Done;
            task.Digest = completionSummary;
            task.LatestUpdate = completionSummary;
            task.OutputSummary = completionSummary;
            task.BlockedReason = null;
            task.BlockedOnTaskIds = [];
            task.QueuedInputId = null;
            task.LeaderNotifiedAt = null;
            ClearCompletionRecovery(task);
            task.UpdatedAt = DateTimeOffset.UtcNow;
            var member = RequireMember(write, task.AssigneeMemberId);
            var missionThread = FindMissionThread(write, task.MissionId, member.MemberId);
            if (missionThread != null && string.Equals(missionThread.CurrentTaskId, task.TaskId, StringComparison.Ordinal))
            {
                missionThread.CurrentTaskId = null;
                missionThread.UpdatedAt = task.UpdatedAt;
            }
            UpsertDigest(write, member.MemberId, completionSummary);
            RequireMission(write, task.MissionId).LeaderContinuationQueuedInputId = null;

            write.Team.UpdatedAt = DateTimeOffset.UtcNow;
            return write;
        });
        RefreshContexts(context.WorkspaceCraftPath, state);
        await RunMissionSchedulerAsync(appBindingService, sessionService, context.WorkspacePath, context.WorkspaceCraftPath, missionId, ct);
        return Ok("Task marked done.", RequireTask(GetStore(context.WorkspaceCraftPath).Snapshot(), taskId));
    }

    [DynamicTool("MarkMissionDone", Order = 110)]
    [Description("Finalize a Teams mission with the user-facing final response.")]
    private async Task<DynamicToolCallResult> MarkMissionDoneToolAsync(
        ManagedAppBindingToolCallContext context,
        CancellationToken ct,
        [Description("User-facing final response.")] string finalResponse)
    {
        _ = ct;
        var final = finalResponse;
        var completionSummary = Truncate(final, 280);
        var missionId = string.Empty;
        var state = GetStore(context.WorkspaceCraftPath).Update(write =>
        {
            var callerThread = RequireMissionCaller(write, context);
            missionId = callerThread.MissionId;
            var mission = RequireMission(write, missionId);
            EnsureMissionCanReceiveWork(mission);
            if (!string.Equals(callerThread.MemberId, "leader", StringComparison.OrdinalIgnoreCase))
                throw AppServerErrors.InvalidParams($"Mission '{missionId}' can only be marked done by its Leader thread.");
            var missionTasks = write.Tasks.Where(t => string.Equals(t.MissionId, missionId, StringComparison.Ordinal)).ToList();
            var unfinishedTasks = missionTasks.Where(t => IsMissionFinalizationTask(t) && t.Status is not TeamTaskStatuses.Done).ToList();
            if (unfinishedTasks.Count > 0)
                throw AppServerErrors.InvalidParams($"Mission '{missionId}' still has unfinished work: {DescribeTasks(unfinishedTasks)}. Inspect progress with ReadMissionState / ReadMemberStatus, resolve blockers, or wait for teammates to call MarkTaskDone.");

            var now = DateTimeOffset.UtcNow;
            mission.Status = TeamMissionStatuses.Done;
            mission.FinalResponse = final;
            mission.LeaderContinuationQueuedInputId = null;
            CompleteMission(mission, now, completionSummary);
            write.Team.UpdatedAt = now;
            return write;
        });
        RefreshContexts(context.WorkspaceCraftPath, state);
        await TryEnqueueMissionCompletionToOriginAsync(context, missionId, ct);
        var latest = GetStore(context.WorkspaceCraftPath).Snapshot();
        return Ok("Mission marked done.", RequireMission(latest, missionId));
    }

    private async Task TryEnqueueMissionCompletionToOriginAsync(
        ManagedAppBindingToolCallContext context,
        string missionId,
        CancellationToken ct)
    {
        var appBindingService = context.AppBindingService ?? _appBindingService;
        var sessionService = context.SessionService ?? _sessionService;
        if (appBindingService == null || sessionService == null)
            return;

        var state = GetStore(context.WorkspaceCraftPath).Snapshot();
        var mission = state.Missions.FirstOrDefault(item => string.Equals(item.MissionId, missionId, StringComparison.Ordinal));
        if (mission == null
            || string.IsNullOrWhiteSpace(mission.OriginThreadId)
            || string.IsNullOrWhiteSpace(mission.OriginBindingId)
            || !string.IsNullOrWhiteSpace(mission.CompletionQueuedInputId))
        {
            return;
        }

        var input = BuildOriginMissionCompletionInput(state, mission);
        var modelPart = new SessionWireInputPart { Type = "text", Text = input.ModelText };
        var displayPart = new SessionWireInputPart { Type = "text", Text = input.DisplayText };
        var triggerLabel = $"Mission completed: {mission.Title}";
        try
        {
            using (TurnTriggerScope.Set(new TurnTriggerInfo
                   {
                       Kind = "team",
                       Label = triggerLabel,
                       RefId = mission.MissionId
                   }))
            {
                var queued = await sessionService.EnqueueTurnInputAsync(
                    mission.OriginThreadId,
                    [new TextContent(input.ModelText)],
                    sender: null,
                    ct,
                    new SessionInputSnapshot
                    {
                        NativeInputParts = [displayPart],
                        MaterializedInputParts = [modelPart],
                        DisplayText = input.DisplayText
                    });

                appBindingService.RecordThreadInputEnqueued(
                    context.WorkspaceCraftPath,
                    mission.OriginBindingId,
                    queued.Id,
                    "team",
                    triggerLabel,
                    mission.MissionId);

                var now = DateTimeOffset.UtcNow;
                GetStore(context.WorkspaceCraftPath).Update(write =>
                {
                    var current = RequireMission(write, mission.MissionId);
                    current.CompletionQueuedInputId ??= queued.Id;
                    current.CompletionNotifiedAt ??= now;
                    write.Team.UpdatedAt = now;
                    return write;
                });

                await sessionService.TryStartNextQueuedTurnAsync(mission.OriginThreadId, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Mission completion is authoritative even if the origin thread can no longer be notified.
        }
    }

    private void RefreshContexts(string workspaceCraftPath, TeamsStateDocument state)
    {
        var appBindingService = _appBindingService;
        if (appBindingService == null)
            return;
        foreach (var missionThread in state.MissionThreads.Where(thread => thread.ArchivedAt == null))
        {
            var member = state.Members.FirstOrDefault(item => string.Equals(item.MemberId, missionThread.MemberId, StringComparison.Ordinal));
            if (member != null)
                UpsertMissionThreadContextBlocks(appBindingService, workspaceCraftPath, state, missionThread, member);
        }
    }

    private async Task<TeamsTeamViewResult> BuildViewAsync(
        ISessionService sessionService,
        TeamsStateDocument state,
        CancellationToken ct)
    {
        var visibleMissions = state.Missions
            .Where(mission => mission.ArchivedAt == null)
            .OrderByDescending(m => m.CreatedAt)
            .ToList();
        var archivedMissions = state.Missions
            .Where(mission => mission.ArchivedAt != null)
            .OrderByDescending(m => m.ArchivedAt ?? m.UpdatedAt)
            .ToList();
        var visibleMissionIds = visibleMissions
            .Select(mission => mission.MissionId)
            .ToHashSet(StringComparer.Ordinal);
        var visibleTasks = state.Tasks
            .Where(task => visibleMissionIds.Contains(task.MissionId))
            .OrderByDescending(t => t.CreatedAt)
            .ToList();
        var visibleTaskIds = visibleTasks
            .Select(task => task.TaskId)
            .ToHashSet(StringComparer.Ordinal);
        var visibleArtifacts = state.Artifacts
            .Where(artifact => visibleTaskIds.Contains(artifact.TaskId))
            .OrderByDescending(a => a.CreatedAt)
            .ToList();
        var visibleMessages = state.Messages
            .Where(message => visibleMissionIds.Contains(message.MissionId))
            .OrderByDescending(message => message.CreatedAt)
            .ToList();
        var visibleLeaderWaits = new List<TeamLeaderWaitRecord>();
        var visibleMissionThreads = state.MissionThreads
            .Where(thread => thread.ArchivedAt == null && visibleMissionIds.Contains(thread.MissionId))
            .OrderByDescending(thread => thread.CreatedAt)
            .ToList();
        var missionThreadViews = new List<MissionThreadView>();
        var stats = new TeamsTeamStats
        {
            TotalTasks = visibleTasks.Count,
            CompletedTasks = visibleTasks.Count(task => string.Equals(task.Status, "done", StringComparison.Ordinal))
        };
        foreach (var missionThread in visibleMissionThreads)
        {
            var view = CopyMissionThread(missionThread);
            if (missionThread.Status is "cancelled" or "archived")
            {
                missionThreadViews.Add(view);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(missionThread.ThreadId))
            {
                try
                {
                    var thread = await sessionService.GetThreadAsync(missionThread.ThreadId, ct);
                    view.QueuedInputCount = thread.QueuedInputs.Count;
                    view.Running = thread.Turns.Any(turn => turn.Status == TurnStatus.Running);
                    view.WaitingOnApproval = thread.Turns.Any(turn => turn.Status == TurnStatus.WaitingApproval);
                    view.WaitingOnInput = thread.Turns.Any(turn => turn.Status == TurnStatus.WaitingInput);
                    stats.QueuedInputs += view.QueuedInputCount;
                    if (view.Running)
                        stats.RunningMembers++;
                    foreach (var usage in thread.Turns.Select(turn => turn.TokenUsage).OfType<TokenUsageInfo>())
                    {
                        stats.InputTokens += usage.InputTokens;
                        stats.OutputTokens += usage.OutputTokens;
                        stats.CachedInputTokens += usage.CachedInputTokens;
                        stats.TotalTokens += usage.TotalTokens;
                    }

                    view.Status = view.Running
                        ? "running"
                        : view.WaitingOnApproval
                            ? "approval"
                            : view.WaitingOnInput
                                ? "input"
                                : view.QueuedInputCount > 0
                                    ? "queued"
                                    : missionThread.Status;
                }
                catch
                {
                    view.Status = "missing";
                }
            }

            missionThreadViews.Add(view);
        }

        var members = new List<TeamMemberView>();
        foreach (var member in state.Members)
        {
            var view = CopyMember(member);
            var memberThreadViews = missionThreadViews
                .Where(thread => string.Equals(thread.MemberId, member.MemberId, StringComparison.Ordinal))
                .ToList();
            view.QueuedInputCount = memberThreadViews.Sum(thread => thread.QueuedInputCount);
            view.Running = memberThreadViews.Any(thread => thread.Running);
            view.WaitingOnApproval = memberThreadViews.Any(thread => thread.WaitingOnApproval);
            view.WaitingOnInput = memberThreadViews.Any(thread => thread.WaitingOnInput);
            view.CurrentTaskId = memberThreadViews
                .Select(thread => thread.CurrentTaskId)
                .FirstOrDefault(taskId => !string.IsNullOrWhiteSpace(taskId));
            view.Status = view.Running
                ? "running"
                : view.WaitingOnApproval
                    ? "approval"
                    : view.WaitingOnInput
                        ? "input"
                        : memberThreadViews.Any(thread => string.Equals(thread.Status, "running", StringComparison.Ordinal))
                            ? "running"
                        : view.QueuedInputCount > 0 || memberThreadViews.Any(thread => string.Equals(thread.Status, "queued", StringComparison.Ordinal))
                            ? "queued"
                            : member.Status;

            members.Add(view);
        }

        return new TeamsTeamViewResult
        {
            Team = state.Team,
            Stats = stats,
            Members = members,
            Missions = visibleMissions,
            ArchivedMissions = archivedMissions,
            MissionThreads = missionThreadViews,
            Tasks = visibleTasks,
            Messages = visibleMessages,
            LeaderWaits = visibleLeaderWaits,
            MailboxDigests = state.MailboxDigests.OrderByDescending(d => d.UpdatedAt).ToList(),
            Artifacts = visibleArtifacts
        };
    }

    private TeamsStateStore GetStore(string workspaceCraftPath) =>
        _stores.GetOrAdd(Path.GetFullPath(workspaceCraftPath), path => new TeamsStateStore(path));

    private static string ResolveWorkspacePath(string workspaceCraftPath)
    {
        var fullCraftPath = Path.GetFullPath(workspaceCraftPath);
        var directoryName = Path.GetFileName(fullCraftPath);
        return string.Equals(directoryName, ".craft", StringComparison.OrdinalIgnoreCase)
            ? Directory.GetParent(fullCraftPath)?.FullName ?? fullCraftPath
            : fullCraftPath;
    }

    private static string BuildMissionScratchpadPath(string workspaceCraftPath, string missionId) =>
        Path.GetFullPath(Path.Combine(workspaceCraftPath, "teams", "missions", missionId));

    private static void EnsureMissionScratchpad(MissionRecord mission, string workspaceCraftPath)
    {
        if (string.IsNullOrWhiteSpace(mission.MissionId))
            return;

        var scratchpadPath = string.IsNullOrWhiteSpace(mission.ScratchpadPath)
            ? BuildMissionScratchpadPath(workspaceCraftPath, mission.MissionId)
            : mission.ScratchpadPath;
        mission.ScratchpadPath = Path.GetFullPath(Path.IsPathRooted(scratchpadPath)
            ? scratchpadPath
            : Path.Combine(workspaceCraftPath, scratchpadPath));
        Directory.CreateDirectory(mission.ScratchpadPath);
    }

    private static void EnsureMissionScratchpads(TeamsStateDocument state, string workspaceCraftPath)
    {
        foreach (var mission in state.Missions)
            EnsureMissionScratchpad(mission, workspaceCraftPath);
    }

    private IReadOnlyList<DynamicToolSpec> BuildToolSpecsForMember(TeamMemberRecord member)
    {
        var allowed = string.Equals(member.MemberId, "leader", StringComparison.OrdinalIgnoreCase)
            ? LeaderToolNames
            : TeammateToolNames;
        return ToolSpecs
            .Where(tool => allowed.Contains(tool.Name))
            .ToList();
    }

    private static void EnsureDefaultMembers(TeamsStateDocument state)
    {
        foreach (var seed in DefaultMembers())
        {
            if (state.Members.Any(m => string.Equals(m.MemberId, seed.MemberId, StringComparison.Ordinal)))
                continue;
            state.Members.Add(seed);
        }
    }

    private static IReadOnlyList<TeamMemberRecord> DefaultMembers() =>
    [
        new()
        {
            MemberId = "leader",
            Role = "leader",
            DisplayName = "Team Leader",
            Description = "Breaks missions into plans, assigns tasks, and keeps the team synchronized.",
            AvatarAccent = LeaderAvatarAccent,
            DeskX = 50,
            DeskY = 26
        },
        new()
        {
            MemberId = "explorer",
            Role = "explorer",
            DisplayName = "Explorer",
            Description = "Researches, inspects, and maps unknowns before the team commits to a path.",
            AvatarAccent = "#0ea5e9",
            DeskX = 32,
            DeskY = 62
        },
        new()
        {
            MemberId = "builder",
            Role = "builder",
            DisplayName = "Builder",
            Description = "Implements changes, creates artifacts, and reports practical blockers.",
            AvatarAccent = "#8b5cf6",
            DeskX = 52,
            DeskY = 68
        },
        new()
        {
            MemberId = "reviewer",
            Role = "reviewer",
            DisplayName = "Reviewer",
            Description = "Checks correctness, risks, missing tests, and quality before delivery.",
            AvatarAccent = "#22c55e",
            DeskX = 68,
            DeskY = 52
        },
        new()
        {
            MemberId = "operator",
            Role = "operator",
            DisplayName = "Operator",
            Description = "Handles app, computer, and workflow operations that need careful execution.",
            AvatarAccent = "#eab308",
            DeskX = 70,
            DeskY = 28
        }
    ];

    private static string TaskAliasOrId(TeamTaskRecord task) =>
        string.IsNullOrWhiteSpace(task.Alias) ? task.TaskId : task.Alias;

    private static string ArtifactAliasOrId(ArtifactRefRecord artifact) =>
        string.IsNullOrWhiteSpace(artifact.Alias) ? artifact.ArtifactId : artifact.Alias;

    private static string FormatTaskReference(TeamTaskRecord task)
    {
        var alias = TaskAliasOrId(task);
        return string.Equals(alias, task.TaskId, StringComparison.Ordinal) ? task.TaskId : $"{alias} / {task.TaskId}";
    }

    private static string FormatTaskReferences(TeamsStateDocument state, IEnumerable<string> taskIds)
    {
        var references = taskIds
            .Distinct(StringComparer.Ordinal)
            .Select(taskId =>
            {
                var task = state.Tasks.FirstOrDefault(item => string.Equals(item.TaskId, taskId, StringComparison.Ordinal));
                return task == null ? taskId : FormatTaskReference(task);
            })
            .ToList();
        return references.Count == 0 ? "none" : string.Join(", ", references);
    }

    private static string FormatArtifactReferences(TeamsStateDocument state, IEnumerable<string> artifactIds)
    {
        var references = artifactIds
            .Distinct(StringComparer.Ordinal)
            .Select(artifactId =>
            {
                var artifact = state.Artifacts.FirstOrDefault(item => string.Equals(item.ArtifactId, artifactId, StringComparison.Ordinal));
                if (artifact == null)
                    return artifactId;
                var alias = ArtifactAliasOrId(artifact);
                return string.Equals(alias, artifact.ArtifactId, StringComparison.Ordinal) ? artifact.ArtifactId : $"{alias} / {artifact.ArtifactId}";
            })
            .ToList();
        return references.Count == 0 ? string.Empty : string.Join(", ", references);
    }

    private static TeamQueuedInput BuildLeaderMissionInput(MissionRecord mission)
    {
        var modelText =
            $"""
        <team-notification type="mission.created" missionId="{mission.MissionId}">
        Mission created: {mission.Title}

        Scratchpad: {mission.ScratchpadPath ?? "(unavailable)"}

        {mission.Prompt}

        You are the DotCraft Team Leader. Treat the Mission as the user-facing delivery shell and the Tasks as a mission-scoped shared task board. Create a concise mission plan with CreateMissionPlan(plan), then assign concrete tasks to Explorer, Builder, Reviewer, or Operator with AssignTask(assignee, title, prompt).
        Express task ordering with AssignTask dependsOnTaskIds instead of asking teammates to wait in prose. Use short task/artifact aliases such as t1 and a1 in tool parameters instead of copying long canonical ids. Use kind "review" for review-gate tasks when needed. If a downstream teammate needs you to synthesize upstream results before they begin, assign it with requiresLeaderSynthesis true; Teams will wake you to send SendMessage(to, message, taskId) after dependencies finish.
        Teams tools infer the current mission from this thread. Do not copy mission ids into tool calls. After dispatching tasks, end the turn. Do not poll ReadMemberStatus while waiting; Teams will wake you when task results, blockers, teammate messages, synthesis needs, or final review require your attention. Inspect teammate progress with ListTeamMembers, ReadMissionState, and ReadMemberStatus only when you need current state to make a decision. Send follow-up instructions with SendMessage only when a participating teammate needs more direction.
        When all required tasks are complete, Teams will wake you for finalization. Then call MarkMissionDone(finalResponse) for the user-facing result. If the mission can be completed without dispatching tasks, call MarkMissionDone(finalResponse) directly.
        Keep raw team coordination in Teams tools, not in normal thread history.
        </team-notification>
        """;
        var displayText =
            $"""
            Mission created: {mission.Title}

            {Truncate(mission.Prompt, 500)}
            """;
        return new TeamQueuedInput(modelText, displayText);
    }

    private static TeamQueuedInput BuildMemberTaskInput(TeamsStateDocument state, TeamTaskRecord task, TeamMemberRecord member)
    {
        var mission = state.Missions.FirstOrDefault(m => string.Equals(m.MissionId, task.MissionId, StringComparison.Ordinal));
        var messages = GetTaskScopedActionMessages(state, task);
        var relatedTaskIds = task.DependsOnTaskIds.ToHashSet(StringComparer.Ordinal);
        relatedTaskIds.Add(task.TaskId);
        var artifactLines = state.Artifacts
            .Where(artifact => relatedTaskIds.Contains(artifact.TaskId))
            .OrderBy(artifact => artifact.CreatedAt)
            .Select(FormatArtifactLine)
            .ToList();
        var messageLines = messages.Count == 0
            ? "- none"
            : string.Join("\n", messages.Select(message =>
            {
                var sender = state.Members.FirstOrDefault(item => string.Equals(item.MemberId, message.FromMemberId, StringComparison.OrdinalIgnoreCase));
                var artifactLine = message.ArtifactIds.Count == 0 ? string.Empty : $" artifacts={FormatArtifactReferences(state, message.ArtifactIds)}";
                return $"- [{message.Kind}] from {sender?.DisplayName ?? message.FromMemberId}:{artifactLine} {message.Content}";
            }));
        var taskId = TaskAliasOrId(task);
        var modelText =
            $"""
        <team-notification type="task.assigned" missionId="{task.MissionId}" taskId="{task.TaskId}">
        Team task assigned: {task.Title}

        Mission: {mission?.Title ?? task.MissionId}
        Role: {member.DisplayName}
        Scratchpad: {mission?.ScratchpadPath ?? "(unavailable)"}

        Task kind: {task.Kind}
        Task ID: {taskId}
        Canonical task ID: {task.TaskId}
        Dependencies: {FormatTaskReferences(state, task.DependsOnTaskIds)}
        Requires Leader synthesis: {task.RequiresLeaderSynthesis}

        Task brief:
        {task.Prompt}

        Related artifacts:
        {(artifactLines.Count == 0 ? "- none" : string.Join("\n", artifactLines))}

        Task-scoped Leader messages:
        {messageLines}

        Work as this team member. Treat this Mission as the delivery shell and this Task as an item on the shared task board. Teams tools infer the current mission and task from this thread. Use short task/artifact aliases such as t1 and a1 in tool parameters instead of copying long canonical ids. Use ReportProgress(summary, status: "running") while working, PublishArtifact(title, pathOrUri, summary) for important outputs, and MarkTaskDone(summary) when complete. If you are blocked or need Leader input, call ReportProgress(summary, status: "blocked", blockedOnTaskIds) with an actionable summary and use SendMessage(to: "leader", message, taskId) when the Leader needs specific context or a decision request. Do not just reply in prose when the task is finished; call MarkTaskDone.
        </team-notification>
        """;
        var displayText =
            $"""
            Team task assigned: {task.Title}

            Mission: {mission?.Title ?? task.MissionId}
            Task: {taskId}
            Role: {member.DisplayName}
            """;
        return new TeamQueuedInput(modelText, displayText);
    }

    private static TeamQueuedInput BuildMemberCompletionRecoveryInput(TeamsStateDocument state, TeamTaskRecord task, TeamMemberRecord member)
    {
        var mission = state.Missions.FirstOrDefault(m => string.Equals(m.MissionId, task.MissionId, StringComparison.Ordinal));
        var taskId = TaskAliasOrId(task);
        var modelText =
            $"""
        <team-notification type="task.completionRecovery" missionId="{task.MissionId}" taskId="{task.TaskId}">
        Task completion check: {task.Title}

        Mission: {mission?.Title ?? task.MissionId}
        Role: {member.DisplayName}
        Scratchpad: {mission?.ScratchpadPath ?? "(unavailable)"}
        Task ID: {taskId}
        Canonical task ID: {task.TaskId}

        Your previous turn ended while this task was still running. Continue only if more work is needed. If the task is complete, call MarkTaskDone now with a concise summary. If it is not complete because something blocks you, call ReportProgress with status "blocked" and include blockedOnTaskIds when applicable.
        </team-notification>
        """;
        var displayText =
            $"""
            Task completion check: {task.Title}

            Mission: {mission?.Title ?? task.MissionId}
            Task: {taskId}
            """;
        return new TeamQueuedInput(modelText, displayText);
    }

    private static string BuildMissionThreadRoleInstructions(TeamMemberRecord member)
    {
        if (string.Equals(member.MemberId, "leader", StringComparison.OrdinalIgnoreCase))
        {
            return """
            You are the DotCraft Team Leader for the current Mission.

            You are coordinating a Team workflow, not acting as the user's direct chat assistant. The user-facing Mission request is already captured in Teams state and the Mission input; Tasks are the Mission-scoped shared task board. Teams tools infer the current mission from this thread. Break the Mission into a concise plan, dispatch concrete work with AssignTask(assignee, title, prompt), then end the turn while Teams waits for runtime events. Do not run status polling loops. Use short task/artifact aliases such as t1 and a1 in tool parameters instead of copying long canonical ids. Use dependsOnTaskIds for task ordering, kind "review" for review-gate tasks, and requiresLeaderSynthesis true when a downstream teammate should wait for you to synthesize upstream results. Use SendMessage only for mission-scoped follow-up to the Leader or a participating teammate.

            Do not rely on App Context for live teammate status. App Context contains fixed role and Mission binding only. Use Team tools for current state. Do not call MarkMissionDone for Missions that still have unfinished Tasks. Do not repeatedly call ReadMemberStatus as a wait loop. When Teams wakes you for task results, decide whether to assign follow-up work, send a handoff with SendMessage, or end the turn and wait for the next event. When Teams wakes you for synthesis, send an actionable task-scoped SendMessage to the assignee. When Teams wakes you for finalization, call MarkMissionDone(finalResponse) for the user-facing result.
            """;
        }

        return $"""
        You are {member.DisplayName}, a DotCraft Agent Teams teammate in the current Mission.

        You are working inside a mission-scoped teammate thread, not chatting directly with the end user. Treat Team task inputs and Leader messages as internal coordination from the DotCraft Team runtime. Stay in your role: {member.Role}. Profile: {member.Description}

        Work only on the current Mission/Task. Teams tools infer the current mission and task from this thread. Treat the Task as your item on the shared task board: report progress with ReportProgress status "running", publish important outputs with PublishArtifact(title, pathOrUri, summary), and call MarkTaskDone(summary) when your assigned Task is complete. Use short task/artifact aliases such as t1 and a1 in tool parameters instead of copying long canonical ids. If blocked or needing Leader input, call ReportProgress with status "blocked" and blockedOnTaskIds when applicable, and use SendMessage(to: "leader", message, taskId) for specific Leader decisions instead of asking the end user directly. Teams may send a completion-recovery notification if your previous turn ended without MarkTaskDone or ReportProgress(status:"blocked"); in that case, make exactly one of those tool calls before ending.
        """;
    }

    private static string BuildRoleContext(TeamMemberRecord member) =>
        $"""
        You are {member.DisplayName}, a DotCraft Teams member.

        Role: {member.Role}
        Profile: {member.Description}

        Coordinate through explicit Teams tools. Treat Missions and Tasks as app-owned workflow state.
        """;

    private static string ComputeContextBlockVersion(string content) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    private static string BuildMissionContext(
        TeamsStateDocument state,
        MissionThreadRecord missionThread,
        TeamMemberRecord member)
    {
        var mission = state.Missions.FirstOrDefault(m => string.Equals(m.MissionId, missionThread.MissionId, StringComparison.Ordinal));
        return $"""
        Mission ID: {missionThread.MissionId}
        Mission title: {mission?.Title ?? missionThread.MissionId}
        Mission scratchpad: {mission?.ScratchpadPath ?? "(unavailable)"}
        Your teammate role for this mission: {member.DisplayName}

        Mission prompt:
        {mission?.Prompt ?? string.Empty}
        """;
    }

    private static TeamQueuedInput BuildMemberMailboxInput(TeamsStateDocument state, string memberId, IReadOnlyList<TeamMessageRecord> messages)
    {
        var first = messages[0];
        var mission = state.Missions.FirstOrDefault(m => string.Equals(m.MissionId, first.MissionId, StringComparison.Ordinal));
        var member = state.Members.FirstOrDefault(item => string.Equals(item.MemberId, memberId, StringComparison.OrdinalIgnoreCase));
        var messageLines = messages
            .Select(message =>
            {
                var sender = state.Members.FirstOrDefault(item => string.Equals(item.MemberId, message.FromMemberId, StringComparison.OrdinalIgnoreCase));
                var task = string.IsNullOrWhiteSpace(message.TaskId)
                    ? null
                    : state.Tasks.FirstOrDefault(t => string.Equals(t.TaskId, message.TaskId, StringComparison.Ordinal));
                var taskLine = task == null ? string.Empty : $" relatedTask={task.Title} ({FormatTaskReference(task)})";
                var artifactLine = message.ArtifactIds.Count == 0 ? string.Empty : $" artifacts={FormatArtifactReferences(state, message.ArtifactIds)}";
                return $"- [{message.Kind}] from {sender?.DisplayName ?? message.FromMemberId}:{taskLine}{artifactLine} {message.Content}";
            });
        var modelText =
            $"""
        <team-notification type="mailbox.actionable" missionId="{first.MissionId}">
        Team mailbox update for {member?.DisplayName ?? memberId}.

        Mission: {mission?.Title ?? first.MissionId}
        Scratchpad: {mission?.ScratchpadPath ?? "(unavailable)"}

        Actionable messages:
        {string.Join("\n", messageLines)}

        Continue only the work that is actionable now. Use ReadMissionState if you need current task/artifact context. Use SendMessage for Team communication, ReportProgress for status updates, PublishArtifact for important outputs, and MarkTaskDone(summary) when your assigned Task is complete.
        </team-notification>
        """;
        var displayText =
            $"""
            Team mailbox update for {member?.DisplayName ?? memberId}

            Mission: {mission?.Title ?? first.MissionId}
            Messages: {messages.Count}
            """;
        return new TeamQueuedInput(modelText, displayText);
    }

    private static string FormatArtifactLine(ArtifactRefRecord artifact)
    {
        var kind = string.IsNullOrWhiteSpace(artifact.Kind) ? "reference" : artifact.Kind;
        var format = string.IsNullOrWhiteSpace(artifact.Format) ? string.Empty : $" format={artifact.Format}";
        var summary = artifact.Summary ?? artifact.Description;
        var details = string.IsNullOrWhiteSpace(summary) ? string.Empty : $" - {summary}";
        var alias = ArtifactAliasOrId(artifact);
        var idPart = string.Equals(alias, artifact.ArtifactId, StringComparison.Ordinal)
            ? artifact.ArtifactId
            : $"{alias} / {artifact.ArtifactId}";
        return $"- {artifact.Title} ({idPart}, {kind}{format}): {artifact.Uri}{details}".TrimEnd();
    }

    private static TeamQueuedInput BuildOriginMissionCompletionInput(TeamsStateDocument state, MissionRecord mission)
    {
        var taskLines = state.Tasks
            .Where(task => string.Equals(task.MissionId, mission.MissionId, StringComparison.Ordinal))
            .OrderBy(task => task.CreatedAt)
            .Select(task =>
            {
                var latest = string.IsNullOrWhiteSpace(task.OutputSummary)
                    ? string.IsNullOrWhiteSpace(task.LatestUpdate) ? task.Digest : task.LatestUpdate
                    : task.OutputSummary;
                return $"- {task.Title} ({FormatTaskReference(task)}, {task.Kind}, {task.Status}): {latest}";
            })
            .ToList();
        var missionTaskIds = state.Tasks
            .Where(task => string.Equals(task.MissionId, mission.MissionId, StringComparison.Ordinal))
            .Select(task => task.TaskId)
            .ToHashSet(StringComparer.Ordinal);
        var artifactLines = state.Artifacts
            .Where(artifact => missionTaskIds.Contains(artifact.TaskId))
            .OrderBy(artifact => artifact.CreatedAt)
            .Select(FormatArtifactLine)
            .ToList();
        var finalResponse = mission.FinalResponse ?? mission.CompletionSummary ?? string.Empty;
        var modelText =
            $"""
        <team-notification type="mission.completed" missionId="{mission.MissionId}">
        Team mission completed: {mission.Title}

        Status: {mission.Status}

        Final response:
        {finalResponse}

        Task summaries:
        {(taskLines.Count == 0 ? "- none" : string.Join("\n", taskLines))}

        Artifacts:
        {(artifactLines.Count == 0 ? "- none" : string.Join("\n", artifactLines))}
        </team-notification>
        """;
        var displayText =
            $"""
            Team mission completed: {mission.Title}

            {Truncate(finalResponse, 500)}
            """;
        return new TeamQueuedInput(modelText, displayText);
    }

    private static TeamQueuedInput BuildLeaderContinuationInput(
        TeamsStateDocument state,
        MissionRecord mission,
        string reason)
    {
        var tasks = state.Tasks
            .Where(task => string.Equals(task.MissionId, mission.MissionId, StringComparison.Ordinal))
            .OrderBy(task => task.CreatedAt)
            .Select(task =>
            {
                var latest = string.IsNullOrWhiteSpace(task.LatestUpdate) ? task.Digest : task.LatestUpdate;
                var output = string.IsNullOrWhiteSpace(task.OutputSummary) ? string.Empty : $" output={task.OutputSummary}";
                return $"- {task.Title} ({FormatTaskReference(task)}, {task.Kind}, {task.Status}): {latest}{output}";
            })
            .ToList();
        var artifacts = state.Artifacts
            .Where(artifact => state.Tasks.Any(task => string.Equals(task.TaskId, artifact.TaskId, StringComparison.Ordinal)
                                                       && string.Equals(task.MissionId, mission.MissionId, StringComparison.Ordinal)))
            .OrderBy(artifact => artifact.CreatedAt)
            .Select(FormatArtifactLine)
            .ToList();
        var blocked = state.Tasks
            .Where(task => string.Equals(task.MissionId, mission.MissionId, StringComparison.Ordinal)
                           && task.Status == TeamTaskStatuses.Blocked)
            .OrderBy(task => task.UpdatedAt)
            .Select(task => $"- {task.Title} ({FormatTaskReference(task)}): {task.BlockedReason ?? task.Digest}")
            .ToList();
        var synthesis = state.Tasks
            .Where(task => string.Equals(task.MissionId, mission.MissionId, StringComparison.Ordinal)
                           && TaskNeedsLeaderSynthesis(state, task))
            .OrderBy(task => task.CreatedAt)
            .Select(task => $"- {task.Title} ({FormatTaskReference(task)}) assigned to {task.AssigneeMemberId}; dependencies={FormatTaskReferences(state, task.DependsOnTaskIds)}")
            .ToList();
        var newlyDone = state.Tasks
            .Where(task => string.Equals(task.MissionId, mission.MissionId, StringComparison.Ordinal)
                           && task.Status == TeamTaskStatuses.Done
                           && task.LeaderNotifiedAt == null)
            .OrderBy(task => task.UpdatedAt)
            .Select(task =>
            {
                var output = string.IsNullOrWhiteSpace(task.OutputSummary) ? task.Digest : task.OutputSummary;
                return $"- {task.Title} ({FormatTaskReference(task)}) by {task.AssigneeMemberId}: {output}";
            })
            .ToList();

        if (string.Equals(reason, "taskResult", StringComparison.Ordinal))
        {
            var modelText =
                $"""
            <team-notification type="mission.taskResult" missionId="{mission.MissionId}">
            Team task result available: {mission.Title}

            One or more teammate tasks completed while the Mission is still active. Read the mission state if needed, then decide whether to assign follow-up work, send a handoff with SendMessage, or end this turn and wait for the next Teams event. Do not poll teammate status as a wait loop.

            Newly completed tasks:
            {(newlyDone.Count == 0 ? "- none" : string.Join("\n", newlyDone))}

            Task summaries:
            {(tasks.Count == 0 ? "- none" : string.Join("\n", tasks))}

            Artifacts:
            {(artifacts.Count == 0 ? "- none" : string.Join("\n", artifacts))}
            </team-notification>
            """;
            var displayText =
                $"""
                Team task result available: {mission.Title}

                Completed tasks: {newlyDone.Count}
                """;
            return new TeamQueuedInput(modelText, displayText);
        }

        if (string.Equals(reason, "blocked", StringComparison.Ordinal))
        {
            var modelText =
                $"""
            <team-notification type="mission.blocked" missionId="{mission.MissionId}">
            Mission needs Leader coordination: {mission.Title}

            One or more required tasks are blocked. Read the mission state, then either send an actionable message, assign follow-up/revision work, or update the plan. Do not mark the Mission done until required tasks are complete.

            Blocked tasks:
            {(blocked.Count == 0 ? "- none" : string.Join("\n", blocked))}

            Task summaries:
            {(tasks.Count == 0 ? "- none" : string.Join("\n", tasks))}
            </team-notification>
            """;
            var displayText =
                $"""
                Mission needs Leader coordination: {mission.Title}

                Blocked tasks: {blocked.Count}
                """;
            return new TeamQueuedInput(modelText, displayText);
        }

        if (string.Equals(reason, "synthesis", StringComparison.Ordinal))
        {
            var modelText =
                $"""
            <team-notification type="mission.synthesisNeeded" missionId="{mission.MissionId}">
            Mission needs Leader synthesis: {mission.Title}

            One or more downstream tasks have finished dependencies and are waiting for your synthesized handoff. Read the mission state and upstream artifacts, then send SendMessage to each assignee with taskId set to the waiting task. Do not mark the Mission done.

            Waiting synthesis tasks:
            {(synthesis.Count == 0 ? "- none" : string.Join("\n", synthesis))}

            Task summaries:
            {(tasks.Count == 0 ? "- none" : string.Join("\n", tasks))}

            Artifacts:
            {(artifacts.Count == 0 ? "- none" : string.Join("\n", artifacts))}
            </team-notification>
            """;
            var displayText =
                $"""
                Mission needs Leader synthesis: {mission.Title}

                Waiting synthesis tasks: {synthesis.Count}
                """;
            return new TeamQueuedInput(modelText, displayText);
        }

        var finalModelText =
            $"""
        <team-notification type="mission.finalize" missionId="{mission.MissionId}">
        Mission ready for Leader finalization: {mission.Title}

        All required tasks and review gates are complete. Review the mission state, synthesize the final user-facing response, and call MarkMissionDone(finalResponse).

        Task summaries:
        {(tasks.Count == 0 ? "- none" : string.Join("\n", tasks))}

        Artifacts:
        {(artifacts.Count == 0 ? "- none" : string.Join("\n", artifacts))}
        </team-notification>
        """;
        var finalDisplayText =
            $"""
            Mission ready for Leader finalization: {mission.Title}

            All required tasks and review gates are complete.
            """;
        return new TeamQueuedInput(finalModelText, finalDisplayText);
    }

    private static object BuildMissionStateSummary(TeamsStateDocument state, MissionRecord mission)
    {
        var tasks = state.Tasks
            .Where(task => string.Equals(task.MissionId, mission.MissionId, StringComparison.Ordinal))
            .OrderBy(task => task.CreatedAt)
            .Select(task => BuildTaskSummary(state, task))
            .ToList();
        var taskIds = state.Tasks
            .Where(task => string.Equals(task.MissionId, mission.MissionId, StringComparison.Ordinal))
            .Select(task => task.TaskId)
            .ToHashSet(StringComparer.Ordinal);
        var memberIds = state.MissionThreads
            .Where(thread => string.Equals(thread.MissionId, mission.MissionId, StringComparison.Ordinal))
            .Select(thread => thread.MemberId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new
        {
            mission,
            mission.ScratchpadPath,
            missionThreads = state.MissionThreads
                .Where(thread => string.Equals(thread.MissionId, mission.MissionId, StringComparison.Ordinal))
                .OrderBy(thread => thread.CreatedAt)
                .Select(BuildMissionThreadSummary)
                .ToList(),
            members = state.Members
                .Where(member => memberIds.Contains(member.MemberId))
                .Select(member => BuildMemberStatusSummary(state, member, mission.MissionId))
                .ToList(),
            tasks,
            digests = state.MailboxDigests
                .Where(digest => memberIds.Contains(digest.MemberId))
                .OrderByDescending(digest => digest.UpdatedAt)
                .ToList(),
            artifacts = state.Artifacts
                .Where(artifact => taskIds.Contains(artifact.TaskId))
                .OrderByDescending(artifact => artifact.CreatedAt)
                .ToList(),
            messages = state.Messages
                .Where(message => string.Equals(message.MissionId, mission.MissionId, StringComparison.Ordinal))
                .OrderByDescending(message => message.CreatedAt)
                .Take(20)
                .ToList(),
            leaderWaits = Array.Empty<TeamLeaderWaitRecord>()
        };
    }

    private static object BuildMemberStatusSummary(
        TeamsStateDocument state,
        TeamMemberRecord member,
        string? missionId,
        bool includeHistory = false)
    {
        var threads = state.MissionThreads
            .Where(thread => string.Equals(thread.MemberId, member.MemberId, StringComparison.OrdinalIgnoreCase)
                             && thread.ArchivedAt == null
                             && (string.IsNullOrWhiteSpace(missionId) || string.Equals(thread.MissionId, missionId, StringComparison.Ordinal)))
            .OrderByDescending(thread => thread.UpdatedAt)
            .ToList();
        var tasks = state.Tasks
            .Where(task => string.Equals(task.AssigneeMemberId, member.MemberId, StringComparison.OrdinalIgnoreCase)
                           && (string.IsNullOrWhiteSpace(missionId) || string.Equals(task.MissionId, missionId, StringComparison.Ordinal)))
            .OrderByDescending(task => task.UpdatedAt)
            .ToList();
        var digest = state.MailboxDigests.FirstOrDefault(d => string.Equals(d.MemberId, member.MemberId, StringComparison.OrdinalIgnoreCase));
        return new
        {
            member.MemberId,
            member.Role,
            member.DisplayName,
            member.Description,
            member.AvatarAccent,
            status = DeriveMemberStatus(member, threads),
            currentTaskId = threads.FirstOrDefault(thread => !string.IsNullOrWhiteSpace(thread.CurrentTaskId))?.CurrentTaskId
                            ?? member.CurrentTaskId,
            running = threads.Any(thread => string.Equals(thread.Status, "running", StringComparison.Ordinal)),
            queued = threads.Count(thread => string.Equals(thread.Status, "queued", StringComparison.Ordinal)),
            waitingOnApproval = threads.Any(thread => string.Equals(thread.Status, "approval", StringComparison.Ordinal)),
            waitingOnInput = threads.Any(thread => string.Equals(thread.Status, "input", StringComparison.Ordinal)),
            activeTasks = tasks.Where(task => task.Status is not "done" and not "cancelled").Select(task => BuildTaskSummary(state, task)).ToList(),
            completedTasks = includeHistory
                ? tasks.Where(task => task.Status == "done").Take(8).Select(task => BuildTaskSummary(state, task)).ToList()
                : new List<object>(),
            digest = digest?.Content,
            recentMessages = includeHistory
                ? state.Messages
                    .Where(message => string.Equals(message.ToMemberId, member.MemberId, StringComparison.OrdinalIgnoreCase)
                                      || string.Equals(message.FromMemberId, member.MemberId, StringComparison.OrdinalIgnoreCase))
                    .Where(message => string.IsNullOrWhiteSpace(missionId) || string.Equals(message.MissionId, missionId, StringComparison.Ordinal))
                    .OrderByDescending(message => message.CreatedAt)
                    .Take(8)
                    .ToList()
                : new List<TeamMessageRecord>()
        };
    }

    private static object BuildTaskSummary(TeamsStateDocument state, TeamTaskRecord task)
    {
        var member = state.Members.FirstOrDefault(item => string.Equals(item.MemberId, task.AssigneeMemberId, StringComparison.OrdinalIgnoreCase));
        return new
        {
            id = TaskAliasOrId(task),
            alias = task.Alias,
            task.TaskId,
            task.MissionId,
            task.AssigneeMemberId,
            assigneeDisplayName = member?.DisplayName ?? task.AssigneeMemberId,
            task.Title,
            task.Status,
            task.Kind,
            task.RequiredForMission,
            task.RequiresLeaderSynthesis,
            task.DependsOnTaskIds,
            dependsOnTaskAliases = task.DependsOnTaskIds
                .Select(taskId => state.Tasks.FirstOrDefault(item => string.Equals(item.TaskId, taskId, StringComparison.Ordinal)))
                .Where(item => item != null)
                .Select(item => TaskAliasOrId(item!))
                .ToList(),
            task.BlockedOnTaskIds,
            blockedOnTaskAliases = task.BlockedOnTaskIds
                .Select(taskId => state.Tasks.FirstOrDefault(item => string.Equals(item.TaskId, taskId, StringComparison.Ordinal)))
                .Where(item => item != null)
                .Select(item => TaskAliasOrId(item!))
                .ToList(),
            task.BlockedReason,
            task.QueuedInputId,
            task.SynthesisMessageId,
            task.CompletionRecoveryPending,
            task.CompletionRecoveryQueuedInputId,
            task.CompletionRecoveryAttempts,
            task.LeaderNotifiedAt,
            task.Digest,
            task.LatestUpdate,
            task.OutputSummary,
            task.Metadata,
            task.CreatedAt,
            task.UpdatedAt
        };
    }

    private static object BuildMissionThreadSummary(MissionThreadRecord thread) => new
    {
        thread.MissionId,
        thread.MemberId,
        thread.ThreadId,
        thread.Status,
        thread.CurrentTaskId,
        thread.QueuedInputId,
        thread.CreatedAt,
        thread.UpdatedAt
    };

    private async Task RunMissionSchedulerAsync(
        AppBindingService appBindingService,
        ISessionService sessionService,
        string workspacePath,
        string workspaceCraftPath,
        string? missionId,
        CancellationToken ct)
    {
        var schedulerLock = _schedulerLocks.GetOrAdd(Path.GetFullPath(workspaceCraftPath), _ => new SemaphoreSlim(1, 1));
        await schedulerLock.WaitAsync(ct);
        try
        {
            var store = GetStore(workspaceCraftPath);
            var plan = new SchedulerPlan();
            var state = store.Update(write =>
            {
                EnsureMissionScratchpads(write, workspaceCraftPath);
                plan = ReconcileSchedulerState(write, missionId);
                return write;
            });
            RefreshContexts(workspaceCraftPath, state);

            foreach (var dispatch in plan.CompletionRecoveryDispatches)
                await DispatchCompletionRecoveryAsync(appBindingService, sessionService, workspacePath, workspaceCraftPath, dispatch, ct);

            foreach (var dispatch in plan.TaskDispatches)
                await DispatchReadyTaskAsync(appBindingService, sessionService, workspacePath, workspaceCraftPath, dispatch, ct);

            foreach (var dispatch in plan.MessageDispatches)
                await DispatchMailboxMessagesAsync(appBindingService, sessionService, workspacePath, workspaceCraftPath, dispatch, ct);

            foreach (var dispatch in plan.LeaderDispatches)
                await DispatchLeaderFinalizationAsync(appBindingService, sessionService, workspaceCraftPath, dispatch, ct);
        }
        finally
        {
            schedulerLock.Release();
        }
    }

    private SchedulerPlan ReconcileSchedulerState(TeamsStateDocument state, string? missionId)
    {
        NormalizeLegacyState(state);
        var plan = new SchedulerPlan();
        var missions = state.Missions
            .Where(mission => mission.ArchivedAt == null
                              && !IsTerminalMissionStatus(mission.Status)
                              && (string.IsNullOrWhiteSpace(missionId) || string.Equals(mission.MissionId, missionId, StringComparison.Ordinal)))
            .ToList();

        foreach (var mission in missions)
        {
            var missionTasks = state.Tasks
                .Where(task => string.Equals(task.MissionId, mission.MissionId, StringComparison.Ordinal))
                .ToList();

            foreach (var task in missionTasks)
                ReconcileTaskReadiness(state, task);

            var requiredTasks = missionTasks.Where(IsMissionFinalizationTask).ToList();
            var synthesisTasks = missionTasks
                .Where(task => TaskNeedsLeaderSynthesis(state, task))
                .ToList();
            if (synthesisTasks.Count > 0)
                TryAddLeaderDispatch(plan, state, mission, "synthesis");

            var blockedRequiredTasks = requiredTasks
                .Where(task => task.Status == TeamTaskStatuses.Blocked)
                .ToList();
            if (blockedRequiredTasks.Count > 0)
                TryAddLeaderDispatch(plan, state, mission, "blocked");

            if (requiredTasks.Count > 0
                && requiredTasks.All(task => string.Equals(task.Status, TeamTaskStatuses.Done, StringComparison.Ordinal))
                && mission.Status is TeamMissionStatuses.Planning or TeamMissionStatuses.Active)
            {
                mission.Status = TeamMissionStatuses.AwaitingLeaderReview;
                mission.UpdatedAt = DateTimeOffset.UtcNow;
                state.Team.UpdatedAt = mission.UpdatedAt;
            }

            foreach (var task in missionTasks.Where(task =>
                         task.CompletionRecoveryPending
                         && string.Equals(task.Status, TeamTaskStatuses.Ready, StringComparison.Ordinal)
                         && string.IsNullOrWhiteSpace(task.QueuedInputId)
                         && string.IsNullOrWhiteSpace(task.CompletionRecoveryQueuedInputId)))
            {
                if (IsMemberAvailableForDispatch(state, task.AssigneeMemberId))
                    plan.CompletionRecoveryDispatches.Add(new TaskDispatch(task.MissionId, task.TaskId));
            }

            foreach (var task in missionTasks.Where(task =>
                         !task.CompletionRecoveryPending
                         && string.Equals(task.Status, TeamTaskStatuses.Ready, StringComparison.Ordinal)
                         && string.IsNullOrWhiteSpace(task.QueuedInputId)))
            {
                if (IsMemberAvailableForDispatch(state, task.AssigneeMemberId))
                    plan.TaskDispatches.Add(new TaskDispatch(task.MissionId, task.TaskId));
            }

            var actionableMessages = state.Messages
                .Where(message => string.Equals(message.MissionId, mission.MissionId, StringComparison.Ordinal)
                                  && message.RequiresAction
                                  && string.IsNullOrWhiteSpace(message.DeliveredQueuedInputId)
                                  && message.Status is TeamMessageStatuses.Recorded or TeamMessageStatuses.Summarized)
                .GroupBy(message => message.ToMemberId, StringComparer.OrdinalIgnoreCase);
            foreach (var group in actionableMessages)
            {
                if (IsMemberAvailableForDispatch(state, group.Key))
                    plan.MessageDispatches.Add(new MessageDispatch(mission.MissionId, group.Key, group.Select(message => message.MessageId).ToList()));
            }

            if (mission.Status == TeamMissionStatuses.AwaitingLeaderReview
                && string.IsNullOrWhiteSpace(mission.FinalResponse)
                && !plan.LeaderDispatches.Any(dispatch => string.Equals(dispatch.MissionId, mission.MissionId, StringComparison.Ordinal)))
            {
                TryAddLeaderDispatch(plan, state, mission, "finalize");
            }

            if (mission.Status != TeamMissionStatuses.AwaitingLeaderReview
                && missionTasks.Any(task => task.Status == TeamTaskStatuses.Done && task.LeaderNotifiedAt == null)
                && !plan.LeaderDispatches.Any(dispatch => string.Equals(dispatch.MissionId, mission.MissionId, StringComparison.Ordinal)))
            {
                TryAddLeaderDispatch(plan, state, mission, "taskResult");
            }
        }

        return plan;
    }

    private static bool TryAddLeaderDispatch(
        SchedulerPlan plan,
        TeamsStateDocument state,
        MissionRecord mission,
        string reason)
    {
        if (!string.IsNullOrWhiteSpace(mission.LeaderContinuationQueuedInputId)
            || !IsMemberAvailableForDispatch(state, "leader")
            || plan.LeaderDispatches.Any(dispatch => string.Equals(dispatch.MissionId, mission.MissionId, StringComparison.Ordinal)))
        {
            return false;
        }

        plan.LeaderDispatches.Add(new LeaderDispatch(mission.MissionId, reason));
        return true;
    }

    private static void NormalizeLegacyState(TeamsStateDocument state)
    {
        foreach (var member in state.Members)
        {
            if (string.Equals(member.MemberId, "leader", StringComparison.OrdinalIgnoreCase)
                && string.Equals(member.AvatarAccent, LegacyLeaderAvatarAccent, StringComparison.OrdinalIgnoreCase))
            {
                member.AvatarAccent = LeaderAvatarAccent;
            }
        }

        foreach (var mission in state.Missions)
        {
            if (string.Equals(mission.Status, "new", StringComparison.OrdinalIgnoreCase))
                mission.Status = TeamMissionStatuses.Planning;
        }

        foreach (var task in state.Tasks)
        {
            if (string.IsNullOrWhiteSpace(task.Kind))
                task.Kind = "work";
            if (string.IsNullOrWhiteSpace(task.LatestUpdate) && !string.IsNullOrWhiteSpace(task.Digest))
                task.LatestUpdate = task.Digest;
            if (IsLegacyDoneTaskStatus(task.Status))
                task.Status = TeamTaskStatuses.Done;
            else if (string.Equals(task.Status, "queued", StringComparison.OrdinalIgnoreCase))
                task.Status = string.IsNullOrWhiteSpace(task.QueuedInputId) ? TeamTaskStatuses.Pending : TeamTaskStatuses.Running;
            else if (string.IsNullOrWhiteSpace(task.Status))
                task.Status = TeamTaskStatuses.Pending;
        }

        foreach (var message in state.Messages)
        {
            if (string.IsNullOrWhiteSpace(message.Kind))
                message.Kind = TeamMessageKinds.Info;
            if (string.IsNullOrWhiteSpace(message.Status))
                message.Status = TeamMessageStatuses.Recorded;
        }

        foreach (var artifact in state.Artifacts)
        {
            if (string.IsNullOrWhiteSpace(artifact.Kind))
                artifact.Kind = "reference";
            if (string.IsNullOrWhiteSpace(artifact.SourceTaskId))
                artifact.SourceTaskId = artifact.TaskId;
        }

        foreach (var wait in state.LeaderWaits)
        {
            if (string.IsNullOrWhiteSpace(wait.Condition))
                wait.Condition = TeamLeaderWaitConditions.MissionReady;
            if (string.IsNullOrWhiteSpace(wait.Status))
                wait.Status = TeamLeaderWaitStatuses.Cancelled;
            else if (wait.Status is TeamLeaderWaitStatuses.Active or TeamLeaderWaitStatuses.Satisfied)
                wait.Status = TeamLeaderWaitStatuses.Cancelled;
        }

        EnsureMissionScopedAliases(state);
    }

    private static void EnsureMissionScopedAliases(TeamsStateDocument state)
    {
        foreach (var mission in state.Missions)
        {
            var orderedTasks = state.Tasks
                .Where(task => string.Equals(task.MissionId, mission.MissionId, StringComparison.Ordinal))
                .OrderBy(task => task.CreatedAt)
                .ThenBy(task => task.TaskId, StringComparer.Ordinal)
                .ToList();
            var preservedTaskAliases = orderedTasks
                .Where(task => IsValidTaskAlias(task.Alias))
                .GroupBy(task => task.Alias, StringComparer.Ordinal)
                .Where(group => group.Count() == 1)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.Ordinal);
            var usedTaskAliases = new HashSet<string>(preservedTaskAliases, StringComparer.Ordinal);
            var nextTaskIndex = 1;
            foreach (var task in orderedTasks)
            {
                if (!preservedTaskAliases.Contains(task.Alias))
                    task.Alias = TakeNextAlias("t", usedTaskAliases, ref nextTaskIndex);
            }

            var missionTaskIds = orderedTasks
                .Select(task => task.TaskId)
                .ToHashSet(StringComparer.Ordinal);
            var orderedArtifacts = state.Artifacts
                .Where(artifact => missionTaskIds.Contains(artifact.TaskId))
                .OrderBy(artifact => artifact.CreatedAt)
                .ThenBy(artifact => artifact.ArtifactId, StringComparer.Ordinal)
                .ToList();
            var preservedArtifactAliases = orderedArtifacts
                .Where(artifact => IsValidArtifactAlias(artifact.Alias))
                .GroupBy(artifact => artifact.Alias, StringComparer.Ordinal)
                .Where(group => group.Count() == 1)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.Ordinal);
            var usedArtifactAliases = new HashSet<string>(preservedArtifactAliases, StringComparer.Ordinal);
            var nextArtifactIndex = 1;
            foreach (var artifact in orderedArtifacts)
            {
                if (!preservedArtifactAliases.Contains(artifact.Alias))
                    artifact.Alias = TakeNextAlias("a", usedArtifactAliases, ref nextArtifactIndex);
            }
        }
    }

    private static string TakeNextAlias(string prefix, HashSet<string> usedAliases, ref int nextIndex)
    {
        string alias;
        do
        {
            alias = $"{prefix}{nextIndex++}";
        }
        while (usedAliases.Contains(alias));

        usedAliases.Add(alias);
        return alias;
    }

    private static string NextTaskAlias(TeamsStateDocument state, string missionId)
    {
        var usedAliases = state.Tasks
            .Where(task => string.Equals(task.MissionId, missionId, StringComparison.Ordinal) && IsValidTaskAlias(task.Alias))
            .Select(task => task.Alias)
            .ToHashSet(StringComparer.Ordinal);
        var nextIndex = 1;
        return TakeNextAlias("t", usedAliases, ref nextIndex);
    }

    private static string NextArtifactAlias(TeamsStateDocument state, string missionId)
    {
        var missionTaskIds = state.Tasks
            .Where(task => string.Equals(task.MissionId, missionId, StringComparison.Ordinal))
            .Select(task => task.TaskId)
            .ToHashSet(StringComparer.Ordinal);
        var usedAliases = state.Artifacts
            .Where(artifact => missionTaskIds.Contains(artifact.TaskId) && IsValidArtifactAlias(artifact.Alias))
            .Select(artifact => artifact.Alias)
            .ToHashSet(StringComparer.Ordinal);
        var nextIndex = 1;
        return TakeNextAlias("a", usedAliases, ref nextIndex);
    }

    private static bool IsValidTaskAlias(string? alias) =>
        !string.IsNullOrWhiteSpace(alias) && TaskAliasPattern.IsMatch(alias);

    private static bool IsValidArtifactAlias(string? alias) =>
        !string.IsNullOrWhiteSpace(alias) && ArtifactAliasPattern.IsMatch(alias);

    private static void ReconcileTaskReadiness(TeamsStateDocument state, TeamTaskRecord task)
    {
        if (task.Status is TeamTaskStatuses.Done or TeamTaskStatuses.Failed or TeamTaskStatuses.Cancelled)
            return;
        if (!string.IsNullOrWhiteSpace(task.QueuedInputId) || task.Status == TeamTaskStatuses.Running)
            return;
        if (task.CompletionRecoveryPending && string.IsNullOrWhiteSpace(task.CompletionRecoveryQueuedInputId))
        {
            task.Status = TeamTaskStatuses.Ready;
            task.UpdatedAt = DateTimeOffset.UtcNow;
            return;
        }

        var unresolvedDependencies = UnresolvedDependencyIds(state, task);
        if (unresolvedDependencies.Count > 0)
        {
            task.Status = TeamTaskStatuses.WaitingDependencies;
            task.UpdatedAt = DateTimeOffset.UtcNow;
            return;
        }

        if (task.RequiresLeaderSynthesis && string.IsNullOrWhiteSpace(task.SynthesisMessageId))
        {
            task.Status = TeamTaskStatuses.WaitingDependencies;
            task.UpdatedAt = DateTimeOffset.UtcNow;
            return;
        }

        if (task.Status == TeamTaskStatuses.Blocked
            && task.BlockedOnTaskIds.Distinct(StringComparer.Ordinal).Any(dependencyId => !IsDoneTask(state, dependencyId)))
        {
            return;
        }

        if (task.Status == TeamTaskStatuses.Blocked && task.BlockedOnTaskIds.Count == 0 && !string.IsNullOrWhiteSpace(task.BlockedReason))
            return;

        if (task.Status == TeamTaskStatuses.Blocked)
        {
            task.BlockedReason = null;
            task.BlockedOnTaskIds = [];
        }

        if (task.Status is TeamTaskStatuses.Pending or TeamTaskStatuses.WaitingDependencies or TeamTaskStatuses.Ready)
        {
            task.Status = TeamTaskStatuses.Ready;
            task.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    private static List<TeamMessageRecord> GetTaskScopedActionMessages(TeamsStateDocument state, TeamTaskRecord task) =>
        state.Messages
            .Where(message => string.Equals(message.MissionId, task.MissionId, StringComparison.Ordinal)
                              && string.Equals(message.TaskId, task.TaskId, StringComparison.Ordinal)
                              && string.Equals(message.ToMemberId, task.AssigneeMemberId, StringComparison.OrdinalIgnoreCase)
                              && message.RequiresAction
                              && string.IsNullOrWhiteSpace(message.DeliveredQueuedInputId)
                              && message.Status is TeamMessageStatuses.Recorded or TeamMessageStatuses.Summarized)
            .OrderBy(message => message.CreatedAt)
            .ToList();

    private async Task DispatchReadyTaskAsync(
        AppBindingService appBindingService,
        ISessionService sessionService,
        string workspacePath,
        string workspaceCraftPath,
        TaskDispatch dispatch,
        CancellationToken ct)
    {
        var snapshot = GetStore(workspaceCraftPath).Snapshot();
        var task = snapshot.Tasks.FirstOrDefault(item => string.Equals(item.TaskId, dispatch.TaskId, StringComparison.Ordinal));
        if (task == null
            || !string.Equals(task.Status, TeamTaskStatuses.Ready, StringComparison.Ordinal)
            || !string.IsNullOrWhiteSpace(task.QueuedInputId)
            || !IsMemberAvailableForDispatch(snapshot, task.AssigneeMemberId))
        {
            return;
        }

        var member = RequireMember(snapshot, task.AssigneeMemberId);
        var missionThread = await EnsureMissionMemberThreadAsync(
            appBindingService,
            sessionService,
            workspacePath,
            workspaceCraftPath,
            snapshot,
            task.MissionId,
            member,
            ct);

        snapshot = GetStore(workspaceCraftPath).Snapshot();
        task = RequireTask(snapshot, dispatch.TaskId);
        if (!string.Equals(task.Status, TeamTaskStatuses.Ready, StringComparison.Ordinal)
            || !string.IsNullOrWhiteSpace(task.QueuedInputId)
            || !IsMemberAvailableForDispatch(snapshot, task.AssigneeMemberId))
        {
            return;
        }

        var queued = await EnqueueForMissionThreadAsync(
            appBindingService,
            sessionService,
            workspaceCraftPath,
            missionThread,
            BuildMemberTaskInput(snapshot, task, member),
            triggerLabel: $"Task: {task.Title}",
            triggerRefId: task.TaskId,
            ct);

        var state = GetStore(workspaceCraftPath).Update(write =>
        {
            var current = RequireTask(write, task.TaskId);
            current.QueuedInputId = queued.Id;
            current.CompletionRecoveryQueuedInputId = null;
            if (current.Status == TeamTaskStatuses.Ready)
                current.Status = TeamTaskStatuses.Running;
            current.UpdatedAt = DateTimeOffset.UtcNow;
            var now = current.UpdatedAt;
            foreach (var message in GetTaskScopedActionMessages(write, current))
            {
                message.DeliveredQueuedInputId = queued.Id;
                message.DeliveredAt = now;
                message.Status = TeamMessageStatuses.DeliveredToTurn;
            }

            write.Team.UpdatedAt = current.UpdatedAt;
            return write;
        });
        RefreshContexts(workspaceCraftPath, state);
    }

    private async Task DispatchCompletionRecoveryAsync(
        AppBindingService appBindingService,
        ISessionService sessionService,
        string workspacePath,
        string workspaceCraftPath,
        TaskDispatch dispatch,
        CancellationToken ct)
    {
        var snapshot = GetStore(workspaceCraftPath).Snapshot();
        var task = snapshot.Tasks.FirstOrDefault(item => string.Equals(item.TaskId, dispatch.TaskId, StringComparison.Ordinal));
        if (task == null
            || !task.CompletionRecoveryPending
            || !string.Equals(task.Status, TeamTaskStatuses.Ready, StringComparison.Ordinal)
            || !string.IsNullOrWhiteSpace(task.QueuedInputId)
            || !string.IsNullOrWhiteSpace(task.CompletionRecoveryQueuedInputId)
            || !IsMemberAvailableForDispatch(snapshot, task.AssigneeMemberId))
        {
            return;
        }

        var member = RequireMember(snapshot, task.AssigneeMemberId);
        var missionThread = await EnsureMissionMemberThreadAsync(
            appBindingService,
            sessionService,
            workspacePath,
            workspaceCraftPath,
            snapshot,
            task.MissionId,
            member,
            ct);

        snapshot = GetStore(workspaceCraftPath).Snapshot();
        task = RequireTask(snapshot, dispatch.TaskId);
        if (!task.CompletionRecoveryPending
            || !string.Equals(task.Status, TeamTaskStatuses.Ready, StringComparison.Ordinal)
            || !string.IsNullOrWhiteSpace(task.QueuedInputId)
            || !string.IsNullOrWhiteSpace(task.CompletionRecoveryQueuedInputId)
            || !IsMemberAvailableForDispatch(snapshot, task.AssigneeMemberId))
        {
            return;
        }

        var queued = await EnqueueForMissionThreadAsync(
            appBindingService,
            sessionService,
            workspaceCraftPath,
            missionThread,
            BuildMemberCompletionRecoveryInput(snapshot, task, member),
            triggerLabel: $"Task completion check: {task.Title}",
            triggerRefId: task.TaskId,
            ct);

        var state = GetStore(workspaceCraftPath).Update(write =>
        {
            var current = RequireTask(write, task.TaskId);
            if (!current.CompletionRecoveryPending || !string.IsNullOrWhiteSpace(current.CompletionRecoveryQueuedInputId))
                return write;

            current.QueuedInputId = queued.Id;
            current.CompletionRecoveryQueuedInputId = queued.Id;
            current.Status = TeamTaskStatuses.Running;
            current.UpdatedAt = DateTimeOffset.UtcNow;
            write.Team.UpdatedAt = current.UpdatedAt;
            return write;
        });
        RefreshContexts(workspaceCraftPath, state);
    }

    private async Task DispatchMailboxMessagesAsync(
        AppBindingService appBindingService,
        ISessionService sessionService,
        string workspacePath,
        string workspaceCraftPath,
        MessageDispatch dispatch,
        CancellationToken ct)
    {
        var snapshot = GetStore(workspaceCraftPath).Snapshot();
        if (!IsMemberAvailableForDispatch(snapshot, dispatch.MemberId))
            return;

        var messages = snapshot.Messages
            .Where(message => dispatch.MessageIds.Contains(message.MessageId, StringComparer.Ordinal)
                              && message.RequiresAction
                              && string.IsNullOrWhiteSpace(message.DeliveredQueuedInputId))
            .OrderBy(message => message.CreatedAt)
            .ToList();
        if (messages.Count == 0)
            return;

        var missionThread = FindMissionThread(snapshot, dispatch.MissionId, dispatch.MemberId);
        if (missionThread == null || string.IsNullOrWhiteSpace(missionThread.ThreadId) || missionThread.ArchivedAt != null)
        {
            var member = snapshot.Members.FirstOrDefault(item => string.Equals(item.MemberId, dispatch.MemberId, StringComparison.OrdinalIgnoreCase));
            if (member == null)
                return;
            missionThread = await EnsureMissionMemberThreadAsync(
                appBindingService,
                sessionService,
                workspacePath,
                workspaceCraftPath,
                snapshot,
                dispatch.MissionId,
                member,
                ct);
            snapshot = GetStore(workspaceCraftPath).Snapshot();
            if (!IsMemberAvailableForDispatch(snapshot, dispatch.MemberId))
                return;
        }

        var queued = await EnqueueForMissionThreadAsync(
            appBindingService,
            sessionService,
            workspaceCraftPath,
            missionThread,
            BuildMemberMailboxInput(snapshot, dispatch.MemberId, messages),
            triggerLabel: $"Team messages: {RequireMember(snapshot, dispatch.MemberId).DisplayName}",
            triggerRefId: messages[0].MessageId,
            ct);

        var state = GetStore(workspaceCraftPath).Update(write =>
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var messageId in dispatch.MessageIds)
            {
                var message = write.Messages.FirstOrDefault(item => string.Equals(item.MessageId, messageId, StringComparison.Ordinal));
                if (message == null || !string.IsNullOrWhiteSpace(message.DeliveredQueuedInputId))
                    continue;
                message.DeliveredQueuedInputId = queued.Id;
                message.DeliveredAt = now;
                message.Status = TeamMessageStatuses.DeliveredToTurn;
            }

            write.Team.UpdatedAt = now;
            return write;
        });
        RefreshContexts(workspaceCraftPath, state);
    }

    private async Task DispatchLeaderFinalizationAsync(
        AppBindingService appBindingService,
        ISessionService sessionService,
        string workspaceCraftPath,
        LeaderDispatch dispatch,
        CancellationToken ct)
    {
        var snapshot = GetStore(workspaceCraftPath).Snapshot();
        var mission = snapshot.Missions.FirstOrDefault(item => string.Equals(item.MissionId, dispatch.MissionId, StringComparison.Ordinal));
        var hasBlockedRequiredTasks = snapshot.Tasks.Any(task => string.Equals(task.MissionId, dispatch.MissionId, StringComparison.Ordinal)
                                                                  && task.RequiredForMission
                                                                  && task.Status == TeamTaskStatuses.Blocked);
        var isFinalization = string.Equals(dispatch.Reason, "finalize", StringComparison.Ordinal);
        var isSynthesis = string.Equals(dispatch.Reason, "synthesis", StringComparison.Ordinal);
        var isTaskResult = string.Equals(dispatch.Reason, "taskResult", StringComparison.Ordinal);
        var hasUnnotifiedDoneTasks = snapshot.Tasks.Any(task => string.Equals(task.MissionId, dispatch.MissionId, StringComparison.Ordinal)
                                                                && task.Status == TeamTaskStatuses.Done
                                                                && task.LeaderNotifiedAt == null);
        var hasSynthesisTasks = snapshot.Tasks.Any(task => string.Equals(task.MissionId, dispatch.MissionId, StringComparison.Ordinal)
                                                           && TaskNeedsLeaderSynthesis(snapshot, task));
        if (mission == null
            || (isFinalization
                ? mission.Status != TeamMissionStatuses.AwaitingLeaderReview
                : isSynthesis
                    ? !hasSynthesisTasks || IsTerminalMissionStatus(mission.Status)
                    : isTaskResult
                        ? !hasUnnotifiedDoneTasks
                          || mission.Status == TeamMissionStatuses.AwaitingLeaderReview
                          || IsTerminalMissionStatus(mission.Status)
                        : !hasBlockedRequiredTasks || IsTerminalMissionStatus(mission.Status))
            || !string.IsNullOrWhiteSpace(mission.FinalResponse)
            || !string.IsNullOrWhiteSpace(mission.LeaderContinuationQueuedInputId)
            || !IsMemberAvailableForDispatch(snapshot, "leader"))
        {
            return;
        }

        var leaderThread = FindMissionThread(snapshot, mission.MissionId, "leader");
        if (leaderThread == null || string.IsNullOrWhiteSpace(leaderThread.ThreadId) || leaderThread.ArchivedAt != null)
            return;

        var queued = await EnqueueForMissionThreadAsync(
            appBindingService,
            sessionService,
            workspaceCraftPath,
            leaderThread,
            BuildLeaderContinuationInput(snapshot, mission, dispatch.Reason),
            triggerLabel: isFinalization
                ? $"Finalize mission: {mission.Title}"
                : isSynthesis
                    ? $"Synthesize task handoff: {mission.Title}"
                    : isTaskResult
                        ? $"Team task result: {mission.Title}"
                        : $"Mission needs Leader: {mission.Title}",
            triggerRefId: mission.MissionId,
            ct);

        var state = GetStore(workspaceCraftPath).Update(write =>
        {
            var current = RequireMission(write, mission.MissionId);
            var now = DateTimeOffset.UtcNow;
            if (!IsTerminalMissionStatus(current.Status) && string.IsNullOrWhiteSpace(current.LeaderContinuationQueuedInputId))
            {
                current.LeaderContinuationQueuedInputId = queued.Id;
                current.UpdatedAt = now;
                write.Team.UpdatedAt = current.UpdatedAt;
            }

            if (isTaskResult || isSynthesis || isFinalization || hasBlockedRequiredTasks)
            {
                foreach (var task in write.Tasks.Where(task => string.Equals(task.MissionId, mission.MissionId, StringComparison.Ordinal)
                                                                && task.Status == TeamTaskStatuses.Done
                                                                && task.LeaderNotifiedAt == null))
                {
                    task.LeaderNotifiedAt = now;
                    task.UpdatedAt = now;
                }
            }

            return write;
        });
        RefreshContexts(workspaceCraftPath, state);
    }

    private static bool IsMemberAvailableForDispatch(TeamsStateDocument state, string memberId) =>
        state.MissionThreads
            .Where(thread => string.Equals(thread.MemberId, memberId, StringComparison.OrdinalIgnoreCase))
            .All(thread => thread.ArchivedAt != null
                           || thread.Status is not ("running" or "queued" or "approval" or "input")
                           && string.IsNullOrWhiteSpace(thread.QueuedInputId));

    private sealed class SchedulerPlan
    {
        public List<TaskDispatch> CompletionRecoveryDispatches { get; } = [];

        public List<TaskDispatch> TaskDispatches { get; } = [];

        public List<MessageDispatch> MessageDispatches { get; } = [];

        public List<LeaderDispatch> LeaderDispatches { get; } = [];
    }

    private sealed record TaskDispatch(string MissionId, string TaskId);

    private sealed record MessageDispatch(string MissionId, string MemberId, List<string> MessageIds);

    private sealed record LeaderDispatch(string MissionId, string Reason);

    private sealed record TeamQueuedInput(string ModelText, string DisplayText);

    private async Task TryStartNextForMemberAsync(
        ISessionService sessionService,
        string workspaceCraftPath,
        string memberId,
        CancellationToken ct)
    {
        var store = GetStore(workspaceCraftPath);
        var snapshot = store.Snapshot();
        if (snapshot.MissionThreads
            .Where(thread => string.Equals(thread.MemberId, memberId, StringComparison.Ordinal))
            .Any(thread => thread.ArchivedAt == null && IsBusyMissionThreadStatus(thread.Status)))
        {
            return;
        }

        var next = snapshot.MissionThreads
            .Where(thread => string.Equals(thread.MemberId, memberId, StringComparison.Ordinal)
                             && thread.ArchivedAt == null
                             && string.Equals(thread.Status, "queued", StringComparison.Ordinal))
            .OrderBy(thread => thread.UpdatedAt)
            .ThenBy(thread => thread.CreatedAt)
            .FirstOrDefault();
        if (next == null || string.IsNullOrWhiteSpace(next.ThreadId))
            return;

        QueuedTurnInput? queued = null;
        try
        {
            var thread = await sessionService.GetThreadAsync(next.ThreadId, ct);
            queued = thread.QueuedInputs
                .Where(input => string.Equals(input.Status, "queued", StringComparison.Ordinal))
                .OrderBy(input => input.CreatedAt)
                .FirstOrDefault();
            if (queued == null)
                return;
        }
        catch
        {
            return;
        }

        var state = store.Update(write =>
        {
            var current = RequireMissionThread(write, next.MissionId, next.MemberId);
            current.Status = "running";
            current.QueuedInputId = null;
            current.UpdatedAt = DateTimeOffset.UtcNow;
            if (string.Equals(current.MemberId, "leader", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(queued.TriggerRefId))
            {
                var mission = write.Missions.FirstOrDefault(item => string.Equals(item.MissionId, queued.TriggerRefId, StringComparison.Ordinal));
                if (mission != null && string.Equals(mission.LeaderContinuationQueuedInputId, queued.Id, StringComparison.Ordinal))
                    mission.LeaderContinuationQueuedInputId = null;
            }

            if (!string.IsNullOrWhiteSpace(queued.TriggerRefId)
                && write.Tasks.Any(task => string.Equals(task.TaskId, queued.TriggerRefId, StringComparison.Ordinal)))
            {
                current.CurrentTaskId = queued.TriggerRefId;
                var task = RequireTask(write, queued.TriggerRefId);
                if (task.Status is TeamTaskStatuses.Pending or TeamTaskStatuses.WaitingDependencies or TeamTaskStatuses.Ready or "queued")
                {
                    task.Status = TeamTaskStatuses.Running;
                    task.UpdatedAt = current.UpdatedAt;
                }
            }

            write.Team.UpdatedAt = current.UpdatedAt;
            return write;
        });
        RefreshContexts(workspaceCraftPath, state);
        await sessionService.TryStartNextQueuedTurnAsync(next.ThreadId, ct);
    }

    private async Task MarkMissionThreadIdleOrQueuedAsync(
        ISessionService sessionService,
        string workspaceCraftPath,
        string threadId,
        SessionThreadRuntimeSignal signal,
        CancellationToken ct)
    {
        var store = GetStore(workspaceCraftPath);
        QueuedTurnInput? nextQueued = null;
        try
        {
            var thread = await sessionService.GetThreadAsync(threadId, ct);
            nextQueued = thread.QueuedInputs
                .Where(input => string.Equals(input.Status, "queued", StringComparison.Ordinal))
                .OrderBy(input => input.CreatedAt)
                .FirstOrDefault();
        }
        catch
        {
        }

        var state = store.Update(write =>
        {
            var missionThread = write.MissionThreads.FirstOrDefault(t => string.Equals(t.ThreadId, threadId, StringComparison.Ordinal));
            if (missionThread == null || missionThread.ArchivedAt != null || missionThread.Status is "cancelled" or "archived")
                return write;

            var previousTaskId = missionThread.CurrentTaskId;
            missionThread.Status = nextQueued == null ? "idle" : "queued";
            missionThread.QueuedInputId = nextQueued?.Id;
            missionThread.CurrentTaskId = nextQueued?.TriggerRefId != null
                                          && write.Tasks.Any(task => string.Equals(task.TaskId, nextQueued.TriggerRefId, StringComparison.Ordinal))
                ? nextQueued.TriggerRefId
                : null;
            missionThread.UpdatedAt = DateTimeOffset.UtcNow;
            if (nextQueued == null
                && string.Equals(missionThread.MemberId, "leader", StringComparison.OrdinalIgnoreCase)
                && signal is SessionThreadRuntimeSignal.TurnCompleted
                    or SessionThreadRuntimeSignal.TurnCompletedAwaitingPlanConfirmation
                    or SessionThreadRuntimeSignal.TurnFailed
                    or SessionThreadRuntimeSignal.TurnCancelled)
            {
                var mission = write.Missions.FirstOrDefault(item => string.Equals(item.MissionId, missionThread.MissionId, StringComparison.Ordinal));
                if (mission != null)
                    mission.LeaderContinuationQueuedInputId = null;
            }

            if (nextQueued == null
                && signal is SessionThreadRuntimeSignal.TurnCompleted
                    or SessionThreadRuntimeSignal.TurnCompletedAwaitingPlanConfirmation
                    or SessionThreadRuntimeSignal.TurnFailed
                    or SessionThreadRuntimeSignal.TurnCancelled
                && !string.IsNullOrWhiteSpace(previousTaskId))
            {
                var task = write.Tasks.FirstOrDefault(item => string.Equals(item.TaskId, previousTaskId, StringComparison.Ordinal));
                if (task != null && task.Status == TeamTaskStatuses.Running)
                {
                    task.QueuedInputId = null;
                    task.CompletionRecoveryQueuedInputId = null;
                    if (signal is SessionThreadRuntimeSignal.TurnCompleted
                            or SessionThreadRuntimeSignal.TurnCompletedAwaitingPlanConfirmation
                        && !task.CompletionRecoveryPending
                        && task.CompletionRecoveryAttempts == 0)
                    {
                        task.Status = TeamTaskStatuses.Ready;
                        task.CompletionRecoveryPending = true;
                        task.CompletionRecoveryAttempts = 1;
                        task.BlockedReason = null;
                        task.BlockedOnTaskIds = [];
                        task.Digest = "The teammate turn ended before the task was marked done; Teams will ask the teammate to either finish or report a blocker.";
                        task.UpdatedAt = missionThread.UpdatedAt;
                        UpsertDigest(write, task.AssigneeMemberId, task.Digest);
                    }
                    else
                    {
                        task.Status = TeamTaskStatuses.Blocked;
                        task.BlockedReason = task.CompletionRecoveryPending
                            ? "The teammate did not call MarkTaskDone or ReportProgress(status:\"blocked\") after the completion recovery prompt."
                            : $"The teammate turn ended with runtime signal '{signal}' before the task was marked done.";
                        task.BlockedOnTaskIds = [];
                        task.CompletionRecoveryPending = false;
                        task.CompletionRecoveryAttempts = 0;
                        task.Digest = task.BlockedReason;
                        task.UpdatedAt = missionThread.UpdatedAt;
                        UpsertDigest(write, task.AssigneeMemberId, task.BlockedReason);
                    }
                }
            }

            write.Team.UpdatedAt = missionThread.UpdatedAt;
            return write;
        });
        RefreshContexts(workspaceCraftPath, state);
    }

    private async Task StopMissionThreadsAsync(
        ISessionService sessionService,
        IEnumerable<MissionThreadRecord> missionThreads,
        CancellationToken ct)
    {
        foreach (var missionThread in missionThreads)
        {
            if (string.IsNullOrWhiteSpace(missionThread.ThreadId))
                continue;

            SessionThread thread;
            try
            {
                thread = await sessionService.GetThreadAsync(missionThread.ThreadId, ct);
            }
            catch
            {
                continue;
            }

            foreach (var turn in thread.Turns.Where(turn => turn.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput).ToList())
                await sessionService.CancelTurnAsync(thread.Id, turn.Id, ct);

            foreach (var queued in thread.QueuedInputs.ToList())
                await sessionService.RemoveQueuedTurnInputAsync(thread.Id, queued.Id, ct);
        }
    }

    private void UpdateMissionThreadRuntimeState(string workspaceCraftPath, string threadId, string status)
    {
        GetStore(workspaceCraftPath).Update(write =>
        {
            var missionThread = write.MissionThreads.FirstOrDefault(t => string.Equals(t.ThreadId, threadId, StringComparison.Ordinal));
            if (missionThread == null || missionThread.ArchivedAt != null)
                return write;
            missionThread.Status = status;
            missionThread.UpdatedAt = DateTimeOffset.UtcNow;
            write.Team.UpdatedAt = missionThread.UpdatedAt;
            return write;
        });
    }

    private static void UpsertDigest(TeamsStateDocument state, string memberId, string content)
    {
        var digest = state.MailboxDigests.FirstOrDefault(d => string.Equals(d.MemberId, memberId, StringComparison.Ordinal));
        if (digest == null)
        {
            digest = new MailboxDigestRecord
            {
                DigestId = $"digest_{memberId}",
                MemberId = memberId
            };
            state.MailboxDigests.Add(digest);
        }

        digest.Content = content;
        digest.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void ClearCompletionRecovery(TeamTaskRecord task)
    {
        task.CompletionRecoveryPending = false;
        task.CompletionRecoveryQueuedInputId = null;
        task.CompletionRecoveryAttempts = 0;
    }

    private static MissionThreadRecord RequireTaskAssigneeCaller(
        TeamsStateDocument state,
        TeamTaskRecord task,
        ManagedAppBindingToolCallContext context)
    {
        var caller = state.MissionThreads.FirstOrDefault(thread =>
            string.Equals(thread.ThreadId, context.ThreadId, StringComparison.Ordinal)
            && thread.ArchivedAt == null);
        if (caller == null)
            throw AppServerErrors.InvalidParams($"Task '{task.TaskId}' can only be updated from its assignee Mission thread.");
        if (!string.Equals(caller.MissionId, task.MissionId, StringComparison.Ordinal)
            || !string.Equals(caller.MemberId, task.AssigneeMemberId, StringComparison.OrdinalIgnoreCase))
        {
            throw AppServerErrors.InvalidParams($"Task '{task.TaskId}' is assigned to '{task.AssigneeMemberId}' in mission '{task.MissionId}' and cannot be updated by member '{caller.MemberId}' in mission '{caller.MissionId}'.");
        }

        return caller;
    }

    private static MissionThreadRecord RequireMissionCaller(
        TeamsStateDocument state,
        ManagedAppBindingToolCallContext context)
    {
        var caller = state.MissionThreads.FirstOrDefault(thread =>
            string.Equals(thread.ThreadId, context.ThreadId, StringComparison.Ordinal)
            && thread.ArchivedAt == null);
        return caller
               ?? throw AppServerErrors.InvalidParams("Teams tools can only be used from a participating Mission thread.");
    }

    private static TeamTaskRecord RequireCallerTask(
        TeamsStateDocument state,
        MissionThreadRecord caller,
        string? taskId = null)
    {
        var resolvedTaskId = string.IsNullOrWhiteSpace(taskId)
            ? caller.CurrentTaskId
            : taskId;

        TeamTaskRecord? task = null;
        if (!string.IsNullOrWhiteSpace(resolvedTaskId))
        {
            task = RequireMissionTaskReference(state, caller.MissionId, resolvedTaskId);
        }
        else
        {
            var activeTasks = state.Tasks
                .Where(item => string.Equals(item.MissionId, caller.MissionId, StringComparison.Ordinal)
                               && string.Equals(item.AssigneeMemberId, caller.MemberId, StringComparison.OrdinalIgnoreCase)
                               && item.Status is TeamTaskStatuses.Running or TeamTaskStatuses.Ready or TeamTaskStatuses.Blocked)
                .OrderByDescending(item => item.UpdatedAt)
                .Take(2)
                .ToList();
            if (activeTasks.Count == 1)
                task = activeTasks[0];
        }

        if (task == null)
            throw AppServerErrors.InvalidParams("No current Teams task is associated with this Mission thread.");
        if (!string.Equals(task.MissionId, caller.MissionId, StringComparison.Ordinal)
            || !string.Equals(task.AssigneeMemberId, caller.MemberId, StringComparison.OrdinalIgnoreCase))
        {
            throw AppServerErrors.InvalidParams($"Task '{task.TaskId}' is assigned to '{task.AssigneeMemberId}' in mission '{task.MissionId}' and cannot be updated by member '{caller.MemberId}' in mission '{caller.MissionId}'.");
        }

        return task;
    }

    private static TeamTaskRecord? ResolveMessageTask(
        TeamsStateDocument state,
        string missionId,
        MissionThreadRecord caller,
        string targetMemberId,
        string? taskId)
    {
        if (!string.IsNullOrWhiteSpace(taskId))
        {
            return RequireMissionTaskReference(state, missionId, taskId);
        }

        if (!string.Equals(caller.MemberId, "leader", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(caller.CurrentTaskId))
        {
            var currentTask = state.Tasks.FirstOrDefault(item => string.Equals(item.TaskId, caller.CurrentTaskId, StringComparison.Ordinal));
            if (currentTask != null && string.Equals(currentTask.MissionId, missionId, StringComparison.Ordinal))
                return currentTask;
        }

        if (string.Equals(caller.MemberId, "leader", StringComparison.OrdinalIgnoreCase))
        {
            var synthesisTasks = state.Tasks
                .Where(task => string.Equals(task.MissionId, missionId, StringComparison.Ordinal)
                               && string.Equals(task.AssigneeMemberId, targetMemberId, StringComparison.OrdinalIgnoreCase)
                               && TaskNeedsLeaderSynthesis(state, task))
                .OrderBy(task => task.CreatedAt)
                .Take(2)
                .ToList();
            if (synthesisTasks.Count == 1)
                return synthesisTasks[0];
        }

        return null;
    }

    private static string InferMessageKind(
        TeamsStateDocument state,
        MissionThreadRecord caller,
        string targetMemberId,
        TeamTaskRecord? relatedTask)
    {
        if (relatedTask != null
            && string.Equals(caller.MemberId, "leader", StringComparison.OrdinalIgnoreCase)
            && string.Equals(relatedTask.AssigneeMemberId, targetMemberId, StringComparison.OrdinalIgnoreCase)
            && TaskNeedsLeaderSynthesis(state, relatedTask))
        {
            return TeamMessageKinds.Synthesis;
        }

        return TeamMessageKinds.Request;
    }

    private static List<string> ExtractMentionedArtifactIds(
        TeamsStateDocument state,
        string missionId,
        string content)
    {
        var canonicalMatches = ArtifactReferencePattern
            .Matches(content)
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var result = new List<string>();
        foreach (var artifactId in canonicalMatches)
        {
            var artifact = RequireMissionArtifactReference(state, missionId, artifactId);
            result.Add(artifact.ArtifactId);
        }

        foreach (var artifactAlias in ArtifactAliasReferencePattern
                     .Matches(content)
                     .Select(match => match.Value)
                     .Distinct(StringComparer.Ordinal))
        {
            var artifact = RequireMissionArtifactReference(state, missionId, artifactAlias);
            if (!result.Contains(artifact.ArtifactId, StringComparer.Ordinal))
                result.Add(artifact.ArtifactId);
        }

        return result;
    }

    private static string DeriveMemberStatus(TeamMemberRecord member, IReadOnlyList<MissionThreadRecord> threads)
    {
        if (threads.Any(thread => string.Equals(thread.Status, "running", StringComparison.Ordinal)))
            return "running";
        if (threads.Any(thread => string.Equals(thread.Status, "approval", StringComparison.Ordinal)))
            return "approval";
        if (threads.Any(thread => string.Equals(thread.Status, "input", StringComparison.Ordinal)))
            return "input";
        if (threads.Any(thread => string.Equals(thread.Status, "queued", StringComparison.Ordinal)))
            return "queued";
        return member.Status;
    }

    private static string DescribeTasks(IEnumerable<TeamTaskRecord> tasks)
    {
        var parts = tasks
            .Take(5)
            .Select(task => $"{task.Title} ({FormatTaskReference(task)}, {task.Status})")
            .ToList();
        var count = tasks.Count();
        if (count > parts.Count)
            parts.Add($"+{count - parts.Count} more");
        return string.Join("; ", parts);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength].TrimEnd() + "…";

    private static string NormalizeTaskKind(string value)
    {
        var normalized = value.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? "work" : normalized;
    }

    private static string NormalizeMessageKind(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "" => TeamMessageKinds.Info,
            TeamMessageKinds.Info => TeamMessageKinds.Info,
            TeamMessageKinds.Request => TeamMessageKinds.Request,
            TeamMessageKinds.Handoff => TeamMessageKinds.Handoff,
            TeamMessageKinds.Revision => TeamMessageKinds.Revision,
            TeamMessageKinds.Decision => TeamMessageKinds.Decision,
            TeamMessageKinds.Blocker => TeamMessageKinds.Blocker,
            TeamMessageKinds.Synthesis => TeamMessageKinds.Synthesis,
            _ => throw AppServerErrors.InvalidParams($"Message kind must be '{TeamMessageKinds.Info}', '{TeamMessageKinds.Request}', '{TeamMessageKinds.Handoff}', '{TeamMessageKinds.Revision}', '{TeamMessageKinds.Decision}', '{TeamMessageKinds.Blocker}', or '{TeamMessageKinds.Synthesis}'.")
        };
    }

    private static string NormalizeArtifactKind(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? "reference" : normalized;
    }

    private static (string Kind, string? Format) InferArtifactClassification(string pathOrUri)
    {
        var path = pathOrUri.Trim();
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.AbsolutePath))
            path = uri.AbsolutePath;

        var markerIndex = path.IndexOfAny(new[] { '?', '#' });
        if (markerIndex >= 0)
            path = path[..markerIndex];

        string extension;
        try
        {
            extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            extension = string.Empty;
        }

        if (string.IsNullOrWhiteSpace(extension))
            return ("reference", Uri.TryCreate(pathOrUri, UriKind.Absolute, out _) ? "url" : null);

        var kind = extension switch
        {
            "patch" or "diff" => "patch",
            "json" or "csv" or "tsv" or "xlsx" or "xls" => "dataset",
            "md" or "markdown" or "txt" or "doc" or "docx" or "pdf" or "rtf" => "document",
            _ => "reference"
        };
        return (kind, extension);
    }

    private static string NormalizeProgressStatus(string status)
    {
        var normalized = status.Trim();
        if (string.Equals(normalized, TeamTaskStatuses.Running, StringComparison.Ordinal)
            || string.Equals(normalized, TeamTaskStatuses.Blocked, StringComparison.Ordinal))
        {
            return normalized;
        }

        throw AppServerErrors.InvalidParams($"ReportProgress status must be '{TeamTaskStatuses.Running}' or '{TeamTaskStatuses.Blocked}'. Use MarkTaskDone to complete a task.");
    }

    private static TeamMemberRecord ResolveAssignee(TeamsStateDocument state, string value)
    {
        var normalized = value.Trim();
        return state.Members.FirstOrDefault(m =>
                   string.Equals(m.MemberId, normalized, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(m.Role, normalized, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(m.DisplayName, normalized, StringComparison.OrdinalIgnoreCase))
               ?? throw AppServerErrors.InvalidParams($"Team member '{value}' was not found.");
    }

    private static TeamMemberRecord RequireMember(TeamsStateDocument state, string memberId) =>
        state.Members.FirstOrDefault(m => string.Equals(m.MemberId, memberId, StringComparison.OrdinalIgnoreCase)
                                          || string.Equals(m.Role, memberId, StringComparison.OrdinalIgnoreCase)
                                          || string.Equals(m.DisplayName, memberId, StringComparison.OrdinalIgnoreCase))
        ?? throw AppServerErrors.InvalidParams($"Team member '{memberId}' was not found.");

    private static MissionRecord RequireMission(TeamsStateDocument state, string missionId) =>
        state.Missions.FirstOrDefault(m => string.Equals(m.MissionId, missionId, StringComparison.Ordinal))
        ?? throw AppServerErrors.InvalidParams($"Mission '{missionId}' was not found.");

    private static MissionThreadRecord? FindMissionThread(TeamsStateDocument state, string missionId, string memberId) =>
        state.MissionThreads.FirstOrDefault(t => string.Equals(t.MissionId, missionId, StringComparison.Ordinal)
                                                 && string.Equals(t.MemberId, memberId, StringComparison.OrdinalIgnoreCase));

    private static MissionThreadRecord RequireMissionThread(TeamsStateDocument state, string missionId, string memberId) =>
        FindMissionThread(state, missionId, memberId)
        ?? throw AppServerErrors.InvalidParams($"Mission thread for mission '{missionId}' and member '{memberId}' was not found.");

    private static TeamTaskRecord RequireMissionTaskReference(TeamsStateDocument state, string missionId, string taskReference)
    {
        var trimmed = taskReference.Trim();
        var task = state.Tasks.FirstOrDefault(t =>
            string.Equals(t.MissionId, missionId, StringComparison.Ordinal)
            && (string.Equals(t.TaskId, trimmed, StringComparison.Ordinal)
                || string.Equals(t.Alias, trimmed, StringComparison.Ordinal)));
        return task
               ?? throw AppServerErrors.InvalidParams($"Task '{taskReference}' was not found in mission '{missionId}'. Use the task alias such as t1 or the canonical task id.");
    }

    private static ArtifactRefRecord RequireMissionArtifactReference(TeamsStateDocument state, string missionId, string artifactReference)
    {
        var trimmed = artifactReference.Trim();
        var missionTaskIds = state.Tasks
            .Where(task => string.Equals(task.MissionId, missionId, StringComparison.Ordinal))
            .Select(task => task.TaskId)
            .ToHashSet(StringComparer.Ordinal);
        var artifact = state.Artifacts.FirstOrDefault(item =>
            missionTaskIds.Contains(item.TaskId)
            && (string.Equals(item.ArtifactId, trimmed, StringComparison.Ordinal)
                || string.Equals(item.Alias, trimmed, StringComparison.Ordinal)));
        return artifact
               ?? throw AppServerErrors.InvalidParams($"Artifact '{artifactReference}' was not found in mission '{missionId}'. Use the artifact alias such as a1 or the canonical artifact id.");
    }

    private static TeamTaskRecord RequireTask(TeamsStateDocument state, string taskId) =>
        state.Tasks.FirstOrDefault(t => string.Equals(t.TaskId, taskId, StringComparison.Ordinal))
        ?? throw AppServerErrors.InvalidParams($"Task '{taskId}' was not found.");

    private static bool IsDoneTask(TeamsStateDocument state, string taskId) =>
        state.Tasks.FirstOrDefault(t => string.Equals(t.TaskId, taskId, StringComparison.Ordinal))?.Status == TeamTaskStatuses.Done;

    private static List<string> UnresolvedDependencyIds(TeamsStateDocument state, TeamTaskRecord task) =>
        task.DependsOnTaskIds
            .Distinct(StringComparer.Ordinal)
            .Where(dependencyId => !IsDoneTask(state, dependencyId))
            .ToList();

    private static bool AreTaskDependenciesSatisfied(TeamsStateDocument state, TeamTaskRecord task) =>
        UnresolvedDependencyIds(state, task).Count == 0;

    private static bool TaskNeedsLeaderSynthesis(TeamsStateDocument state, TeamTaskRecord task) =>
        task.RequiresLeaderSynthesis
        && string.IsNullOrWhiteSpace(task.SynthesisMessageId)
        && task.Status is not (TeamTaskStatuses.Done or TeamTaskStatuses.Failed or TeamTaskStatuses.Cancelled or TeamTaskStatuses.Blocked or TeamTaskStatuses.Running)
        && string.IsNullOrWhiteSpace(task.QueuedInputId)
        && AreTaskDependenciesSatisfied(state, task);

    private static bool IsMissionFinalizationTask(TeamTaskRecord task) =>
        task.RequiredForMission || string.Equals(task.Kind, "review", StringComparison.OrdinalIgnoreCase);

    private static bool IsLegacyDoneTaskStatus(string status) =>
        status is TeamTaskStatuses.Done
            or "completed"
            or "complete"
            or "succeeded"
            or "success";

    private static bool IsTerminalMissionStatus(string status) =>
        status is TeamMissionStatuses.Done or TeamMissionStatuses.Cancelled;

    private static bool IsBusyMissionThreadStatus(string status) =>
        status is "running" or "approval" or "input";

    private static void EnsureMissionCanReceiveWork(MissionRecord mission)
    {
        if (mission.ArchivedAt != null)
            throw AppServerErrors.InvalidParams($"Mission '{mission.MissionId}' is archived.");
        if (IsTerminalMissionStatus(mission.Status))
            throw AppServerErrors.InvalidParams($"Mission '{mission.MissionId}' is already {mission.Status}.");
    }

    private static void EnsureTaskMissionCanReceiveWork(TeamsStateDocument state, TeamTaskRecord task)
    {
        var mission = RequireMission(state, task.MissionId);
        EnsureMissionCanReceiveWork(mission);
    }

    private static void ReconcileMissionCompletion(
        TeamsStateDocument state,
        string missionId,
        DateTimeOffset now,
        string summary)
    {
        var mission = state.Missions.FirstOrDefault(m => string.Equals(m.MissionId, missionId, StringComparison.Ordinal));
        if (mission == null || mission.ArchivedAt != null || IsTerminalMissionStatus(mission.Status))
            return;

        var missionTasks = state.Tasks.Where(t => string.Equals(t.MissionId, missionId, StringComparison.Ordinal)).ToList();
        if (missionTasks.Count > 0 && missionTasks.All(t => t.Status == "done"))
            CompleteMission(mission, now, summary);
    }

    private static void CompleteMission(MissionRecord mission, DateTimeOffset now, string summary)
    {
        mission.Status = "done";
        mission.CompletedAt ??= now;
        mission.CompletionSummary = summary;
        mission.UpdatedAt = now;
    }

    private static TeamMemberView CopyMember(TeamMemberRecord member) =>
        new()
        {
            MemberId = member.MemberId,
            Role = member.Role,
            DisplayName = member.DisplayName,
            Description = member.Description,
            ThreadId = member.ThreadId,
            BindingId = member.BindingId,
            GrantId = member.GrantId,
            AvatarAccent = member.AvatarAccent,
            Status = member.Status,
            CurrentTaskId = member.CurrentTaskId,
            DeskX = member.DeskX,
            DeskY = member.DeskY
        };

    private static MissionThreadView CopyMissionThread(MissionThreadRecord missionThread) =>
        new()
        {
            MissionId = missionThread.MissionId,
            MemberId = missionThread.MemberId,
            ThreadId = missionThread.ThreadId,
            BindingId = missionThread.BindingId,
            GrantId = missionThread.GrantId,
            Status = missionThread.Status,
            CurrentTaskId = missionThread.CurrentTaskId,
            QueuedInputId = missionThread.QueuedInputId,
            CreatedAt = missionThread.CreatedAt,
            UpdatedAt = missionThread.UpdatedAt,
            ArchivedAt = missionThread.ArchivedAt
        };

    private static JsonObject? MergeMetadata(JsonObject? existing, JsonObject? update)
    {
        if (update == null)
            return existing;

        var merged = existing == null
            ? new JsonObject()
            : (JsonObject)existing.DeepClone();
        foreach (var item in update)
            merged[item.Key] = item.Value?.DeepClone();

        return merged;
    }

    private static DynamicToolCallResult Ok(string text, object structured) =>
        new()
        {
            Success = true,
            ContentItems = [new ExtChannelToolContentItem { Type = "text", Text = text }],
            StructuredResult = JsonSerializer.SerializeToNode(structured, SessionWireJsonOptions.Default)
        };

    private static string FormatAppServerException(AppServerException ex)
    {
        if (ex.ErrorData == null)
            return ex.Message;

        try
        {
            if (JsonSerializer.SerializeToNode(ex.ErrorData, SessionWireJsonOptions.Default) is JsonObject obj
                && obj.TryGetPropertyValue("detail", out var detailNode))
            {
                var detail = detailNode?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(detail))
                    return detail;
            }
        }
        catch
        {
            // Fall back to the JSON-RPC message if the error data is not string-shaped.
        }

        return ex.Message;
    }

    private static DynamicToolCallResult Fail(string code, string message) =>
        new()
        {
            Success = false,
            ErrorCode = code,
            ErrorMessage = message,
            ContentItems = [new ExtChannelToolContentItem { Type = "text", Text = $"{code}: {message}" }]
        };

    private static bool IsExternalThreadCatalogSurface(string surface)
    {
        var normalized = AppBindingCatalogSurfaces.Normalize(surface);
        return string.Equals(normalized, AppBindingCatalogSurfaces.Welcome, StringComparison.Ordinal)
               || string.Equals(normalized, AppBindingCatalogSurfaces.ThreadBinding, StringComparison.Ordinal);
    }

    private static AppDescriptor BuildThreadBindingDescriptor() =>
        new()
        {
            AppId = TeamsConstants.AppId,
            ToolNamespace = TeamsConstants.ToolNamespace,
            DisplayName = "Agent Teams",
            DeveloperName = "DotHarness",
            Description = "Ask Agent Teams to execute an asynchronous mission from this thread.",
            Category = "Productivity",
            Connection = new AppConnectionDescriptor
            {
                HandoffModes = []
            },
            NativeApplication = new AppNativeApplicationDescriptor
            {
                DisplayName = "DotCraft",
                Protocol = string.Empty
            },
            Scopes =
            [
                Scope("mission.manage", "Create Team missions", "Start an Agent Teams mission from this thread and receive its completion notification.", AppBindingRisks.Mutate)
            ],
            ToolCatalog =
            [
                Tool(CreateTeamToolName, "mission.manage", AppBindingRisks.Mutate)
            ],
            DynamicToolCatalog = new AppDynamicToolCatalogDescriptor
            {
                Enabled = false
            }
        };

    private static AppDescriptor BuildDescriptor() =>
        new()
        {
            AppId = TeamsConstants.AppId,
            ToolNamespace = TeamsConstants.ToolNamespace,
            DisplayName = "DotCraft Teams",
            DeveloperName = "DotHarness",
            Description = "Run a DotCraft Team with robot teammates, missions, task dispatch, progress digests, and artifacts.",
            Category = "Productivity",
            // Per-role origin branding: each mission member thread stamps ChannelName="teams" and
            // ChannelContext="{missionId}:{memberId}", so the host shows each role's avatar in the
            // thread-list origin badge. Avatars live in the agent-teams plugin assets.
            OriginChannel = TeamsConstants.ChannelName,
            OriginMembers =
            [
                new AppOriginMemberDescriptor { Match = "leader", DisplayName = "Team Leader", Icon = "./assets/team-leader.svg" },
                new AppOriginMemberDescriptor { Match = "explorer", DisplayName = "Explorer", Icon = "./assets/team-explorer.svg" },
                new AppOriginMemberDescriptor { Match = "builder", DisplayName = "Builder", Icon = "./assets/team-builder.svg" },
                new AppOriginMemberDescriptor { Match = "reviewer", DisplayName = "Reviewer", Icon = "./assets/team-reviewer.svg" },
                new AppOriginMemberDescriptor { Match = "operator", DisplayName = "Operator", Icon = "./assets/team-operator.svg" }
            ],
            Connection = new AppConnectionDescriptor
            {
                HandoffModes =
                [
                    new AppHandoffModeDescriptor
                    {
                        Mode = "managed",
                        UriTemplate = "dotcraft://managed/teams/{operation}?app={appId}"
                    }
                ]
            },
            NativeApplication = new AppNativeApplicationDescriptor
            {
                DisplayName = "DotCraft",
                Protocol = "dotcraft"
            },
            Scopes =
            [
                Scope("team.read", "Read Team state", "Read Teams team, member, mission, task, digest, and artifact summaries.", AppBindingRisks.Read),
                Scope("mission.manage", "Manage missions", "Create mission plans and update mission state.", AppBindingRisks.Mutate),
                Scope("task.dispatch", "Dispatch tasks", "Create task graph entries for scheduler dispatch.", AppBindingRisks.Mutate),
                Scope("message.send", "Send team messages", "Record lightweight mission-scoped mailbox events for participating Team members.", AppBindingRisks.Mutate),
                Scope("artifact.publish", "Publish artifacts", "Record app-owned artifact references.", AppBindingRisks.Mutate)
            ],
            ToolCatalog =
            [
                Tool("CreateMissionPlan", "mission.manage", AppBindingRisks.Mutate),
                Tool("AssignTask", "task.dispatch", AppBindingRisks.Mutate),
                Tool("ListTeamMembers", "team.read", AppBindingRisks.Read),
                Tool("ReadMissionState", "team.read", AppBindingRisks.Read),
                Tool("ReadMemberStatus", "team.read", AppBindingRisks.Read),
                Tool("SendMessage", "message.send", AppBindingRisks.Mutate),
                Tool("ReportProgress", "mission.manage", AppBindingRisks.Mutate),
                Tool("PublishArtifact", "artifact.publish", AppBindingRisks.Mutate),
                Tool("MarkTaskDone", "mission.manage", AppBindingRisks.Mutate),
                Tool("MarkMissionDone", "mission.manage", AppBindingRisks.Mutate)
            ],
            DynamicToolCatalog = new AppDynamicToolCatalogDescriptor
            {
                Enabled = false
            }
        };

    private static AppScopeDescriptor Scope(string id, string displayName, string description, string risk) =>
        new()
        {
            Id = id,
            DisplayName = displayName,
            Description = description,
            Risk = risk,
            DefaultSelected = true
        };

    private static AppToolCatalogEntry Tool(string name, string scope, string risk) =>
        new()
        {
            Name = name,
            Scope = scope,
            Risk = risk,
            DefaultExposure = risk == AppBindingRisks.Read ? AppBindingExposures.Direct : AppBindingExposures.Deferred,
            Description = name
        };

}
