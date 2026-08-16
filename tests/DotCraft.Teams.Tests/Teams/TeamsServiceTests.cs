using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Context;
using DotCraft.Teams;
using DotCraft.Tests.Sessions.Protocol.AppServer;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using DotCraft.Sessions;
using SessionIdentity = DotCraft.Sessions.SessionIdentity;
using SessionThread = DotCraft.Sessions.SessionThread;
using ThreadConfiguration = DotCraft.Sessions.ThreadConfiguration;
using Xunit;

namespace DotCraft.Tests.Teams;

public sealed class TeamsServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"teams_{Guid.NewGuid():N}");
    private readonly string _workspaceCraftPath;
    private readonly TestableSessionService _sessionService;
    private readonly TeamsService _teamsService;
    private readonly TeamsToolSource _toolSource;

    public TeamsServiceTests()
    {
        _workspaceCraftPath = Path.Combine(_tempRoot, ".craft");
        Directory.CreateDirectory(_workspaceCraftPath);
        _sessionService = new TestableSessionService(new ThreadStore(_tempRoot));
        _teamsService = new TeamsService(_tempRoot, _workspaceCraftPath);
        _teamsService.SetSessionService(_sessionService);
        _toolSource = new TeamsToolSource(_teamsService);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public async Task ViewTeam_InitializesVersionedRoster()
    {
        var view = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);

        Assert.Equal(5, view.Members.Count);
        Assert.Contains(view.Members, member => member.MemberId == "leader");
        using var state = JsonDocument.Parse(File.ReadAllText(StatePath()));
        Assert.Equal(1, state.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.False(state.RootElement.GetProperty("team").TryGetProperty("enabled", out _));
    }

    [Fact]
    public async Task ViewTeam_ReinitializesUnsupportedSchema()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath())!);
        await File.WriteAllTextAsync(
            StatePath(),
            """
            {
              "schemaVersion": 0,
              "team": { "teamId": "unsupported" },
              "members": [{ "memberId": "unsupported-member" }],
              "missions": [{ "missionId": "unsupported-mission", "title": "Unsupported" }]
            }
            """);

        var view = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);

        Assert.Empty(view.Missions);
        Assert.DoesNotContain(view.Members, member => member.MemberId == "unsupported-member");
        Assert.Equal(5, view.Members.Count);
        var json = await File.ReadAllTextAsync(StatePath());
        Assert.Contains("\"schemaVersion\": 1", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateMission_CreatesLeaderThreadAndStaticContext()
    {
        var created = await CreateMissionAsync("Ship release", "Run the release mission.");
        var leader = Assert.Single(created.Team.MissionThreads, item => item.MemberId == "leader");
        var thread = await _sessionService.GetThreadAsync(leader.ThreadId);

        Assert.Equal("teams", thread.OriginChannel);
        Assert.Contains("DotCraft Team Leader", thread.Configuration!.RoleInstructions, StringComparison.Ordinal);
        var provider = new TeamsThreadSystemPromptContextProvider(_teamsService);
        var section = provider.GetSystemPromptSection(new ThreadSystemPromptContext(thread.Id, _tempRoot));
        Assert.Contains("## Teams mission context", section, StringComparison.Ordinal);
        Assert.Contains("Ship release", section, StringComparison.Ordinal);
        Assert.Contains("Mission prompt:", section, StringComparison.Ordinal);
        Assert.DoesNotContain("binding", section, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancelMission_StopsThreadAndPreventsToolFallback()
    {
        var created = await CreateMissionAsync("Cancel", "Stop this mission.");
        var leader = Assert.Single(created.Team.MissionThreads);

        await _teamsService.CancelMissionAsync(
            _sessionService,
            _workspaceCraftPath,
            new TeamsMissionCancelCommand { MissionId = created.Mission.MissionId },
            CancellationToken.None);
        var registrations = await RegistrationsAsync(leader.ThreadId, ToolPlanningThreadKind.UserTopLevel);

        Assert.Empty(registrations);
        Assert.Empty((await _sessionService.GetThreadAsync(leader.ThreadId)).QueuedInputs);
    }

    [Fact]
    public void OriginPresentation_UsesMemberIdentity()
    {
        var provider = new TeamsThreadOriginPresentationProvider();

        var result = provider.Resolve(new ThreadOriginPresentationContext(
            "thread-1",
            _tempRoot,
            "teams",
            "mission_1:builder"));

        Assert.NotNull(result);
        Assert.Equal("agent-teams", result.SourceId);
        Assert.Equal("Builder", result.DisplayName);
        Assert.Equal("builder", result.SubjectId);
        Assert.Equal("member", result.SubjectKind);
        Assert.StartsWith("data:image/svg+xml;base64,", result.Icon, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissionCompletion_EnqueuesResultToOriginThread()
    {
        var origin = await CreateUserThreadAsync("origin");
        var created = await _teamsService.CreateMissionAsync(
            _sessionService,
            _tempRoot,
            _workspaceCraftPath,
            new TeamsMissionCreateCommand { Title = "Answer", Prompt = "Return an answer." },
            CancellationToken.None,
            origin.Id);
        var leader = Assert.Single(created.Team.MissionThreads);
        var registrations = await RegistrationsAsync(leader.ThreadId, ToolPlanningThreadKind.ModuleManaged);

        var result = await InvokeAsync(
            registrations.Single(item => item.Definition.Name.Name == "MarkMissionDone"),
            leader.ThreadId,
            new JsonObject { ["finalResponse"] = "Mission answer." });

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal("Mission marked done.", result.Content);
        Assert.NotNull(result.StructuredContent);
        Assert.Contains(
            _sessionService.LastSubmittedContent,
            content => content is TextContent text && text.Text.Contains("Mission answer.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Dependencies_ReleaseDownstreamOnlyAfterUpstreamDone()
    {
        var created = await CreateMissionAsync("Dependency graph", "Research before building.");
        var leader = Assert.Single(created.Team.MissionThreads);
        Assert.True((await InvokeTeamToolAsync(leader.ThreadId, "AssignTask", new JsonObject
        {
            ["assignee"] = "explorer",
            ["title"] = "Research inputs",
            ["prompt"] = "Find builder inputs."
        })).Success);
        var firstView = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var research = Assert.Single(firstView.Tasks);

        Assert.True((await InvokeTeamToolAsync(leader.ThreadId, "AssignTask", new JsonObject
        {
            ["assignee"] = "builder",
            ["title"] = "Build from research",
            ["prompt"] = "Wait for research.",
            ["dependsOnTaskIds"] = new JsonArray(research.Alias)
        })).Success);
        var waiting = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var build = Assert.Single(waiting.Tasks, task => task.AssigneeMemberId == "builder");
        Assert.Equal(TeamTaskStatuses.WaitingDependencies, build.Status);
        Assert.DoesNotContain(waiting.MissionThreads, thread => thread.MemberId == "builder");

        var explorer = Assert.Single(waiting.MissionThreads, thread => thread.MemberId == "explorer");
        Assert.True((await InvokeTeamToolAsync(explorer.ThreadId, "MarkTaskDone", new JsonObject
        {
            ["summary"] = "Research ready."
        })).Success);
        var released = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        Assert.Equal(TeamTaskStatuses.Running, released.Tasks.Single(task => task.TaskId == build.TaskId).Status);
        Assert.Contains(released.MissionThreads, thread => thread.MemberId == "builder" && thread.CurrentTaskId == build.TaskId);
    }

    [Fact]
    public async Task MarkTaskDone_WakesLeaderWhileOtherWorkRemains()
    {
        var created = await CreateMissionAsync("Parallel work", "Wake the leader for partial results.");
        var leader = Assert.Single(created.Team.MissionThreads);
        Assert.True((await InvokeTeamToolAsync(leader.ThreadId, "AssignTask", new JsonObject
        {
            ["assignee"] = "explorer", ["title"] = "Research", ["prompt"] = "Research."
        })).Success);
        Assert.True((await InvokeTeamToolAsync(leader.ThreadId, "AssignTask", new JsonObject
        {
            ["assignee"] = "builder", ["title"] = "Build", ["prompt"] = "Keep working."
        })).Success);
        var assigned = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var explorerTask = assigned.Tasks.Single(task => task.AssigneeMemberId == "explorer");
        var explorer = assigned.MissionThreads.Single(thread => thread.MemberId == "explorer");
        await _teamsService.HandleThreadRuntimeSignalAsync(
            _workspaceCraftPath, leader.ThreadId, SessionThreadRuntimeSignal.TurnCompleted, CancellationToken.None);

        Assert.True((await InvokeTeamToolAsync(explorer.ThreadId, "MarkTaskDone", new JsonObject
        {
            ["summary"] = "Research complete."
        })).Success);
        var woken = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        Assert.Equal(TeamMissionStatuses.Active, Assert.Single(woken.Missions).Status);
        Assert.NotNull(woken.Tasks.Single(task => task.TaskId == explorerTask.TaskId).LeaderNotifiedAt);
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(woken.Missions).LeaderContinuationQueuedInputId));
        Assert.Contains(_sessionService.LastSubmittedContent,
            content => content is TextContent text && text.Text.Contains("Team task result available", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MailboxMessages_CoalesceIntoOneQueuedTurn()
    {
        var created = await CreateMissionAsync("Mailbox", "Coalesce teammate messages.");
        var leader = Assert.Single(created.Team.MissionThreads);
        Assert.True((await InvokeTeamToolAsync(leader.ThreadId, "AssignTask", new JsonObject
        {
            ["assignee"] = "builder", ["title"] = "Draft", ["prompt"] = "Draft."
        })).Success);
        var assigned = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var task = Assert.Single(assigned.Tasks);
        var builder = Assert.Single(assigned.MissionThreads, thread => thread.MemberId == "builder");
        Assert.True((await InvokeTeamToolAsync(builder.ThreadId, "MarkTaskDone", new JsonObject
        {
            ["summary"] = "Draft complete."
        })).Success);
        await _teamsService.HandleThreadRuntimeSignalAsync(
            _workspaceCraftPath, builder.ThreadId, SessionThreadRuntimeSignal.TurnStarted, CancellationToken.None);

        foreach (var message in new[] { "Include risks.", "Include delivery checklist.", "Include owners." })
        {
            Assert.True((await InvokeTeamToolAsync(leader.ThreadId, "SendMessage", new JsonObject
            {
                ["to"] = "builder", ["taskId"] = task.Alias, ["message"] = message
            })).Success);
        }
        var busy = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        Assert.All(busy.Messages, message => Assert.Null(message.DeliveredQueuedInputId));

        await _teamsService.HandleThreadRuntimeSignalAsync(
            _workspaceCraftPath, builder.ThreadId, SessionThreadRuntimeSignal.TurnCompleted, CancellationToken.None);
        var delivered = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        Assert.Equal(3, delivered.Messages.Count);
        Assert.Single(delivered.Messages.Select(message => message.DeliveredQueuedInputId).Distinct(StringComparer.Ordinal));
        Assert.All(delivered.Messages, message => Assert.Equal(TeamMessageStatuses.DeliveredToTurn, message.Status));
        Assert.Contains(_sessionService.LastSubmittedContent,
            content => content is TextContent text
                       && text.Text.Contains("Include risks", StringComparison.Ordinal)
                       && text.Text.Contains("delivery checklist", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BusinessGuards_RejectWrongAssigneeAndPrematureFinalization()
    {
        var created = await CreateMissionAsync("Guards", "Enforce task and leader ownership.");
        var leader = Assert.Single(created.Team.MissionThreads);
        Assert.True((await InvokeTeamToolAsync(leader.ThreadId, "AssignTask", new JsonObject
        {
            ["assignee"] = "explorer", ["title"] = "Explorer task", ["prompt"] = "Explore."
        })).Success);
        Assert.True((await InvokeTeamToolAsync(leader.ThreadId, "AssignTask", new JsonObject
        {
            ["assignee"] = "builder", ["title"] = "Builder task", ["prompt"] = "Build."
        })).Success);
        var view = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var explorerTask = view.Tasks.Single(task => task.AssigneeMemberId == "explorer");
        var builder = view.MissionThreads.Single(thread => thread.MemberId == "builder");

        var wrongAssignee = await InvokeTeamToolAsync(builder.ThreadId, "PublishArtifact", new JsonObject
        {
            ["title"] = "Wrong", ["pathOrUri"] = "wrong.md", ["taskId"] = explorerTask.Alias
        });
        Assert.False(wrongAssignee.Success);
        Assert.Equal(ToolErrorCodes.Unauthorized, wrongAssignee.Error?.Code);
        Assert.Contains("cannot be updated", wrongAssignee.Error?.Message, StringComparison.Ordinal);

        var premature = await InvokeTeamToolAsync(leader.ThreadId, "MarkMissionDone", new JsonObject
        {
            ["finalResponse"] = "Too early."
        });
        Assert.False(premature.Success);
        Assert.Contains("unfinished work", premature.Error?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReviewGate_ReachesAwaitingLeaderReviewOnlyAfterReviewCompletes()
    {
        var created = await CreateMissionAsync("Review gate", "Build and review.");
        var leader = Assert.Single(created.Team.MissionThreads);
        Assert.True((await InvokeTeamToolAsync(leader.ThreadId, "AssignTask", new JsonObject
        {
            ["assignee"] = "builder", ["title"] = "Build artifact", ["prompt"] = "Build."
        })).Success);
        var buildView = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var buildTask = Assert.Single(buildView.Tasks);
        Assert.True((await InvokeTeamToolAsync(leader.ThreadId, "AssignTask", new JsonObject
        {
            ["assignee"] = "reviewer",
            ["title"] = "Review artifact",
            ["prompt"] = "Review.",
            ["kind"] = "review",
            ["dependsOnTaskIds"] = new JsonArray(buildTask.Alias)
        })).Success);
        await _teamsService.HandleThreadRuntimeSignalAsync(
            _workspaceCraftPath, leader.ThreadId, SessionThreadRuntimeSignal.TurnCompleted, CancellationToken.None);
        var waiting = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var reviewTask = waiting.Tasks.Single(task => task.Kind == "review");
        Assert.Equal(TeamTaskStatuses.WaitingDependencies, reviewTask.Status);

        var builder = waiting.MissionThreads.Single(thread => thread.MemberId == "builder");
        Assert.True((await InvokeTeamToolAsync(builder.ThreadId, "MarkTaskDone", new JsonObject
        {
            ["summary"] = "Ready for review."
        })).Success);
        var reviewing = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        Assert.Equal(TeamTaskStatuses.Running, reviewing.Tasks.Single(task => task.TaskId == reviewTask.TaskId).Status);
        await _teamsService.HandleThreadRuntimeSignalAsync(
            _workspaceCraftPath, leader.ThreadId, SessionThreadRuntimeSignal.TurnCompleted, CancellationToken.None);

        var reviewer = reviewing.MissionThreads.Single(thread => thread.MemberId == "reviewer");
        Assert.True((await InvokeTeamToolAsync(reviewer.ThreadId, "MarkTaskDone", new JsonObject
        {
            ["summary"] = "Review passed."
        })).Success);
        var ready = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        Assert.Equal(TeamMissionStatuses.AwaitingLeaderReview, Assert.Single(ready.Missions).Status);
        Assert.Null(Assert.Single(ready.Missions).CompletedAt);
        Assert.Contains(_sessionService.LastSubmittedContent,
            content => content is TextContent text && text.Text.Contains("Mission ready for Leader finalization", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CompletionRecovery_RetriesOnceThenBlocksAndWakesLeader()
    {
        var created = await CreateMissionAsync("Recovery", "Recover an unfinished turn.");
        var leader = Assert.Single(created.Team.MissionThreads);
        Assert.True((await InvokeTeamToolAsync(leader.ThreadId, "AssignTask", new JsonObject
        {
            ["assignee"] = "builder", ["title"] = "Do not finish", ["prompt"] = "End without completion."
        })).Success);
        await _teamsService.HandleThreadRuntimeSignalAsync(
            _workspaceCraftPath, leader.ThreadId, SessionThreadRuntimeSignal.TurnCompleted, CancellationToken.None);
        var active = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var builder = active.MissionThreads.Single(thread => thread.MemberId == "builder");

        await _teamsService.HandleThreadRuntimeSignalAsync(
            _workspaceCraftPath, builder.ThreadId, SessionThreadRuntimeSignal.TurnCompleted, CancellationToken.None);
        var recovery = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var recoveryTask = Assert.Single(recovery.Tasks);
        Assert.True(recoveryTask.CompletionRecoveryPending);
        Assert.Equal(1, recoveryTask.CompletionRecoveryAttempts);
        Assert.False(string.IsNullOrWhiteSpace(recoveryTask.CompletionRecoveryQueuedInputId));
        Assert.Contains(_sessionService.LastSubmittedContent,
            content => content is TextContent text && text.Text.Contains("Task completion check", StringComparison.Ordinal));

        await _teamsService.HandleThreadRuntimeSignalAsync(
            _workspaceCraftPath, builder.ThreadId, SessionThreadRuntimeSignal.TurnCompleted, CancellationToken.None);
        var blocked = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var blockedTask = Assert.Single(blocked.Tasks);
        Assert.Equal(TeamTaskStatuses.Blocked, blockedTask.Status);
        Assert.False(blockedTask.CompletionRecoveryPending);
        Assert.Contains("completion recovery prompt", blockedTask.BlockedReason, StringComparison.Ordinal);
        Assert.Contains(_sessionService.LastSubmittedContent,
            content => content is TextContent text && text.Text.Contains("Mission needs Leader coordination", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SameMemberAcrossMissions_IsSerialized()
    {
        var first = await CreateMissionAsync("First", "Plan first.");
        var firstLeader = Assert.Single(first.Team.MissionThreads);
        Assert.Equal("running", firstLeader.Status);

        var second = await CreateMissionAsync("Second", "Plan second.");
        var blocked = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var secondLeader = blocked.MissionThreads.Single(thread => thread.MissionId == second.Mission.MissionId);
        Assert.Equal("queued", secondLeader.Status);
        Assert.Single((await _sessionService.GetThreadAsync(secondLeader.ThreadId)).QueuedInputs);

        await _teamsService.HandleThreadRuntimeSignalAsync(
            _workspaceCraftPath, firstLeader.ThreadId, SessionThreadRuntimeSignal.TurnCompleted, CancellationToken.None);
        var released = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        Assert.Equal("running", released.MissionThreads.Single(thread => thread.MissionId == second.Mission.MissionId).Status);
        Assert.Empty((await _sessionService.GetThreadAsync(secondLeader.ThreadId)).QueuedInputs);
    }

    private Task<TeamsMissionCreateOutcome> CreateMissionAsync(string title, string prompt) =>
        _teamsService.CreateMissionAsync(
            _sessionService,
            _tempRoot,
            _workspaceCraftPath,
            new TeamsMissionCreateCommand { Title = title, Prompt = prompt },
            CancellationToken.None);

    private Task<SessionThread> CreateUserThreadAsync(string context) =>
        _sessionService.CreateThreadAsync(
            new SessionIdentity
            {
                WorkspacePath = _tempRoot,
                ChannelName = "desktop",
                ChannelContext = context,
                UserId = "user"
            },
            new ThreadConfiguration { Mode = "agent" });

    private async Task<IReadOnlyList<ToolRegistration>> RegistrationsAsync(
        string threadId,
        ToolPlanningThreadKind kind) =>
        await _toolSource.GetRegistrationsAsync(
            new ToolPlanningContext(threadId, null, _tempRoot, _workspaceCraftPath, "agent", null, [], 1, kind));

    private async Task<ToolExecutionResult> InvokeTeamToolAsync(
        string threadId,
        string toolName,
        JsonObject arguments)
    {
        var registrations = await RegistrationsAsync(threadId, ToolPlanningThreadKind.ModuleManaged);
        var registration = registrations.Single(item => item.Definition.Name.Name == toolName);
        return await InvokeAsync(registration, threadId, arguments);
    }

    private static ValueTask<ToolExecutionResult> InvokeAsync(
        ToolRegistration registration,
        string threadId,
        JsonObject arguments) =>
        registration.Binding.Runtime.InvokeAsync(
            new ToolInvocationContext(
                threadId,
                null,
                $"call_{Guid.NewGuid():N}",
                ToolInvocationAudience.Model,
                registration.Definition.Name,
                registration.Definition.Id,
                registration.Binding.Id,
                registration.Binding.Revision,
                DateTimeOffset.UtcNow),
            arguments);

    private string StatePath() => Path.Combine(_workspaceCraftPath, "teams", "state.json");
}
