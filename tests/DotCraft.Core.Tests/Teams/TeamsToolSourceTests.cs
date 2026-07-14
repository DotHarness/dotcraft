using System.Text.Json.Nodes;
using DotCraft.Protocol;
using DotCraft.Teams;
using DotCraft.Tests.Sessions.Protocol.AppServer;
using DotCraft.Tools;

namespace DotCraft.Tests.Teams;

public sealed class TeamsToolSourceTests : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), $"teams_source_{Guid.NewGuid():N}");
    private readonly TestableSessionService _sessions;
    private readonly TeamsService _service;
    private readonly TeamsToolSource _source;

    public TeamsToolSourceTests()
    {
        Directory.CreateDirectory(Path.Combine(_workspace, ".craft"));
        _sessions = new TestableSessionService(new ThreadStore(_workspace));
        _service = new TeamsService();
        _service.SetSessionService(_sessions);
        _source = new TeamsToolSource(_service);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_workspace))
                Directory.Delete(_workspace, true);
        }
        catch
        {
        }
    }

    [Theory]
    [InlineData(ToolPlanningThreadKind.ModuleManaged)]
    [InlineData(ToolPlanningThreadKind.SubAgentChild)]
    [InlineData(ToolPlanningThreadKind.Unattended)]
    [InlineData(ToolPlanningThreadKind.Internal)]
    [InlineData(ToolPlanningThreadKind.Unknown)]
    public async Task NonUserThreads_DoNotReceiveCreateTeam(ToolPlanningThreadKind kind)
    {
        var thread = await CreateThreadAsync("desktop");

        Assert.Empty(await GetRegistrationsAsync(thread.Id, kind));
    }

    [Fact]
    public async Task UserTopLevel_ReceivesOnlyDirectModelCreateTeam()
    {
        var thread = await CreateThreadAsync("desktop");

        var registration = Assert.Single(await GetRegistrationsAsync(thread.Id, ToolPlanningThreadKind.UserTopLevel));

        Assert.Equal(ToolSourceKind.PluginNative, registration.Definition.Id.Kind);
        Assert.Equal("agent-teams", registration.Definition.Id.SourceId);
        Assert.Equal(new ToolName("teams", "CreateTeam"), registration.Definition.Name);
        Assert.Equal(ToolExposure.Direct, registration.Exposure);
        Assert.Equal(ToolInvocationAudience.Model, registration.InvocationAudiences);
        var properties = registration.Definition.InputSchema.GetProperty("properties");
        Assert.True(properties.TryGetProperty("title", out _));
        Assert.True(properties.TryGetProperty("prompt", out _));
        Assert.False(properties.TryGetProperty("threadId", out _));
        Assert.False(properties.TryGetProperty("missionId", out _));
    }

    [Fact]
    public async Task CreateTeam_DispatchesThroughCommonToolDispatcherWithoutRpcSetup()
    {
        var thread = await CreateThreadAsync("desktop");
        var planning = new ToolPlanningContext(
            thread.Id,
            null,
            _workspace,
            "agent",
            null,
            [],
            7,
            ToolPlanningThreadKind.UserTopLevel);
        var snapshot = await new EffectiveToolSnapshotBuilder().BuildAsync([_source], planning);
        var providerName = snapshot.ProviderCallNames[new ToolName("teams", "CreateTeam")];

        var result = await new ToolDispatcher().DispatchProviderCallAsync(
            snapshot,
            providerName,
            new JsonObject
            {
                ["title"] = "Native lifecycle",
                ["prompt"] = "Run without opening the Teams RPC surface."
            },
            new ToolInvocationRequest(
                thread.Id,
                null,
                "call_native",
                ToolInvocationAudience.Model));

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal("Team mission started.", result.Content);
        Assert.Equal("Native lifecycle", result.StructuredContent?.GetProperty("title").GetString());
        var view = await _service.ViewTeamAsync(
            _sessions,
            Path.Combine(_workspace, ".craft"),
            CancellationToken.None);
        Assert.Equal("Native lifecycle", Assert.Single(view.Missions).Title);
    }

    [Fact]
    public async Task MissionThreads_ReceiveExactRoleMatrices()
    {
        var created = await _service.CreateMissionAsync(
            _sessions,
            _workspace,
            Path.Combine(_workspace, ".craft"),
            new TeamsMissionCreateParams { Title = "Matrix", Prompt = "Build matrices." },
            CancellationToken.None);
        var leader = Assert.Single(created.Team.MissionThreads);
        var leaderTools = await GetRegistrationsAsync(leader.ThreadId, ToolPlanningThreadKind.ModuleManaged);

        Assert.Equal(
            ["AssignTask", "CreateMissionPlan", "ListTeamMembers", "MarkMissionDone", "ReadMemberStatus", "ReadMissionState", "SendMessage"],
            leaderTools.Select(item => item.Definition.Name.Name).Order(StringComparer.Ordinal));
        Assert.All(leaderTools, item => Assert.Equal(ToolExposure.Direct, item.Exposure));
        Assert.True(leaderTools.Single(item => item.Definition.Name.Name == "ListTeamMembers").Definition.PolicyHints.ReadOnly);
        Assert.False(leaderTools.Single(item => item.Definition.Name.Name == "AssignTask").Definition.PolicyHints.ReadOnly);

        var planning = new ToolPlanningContext(
            leader.ThreadId,
            null,
            _workspace,
            "agent",
            null,
            [],
            8,
            ToolPlanningThreadKind.ModuleManaged);
        var snapshot = await new EffectiveToolSnapshotBuilder().BuildAsync([_source], planning);
        var assignProviderName = snapshot.ProviderCallNames[new ToolName("teams", "AssignTask")];
        var result = await new ToolDispatcher().DispatchProviderCallAsync(
            snapshot,
            assignProviderName,
            new JsonObject
            {
                ["assignee"] = "builder",
                ["title"] = "Implement",
                ["prompt"] = "Implement native tools.",
                ["kind"] = "work",
                ["requiresLeaderSynthesis"] = false
            },
            new ToolInvocationRequest(
                leader.ThreadId,
                null,
                "call_assign",
                ToolInvocationAudience.Model));
        Assert.True(result.Success, result.Error?.Message);

        var view = await _service.ViewTeamAsync(_sessions, Path.Combine(_workspace, ".craft"), CancellationToken.None);
        var builder = Assert.Single(view.MissionThreads, item => item.MemberId == "builder");
        var teammateTools = await GetRegistrationsAsync(builder.ThreadId, ToolPlanningThreadKind.ModuleManaged);
        Assert.Equal(
            ["ListTeamMembers", "MarkTaskDone", "PublishArtifact", "ReadMemberStatus", "ReadMissionState", "ReportProgress", "SendMessage"],
            teammateTools.Select(item => item.Definition.Name.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task BindingLease_RejectsCrossThreadInvocation()
    {
        var thread = await CreateThreadAsync("desktop");
        var registration = Assert.Single(await GetRegistrationsAsync(thread.Id, ToolPlanningThreadKind.UserTopLevel));

        var lease = await registration.Binding.Lease.CheckAsync(new ToolInvocationContext(
            "other-thread",
            null,
            "call",
            ToolInvocationAudience.Model,
            registration.Definition.Name,
            registration.Definition.Id,
            registration.Binding.Id,
            registration.Binding.Revision,
            DateTimeOffset.UtcNow));

        Assert.False(lease.IsAvailable);
        Assert.Equal(ToolErrorCodes.Unavailable, lease.Error?.Code);
    }

    [Fact]
    public async Task CorruptMissionThread_DoesNotFallBackToCreateTeam()
    {
        var created = await _service.CreateMissionAsync(
            _sessions,
            _workspace,
            Path.Combine(_workspace, ".craft"),
            new TeamsMissionCreateParams { Title = "Corrupt", Prompt = "Reject corrupt membership." },
            CancellationToken.None);
        var leader = Assert.Single(created.Team.MissionThreads);
        var statePath = Path.Combine(_workspace, ".craft", "teams", "state.json");
        var state = JsonNode.Parse(await File.ReadAllTextAsync(statePath))!.AsObject();
        state["missionThreads"]!.AsArray()[0]!["memberId"] = "unknown-member";
        await File.WriteAllTextAsync(statePath, state.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        var registrations = await GetRegistrationsAsync(leader.ThreadId, ToolPlanningThreadKind.UserTopLevel);

        Assert.Empty(registrations);
    }

    private Task<SessionThread> CreateThreadAsync(string channel) =>
        _sessions.CreateThreadAsync(
            new SessionIdentity
            {
                WorkspacePath = _workspace,
                ChannelName = channel,
                ChannelContext = Guid.NewGuid().ToString("N"),
                UserId = "user"
            },
            new ThreadConfiguration { Mode = "agent" });

    private async Task<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
        string threadId,
        ToolPlanningThreadKind kind) =>
        await _source.GetRegistrationsAsync(
            new ToolPlanningContext(threadId, null, _workspace, "agent", null, [], 1, kind));

    private static ValueTask<ToolExecutionResult> InvokeAsync(
        ToolRegistration registration,
        string threadId,
        JsonObject arguments) =>
        registration.Binding.Runtime.InvokeAsync(
            new ToolInvocationContext(
                threadId,
                null,
                "call",
                ToolInvocationAudience.Model,
                registration.Definition.Name,
                registration.Definition.Id,
                registration.Binding.Id,
                registration.Binding.Revision,
                DateTimeOffset.UtcNow),
            arguments);
}
