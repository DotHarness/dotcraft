using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.AppBinding;
using DotCraft.Configuration;
using DotCraft.Plugins;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Teams;
using DotCraft.Tests.Sessions.Protocol.AppServer;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Teams;

public sealed class TeamsServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"teams_{Guid.NewGuid():N}");
    private readonly string _workspaceCraftPath;
    private readonly ThreadStore _threadStore;
    private readonly TestableSessionService _sessionService;
    private readonly TeamsService _teamsService;
    private readonly AppBindingService _appBindingService;

    public TeamsServiceTests()
    {
        _workspaceCraftPath = Path.Combine(_tempRoot, ".craft");
        Directory.CreateDirectory(_workspaceCraftPath);
        _threadStore = new ThreadStore(_tempRoot);
        _sessionService = new TestableSessionService(_threadStore);
        _teamsService = new TeamsService();
        _appBindingService = new AppBindingService([_teamsService]);
        _teamsService.SetRuntimeServices(_appBindingService, _sessionService);
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
    public void ProtocolExtension_ExposesTeamRpcNamesOnly()
    {
        var extension = new TeamsProtocolExtension(_teamsService, _appBindingService);

        Assert.Contains("teams/team/view", extension.Methods);
        Assert.Contains("teams/team/enable", extension.Methods);
        Assert.DoesNotContain("teams/office/view", extension.Methods);
        Assert.DoesNotContain("teams/office/enable", extension.Methods);
    }

    [Fact]
    public async Task ProtocolExtension_RequiresAgentTeamsPluginBeforeTeamRpc()
    {
        var config = new AppConfig();
        var monitor = new AppConfigMonitor(config);
        var teamsService = new TeamsService(monitor);
        var appBindingService = new AppBindingService([teamsService]);
        teamsService.SetRuntimeServices(appBindingService, _sessionService);
        var extension = new TeamsProtocolExtension(teamsService, appBindingService);
        using var harness = new AppServerTestHarness(
            protocolExtensions: [extension],
            workspaceCraftPath: _workspaceCraftPath,
            appConfigMonitor: monitor,
            appBindingService: appBindingService,
            builtInPluginSourceRoots: [BundledPluginSourceRoot()]);
        await harness.InitializeAsync(configChange: true);

        await harness.ExecuteRequestAsync(harness.BuildRequest("teams/team/view", new { }));
        using (var blocked = await harness.Transport.ReadNextSentAsync())
        {
            AppServerTestHarness.AssertIsErrorResponse(blocked, AppServerErrors.MethodNotFoundCode);
        }

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.PluginInstall, new { id = PluginIds.AgentTeams }));
        using (var installed = await harness.Transport.ReadNextSentAsync())
        {
            AppServerTestHarness.AssertIsSuccessResponse(installed);
        }
        harness.Transport.DrainSent();

        await harness.ExecuteRequestAsync(harness.BuildRequest("teams/team/view", new { }));
        using (var visible = await harness.Transport.ReadNextSentAsync())
        {
            AppServerTestHarness.AssertIsSuccessResponse(visible);
            Assert.False(visible.RootElement.GetProperty("result").GetProperty("team").GetProperty("enabled").GetBoolean());
        }

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.PluginSetEnabled, new { id = PluginIds.AgentTeams, enabled = false }));
        using (var disabled = await harness.Transport.ReadNextSentAsync())
        {
            AppServerTestHarness.AssertIsSuccessResponse(disabled);
        }
        harness.Transport.DrainSent();

        await harness.ExecuteRequestAsync(harness.BuildRequest("teams/team/view", new { }));
        using var blockedAfterDisable = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(blockedAfterDisable, AppServerErrors.MethodNotFoundCode);
    }

    [Fact]
    public async Task AppList_ManagedTeamsVisibleOnlyOnAllowedSurfacesWhenPluginEnabled()
    {
        var config = new AppConfig();
        var monitor = new AppConfigMonitor(config);
        var teamsService = new TeamsService(monitor);
        var appBindingService = new AppBindingService([teamsService]);
        teamsService.SetRuntimeServices(appBindingService, _sessionService);
        using var harness = new AppServerTestHarness(
            protocolExtensions:
            [
                new TeamsProtocolExtension(teamsService, appBindingService),
                new AppBindingProtocolExtension(appBindingService, monitor, builtInPluginSourceRoots: [BundledPluginSourceRoot()])
            ],
            workspaceCraftPath: _workspaceCraftPath,
            appConfigMonitor: monitor,
            appBindingService: appBindingService,
            builtInPluginSourceRoots: [BundledPluginSourceRoot()]);
        await harness.InitializeAsync(configChange: true);
        harness.Transport.DrainSent();

        using (var beforeInstall = await AppListAsync(harness, "threadBinding"))
        {
            AppServerTestHarness.AssertIsSuccessResponse(beforeInstall);
            Assert.DoesNotContain(
                beforeInstall.RootElement.GetProperty("result").GetProperty("apps").EnumerateArray(),
                app => app.GetProperty("appId").GetString() == TeamsConstants.AppId);
        }

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.PluginInstall, new { id = PluginIds.AgentTeams }));
        using (var installed = await harness.Transport.ReadNextSentAsync())
        {
            AppServerTestHarness.AssertIsSuccessResponse(installed);
        }
        harness.Transport.DrainSent();

        using (var pluginDetail = await AppListAsync(harness, "pluginDetail"))
        {
            AppServerTestHarness.AssertIsSuccessResponse(pluginDetail);
            Assert.DoesNotContain(
                pluginDetail.RootElement.GetProperty("result").GetProperty("apps").EnumerateArray(),
                app => app.GetProperty("appId").GetString() == TeamsConstants.AppId);
        }

        using (var threadBinding = await AppListAsync(harness, "threadBinding"))
        {
            AppServerTestHarness.AssertIsSuccessResponse(threadBinding);
            var app = Assert.Single(
                threadBinding.RootElement.GetProperty("result").GetProperty("apps").EnumerateArray(),
                item => item.GetProperty("appId").GetString() == TeamsConstants.AppId);
            Assert.Equal("Agent Teams", app.GetProperty("displayName").GetString());
            Assert.True(app.GetProperty("managed").GetBoolean());
            Assert.False(app.GetProperty("requiresExternalConnection").GetBoolean());
            Assert.Equal("connected", app.GetProperty("connectionState").GetString());
            Assert.StartsWith("data:image/svg+xml;base64,", app.GetProperty("icon").GetString(), StringComparison.Ordinal);
            Assert.Equal("CreateTeam", Assert.Single(app.GetProperty("toolCatalog").EnumerateArray()).GetProperty("name").GetString());
        }

        var originThread = await harness.Service.CreateThreadAsync(new SessionIdentity
        {
            WorkspacePath = _tempRoot,
            ChannelName = "desktop",
            UserId = "user"
        });
        appBindingService.EnsureManagedBinding(
            _workspaceCraftPath,
            originThread.Id,
            TeamsConstants.AppId,
            "user",
            "origin-grant",
            ["mission.manage"],
            teamsService.GetToolSpecsForSurface(ManagedAppBindingToolSurfaces.ThreadBinding),
            teamsService.GetCatalogDescriptor(AppBindingCatalogSurfaces.ThreadBinding));
        Assert.Single(appBindingService.CreateRuntimeToolsForThread(originThread, new HashSet<string>(StringComparer.Ordinal)));
        var enabledCatalog = appBindingService.DiscoverCatalog(
            monitor.Current,
            _tempRoot,
            _workspaceCraftPath,
            builtInPluginSourceRoots: [BundledPluginSourceRoot()]);
        var refreshedBeforeDisable = Assert.Single(appBindingService.RefreshBindings(
            enabledCatalog,
            _workspaceCraftPath,
            new ThreadAppBindingRefreshParams { ThreadId = originThread.Id }).Bindings);
        Assert.Equal("active", refreshedBeforeDisable.State);
        var activeBinding = Assert.Single(appBindingService.ListThreadBindings(enabledCatalog, _workspaceCraftPath, originThread.Id, includeRevoked: false).Bindings);
        Assert.Equal("active", activeBinding.State);
        Assert.Equal("connected", activeBinding.ConnectionState);

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.PluginSetEnabled, new { id = PluginIds.AgentTeams, enabled = false }));
        using (var disabled = await harness.Transport.ReadNextSentAsync())
        {
            AppServerTestHarness.AssertIsSuccessResponse(disabled);
        }
        harness.Transport.DrainSent();

        var disabledCatalog = appBindingService.DiscoverCatalog(
            monitor.Current,
            _tempRoot,
            _workspaceCraftPath,
            builtInPluginSourceRoots: [BundledPluginSourceRoot()]);
        var disabledBinding = Assert.Single(appBindingService.ListThreadBindings(disabledCatalog, _workspaceCraftPath, originThread.Id, includeRevoked: false).Bindings);
        Assert.Equal("offline", disabledBinding.State);
        Assert.Empty(appBindingService.CreateRuntimeToolsForThread(originThread, new HashSet<string>(StringComparer.Ordinal)));

        using var afterDisable = await AppListAsync(harness, "threadBinding");
        AppServerTestHarness.AssertIsSuccessResponse(afterDisable);
        Assert.DoesNotContain(
            afterDisable.RootElement.GetProperty("result").GetProperty("apps").EnumerateArray(),
            app => app.GetProperty("appId").GetString() == TeamsConstants.AppId);
    }

    [Fact]
    public async Task EnableTeam_CreatesMemberRosterWithoutWorkThreads()
    {
        var invalidatedThreads = new HashSet<string>(StringComparer.Ordinal);
        void OnAppContextChanged(string threadId) => invalidatedThreads.Add(threadId);
        _appBindingService.AppContextBlocksChanged += OnAppContextChanged;
        try
        {
            var view = await _teamsService.EnableTeamAsync(
                _appBindingService,
                _sessionService,
                _tempRoot,
                _workspaceCraftPath,
                CancellationToken.None);

            Assert.True(view.Team.Enabled);
            Assert.Equal("default", view.Team.TeamId);
            Assert.Equal(new[] { "leader", "explorer", "builder", "reviewer", "operator" }, view.Members.Select(m => m.MemberId).ToList());
            Assert.Equal("team-leader", view.Members.Single(member => member.MemberId == "leader").AgentProfileId);
            Assert.Equal("team-explorer", view.Members.Single(member => member.MemberId == "explorer").AgentProfileId);
            Assert.Equal("team-builder", view.Members.Single(member => member.MemberId == "builder").AgentProfileId);
            Assert.Equal("team-reviewer", view.Members.Single(member => member.MemberId == "reviewer").AgentProfileId);
            Assert.Equal("team-operator", view.Members.Single(member => member.MemberId == "operator").AgentProfileId);
            Assert.Equal("#4f7cf6", view.Members.Single(member => member.MemberId == "leader").AvatarAccent);
            Assert.Empty(view.MissionThreads);
            var stateFile = Path.Combine(_workspaceCraftPath, "teams", "state.json");
            Assert.True(File.Exists(stateFile));
            using (var state = JsonDocument.Parse(File.ReadAllText(stateFile)))
            {
                Assert.True(state.RootElement.TryGetProperty("team", out var team));
                Assert.Equal("default", team.GetProperty("teamId").GetString());
                Assert.False(state.RootElement.TryGetProperty("office", out _));
                Assert.Empty(state.RootElement.GetProperty("missionThreads").EnumerateArray());
                Assert.Equal("team-leader", state.RootElement.GetProperty("members").EnumerateArray()
                    .Single(member => member.GetProperty("memberId").GetString() == "leader")
                    .GetProperty("agentProfileId").GetString());
            }

            foreach (var member in view.Members)
            {
                Assert.Empty(member.ThreadId);
                Assert.Empty(member.BindingId);
                Assert.Empty(member.GrantId);
            }

            Assert.Empty(invalidatedThreads);
        }
        finally
        {
            _appBindingService.AppContextBlocksChanged -= OnAppContextChanged;
        }
    }

    [Fact]
    public async Task CreateMission_UsesLeaderAgentProfileForMissionThread()
    {
        var created = await _teamsService.CreateMissionAsync(
            _appBindingService,
            _sessionService,
            _tempRoot,
            _workspaceCraftPath,
            new TeamsMissionCreateParams
            {
                Title = "Profile-backed mission",
                Prompt = "Use the leader profile."
            },
            CancellationToken.None);

        var leaderThreadView = Assert.Single(created.Team.MissionThreads, thread => thread.MemberId == "leader");
        var leaderThread = await _sessionService.GetThreadAsync(leaderThreadView.ThreadId);
        Assert.NotNull(leaderThread.Configuration);
        var config = leaderThread.Configuration!;

        Assert.Equal("team-leader", config.AgentProfileId);
        Assert.Equal("builtIn", config.AgentProfileSource);
        Assert.StartsWith("sha256:", config.AgentProfileFingerprint);
        Assert.Equal("keep", config.TeamsPolicy?.ReservedTools);
        Assert.Contains("You coordinate the mission", config.RoleInstructions, StringComparison.Ordinal);
        Assert.Contains("You are the DotCraft Team Leader", config.RoleInstructions, StringComparison.Ordinal);
        Assert.True(
            config.RoleInstructions!.IndexOf("You coordinate the mission", StringComparison.Ordinal)
            < config.RoleInstructions.IndexOf("You are the DotCraft Team Leader", StringComparison.Ordinal));

        var leader = Assert.Single(created.Team.Members, member => member.MemberId == "leader");
        Assert.Equal("team-leader", leader.AgentProfile?.ActiveId);
        Assert.Equal("builtIn", leader.AgentProfile?.Source);

        await _teamsService.EnableTeamAsync(
            _appBindingService,
            _sessionService,
            _tempRoot,
            _workspaceCraftPath,
            CancellationToken.None);
        var repairedLeaderThread = await _sessionService.GetThreadAsync(leaderThreadView.ThreadId);
        Assert.Contains("You coordinate the mission", repairedLeaderThread.Configuration?.RoleInstructions, StringComparison.Ordinal);
        Assert.Contains("You are the DotCraft Team Leader", repairedLeaderThread.Configuration?.RoleInstructions, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ViewTeam_ReportsMissingMemberProfileWithFallbackDiagnostics()
    {
        await _teamsService.EnableTeamAsync(
            _appBindingService,
            _sessionService,
            _tempRoot,
            _workspaceCraftPath,
            CancellationToken.None);
        var statePath = Path.Combine(_workspaceCraftPath, "teams", "state.json");
        var stateJson = File.ReadAllText(statePath).Replace("\"agentProfileId\": \"team-reviewer\"", "\"agentProfileId\": \"missing-reviewer\"");
        File.WriteAllText(statePath, stateJson);

        var view = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);

        var reviewer = Assert.Single(view.Members, member => member.MemberId == "reviewer");
        Assert.Equal("missing-reviewer", reviewer.AgentProfileId);
        Assert.Equal("missing-reviewer", reviewer.AgentProfile?.RequestedId);
        Assert.Equal("team-reviewer", reviewer.AgentProfile?.ActiveId);
        Assert.True(reviewer.AgentProfile?.FallbackUsed);
        Assert.Contains(reviewer.AgentProfile!.Diagnostics, diagnostic => diagnostic.Code == "AgentProfileMissing");
    }

    [Fact]
    public void ToolSpecs_AreGeneratedFromDynamicToolAttributesWithoutSchemaDrift()
    {
        Assert.Equal(
            [
                "CreateMissionPlan",
                "AssignTask",
                "ListTeamMembers",
                "ReadMissionState",
                "ReadMemberStatus",
                "SendMessage",
                "ReportProgress",
                "PublishArtifact",
                "MarkTaskDone",
                "MarkMissionDone"
            ],
            _teamsService.ToolSpecs.Select(spec => spec.Name));

        Assert.All(_teamsService.ToolSpecs, spec =>
        {
            Assert.Equal(TeamsConstants.ToolNamespace, spec.Namespace);
            Assert.False(spec.DeferLoading.GetValueOrDefault());
            Assert.True(
                PluginFunctionSchemaValidator.TryValidateSchema(spec.InputSchema!, out var message),
                message);
        });

        Assert.Equal(["assignee", "title", "prompt"], Required("AssignTask"));
        Assert.Equal(["summary"], Required("ReportProgress"));
        Assert.Equal(["title", "pathOrUri"], Required("PublishArtifact"));
        Assert.Equal(["summary"], Required("MarkTaskDone"));
        Assert.Equal(["finalResponse"], Required("MarkMissionDone"));
        Assert.Equal(["to", "message"], Required("SendMessage"));
        Assert.Equal("array", Property("AssignTask", "dependsOnTaskIds")["type"]!.GetValue<string>());
        Assert.Equal("string", ((JsonObject)Property("AssignTask", "dependsOnTaskIds")["items"]!)["type"]!.GetValue<string>());
        Assert.False(HasProperty("AssignTask", "missionId"));
        Assert.False(HasProperty("ReportProgress", "taskId"));
        Assert.False(HasProperty("SendMessage", "requiresAction"));
        Assert.False(HasProperty("PublishArtifact", "metadata"));
    }

    [Fact]
    public void ThreadBindingToolSurface_OnlyExposesCreateTeam()
    {
        var descriptor = _teamsService.GetCatalogDescriptor(AppBindingCatalogSurfaces.ThreadBinding);
        var tools = _teamsService.GetToolSpecsForSurface(ManagedAppBindingToolSurfaces.ThreadBinding);

        Assert.Equal("Agent Teams", descriptor.DisplayName);
        Assert.Equal(["CreateTeam"], descriptor.ToolCatalog.Select(tool => tool.Name).ToArray());
        var tool = Assert.Single(tools);
        Assert.Equal("CreateTeam", tool.Name);
        Assert.Equal(["title", "prompt"], tool.InputSchema!["required"] is JsonArray required
            ? required.Select(item => item!.GetValue<string>()).ToArray()
            : []);
        Assert.DoesNotContain(_teamsService.ToolSpecs, spec => spec.Name == "CreateTeam");
    }

    [Fact]
    public async Task CreateTeam_StartsMissionFromOrdinaryThreadWithOnlyCreateTeamTool()
    {
        var originThread = await _sessionService.CreateThreadAsync(new SessionIdentity
        {
            WorkspacePath = _tempRoot,
            ChannelName = "desktop",
            UserId = "user"
        });
        var binding = _appBindingService.EnsureManagedBinding(
            _workspaceCraftPath,
            originThread.Id,
            TeamsConstants.AppId,
            "user",
            "origin-grant",
            ["mission.manage"],
            _teamsService.GetToolSpecsForSurface(ManagedAppBindingToolSurfaces.ThreadBinding),
            _teamsService.GetCatalogDescriptor(AppBindingCatalogSurfaces.ThreadBinding));

        var originBindingTools = ReadBindingToolNames(binding.BindingId);
        Assert.Equal(["CreateTeam"], originBindingTools.direct);
        Assert.Empty(originBindingTools.deferred);

        var created = await _teamsService.InvokeToolAsync(
            new ManagedAppBindingToolCallContext(
                _workspaceCraftPath,
                _tempRoot,
                binding.BindingId,
                originThread.Id,
                "turn_origin",
                "call_create_team",
                TeamsConstants.AppId,
                "origin-grant",
                "CreateTeam")
            {
                AppBindingService = _appBindingService,
                SessionService = _sessionService
            },
            new JsonObject
            {
                ["title"] = "External mission",
                ["prompt"] = "Have the Team do this asynchronously."
            },
            CancellationToken.None);

        Assert.True(created.Success, created.ErrorMessage);
        var structured = Assert.IsType<JsonObject>(created.StructuredResult);
        Assert.False(string.IsNullOrWhiteSpace(structured["missionId"]?.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(structured["leaderThreadId"]?.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(structured["queuedInputId"]?.GetValue<string>()));
        Assert.Equal("planning", structured["status"]?.GetValue<string>());

        var view = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var mission = Assert.Single(view.Missions);
        Assert.Equal(originThread.Id, mission.OriginThreadId);
        Assert.Equal(binding.BindingId, mission.OriginBindingId);

        var leaderThread = Assert.Single(view.MissionThreads, thread => thread.MemberId == "leader");
        var leaderBindingTools = ReadBindingToolNames(leaderThread.BindingId);
        Assert.Contains("MarkMissionDone", leaderBindingTools.direct);
        Assert.DoesNotContain("CreateTeam", leaderBindingTools.direct);
    }

    [Fact]
    public async Task MarkMissionDone_StartsCompletionNotificationWhenOriginThreadIsIdle()
    {
        var originThread = await _sessionService.CreateThreadAsync(new SessionIdentity
        {
            WorkspacePath = _tempRoot,
            ChannelName = "desktop",
            UserId = "user"
        });
        var originBinding = _appBindingService.EnsureManagedBinding(
            _workspaceCraftPath,
            originThread.Id,
            TeamsConstants.AppId,
            "user",
            "origin-grant",
            ["mission.manage"],
            _teamsService.GetToolSpecsForSurface(ManagedAppBindingToolSurfaces.ThreadBinding),
            _teamsService.GetCatalogDescriptor(AppBindingCatalogSurfaces.ThreadBinding));
        var created = await _teamsService.CreateMissionAsync(
            _appBindingService,
            _sessionService,
            _tempRoot,
            _workspaceCraftPath,
            new TeamsMissionCreateParams
            {
                Title = "Return result",
                Prompt = "Return a final response to the origin thread."
            },
            CancellationToken.None,
            new TeamsMissionOrigin(originThread.Id, originBinding.BindingId));
        var leaderThread = Assert.Single(created.Team.MissionThreads, thread => thread.MemberId == "leader");

        var done = await _teamsService.InvokeToolAsync(
            new ManagedAppBindingToolCallContext(
                _workspaceCraftPath,
                _tempRoot,
                leaderThread.BindingId,
                leaderThread.ThreadId,
                "turn_leader",
                "call_done",
                TeamsConstants.AppId,
                leaderThread.GrantId,
                "MarkMissionDone")
            {
                AppBindingService = _appBindingService,
                SessionService = _sessionService
            },
            new JsonObject
            {
                ["finalResponse"] = "Final answer from the Team."
            },
            CancellationToken.None);

        Assert.True(done.Success, done.ErrorMessage);
        var updatedOrigin = await _sessionService.GetThreadAsync(originThread.Id);
        Assert.Empty(updatedOrigin.QueuedInputs);
        var started = _sessionService.LastStartedQueuedInput;
        Assert.NotNull(started);
        Assert.Equal(originThread.Id, started.ThreadId);
        Assert.Equal("team", started.TriggerKind);
        Assert.Equal(created.Mission.MissionId, started.TriggerRefId);
        Assert.Contains("Mission completed: Return result", started.TriggerLabel);
        Assert.Contains("Final answer from the Team.", started.DisplayText);
        Assert.Contains("mission.completed", Assert.Single(started.MaterializedInputParts).Text);
        var submitted = Assert.IsType<TextContent>(Assert.Single(_sessionService.LastSubmittedContent));
        Assert.Contains("mission.completed", submitted.Text, StringComparison.Ordinal);
        Assert.Contains("Final answer from the Team.", submitted.Text, StringComparison.Ordinal);

        var view = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var mission = Assert.Single(view.Missions);
        Assert.Equal(started.Id, mission.CompletionQueuedInputId);
        Assert.NotNull(mission.CompletionNotifiedAt);
    }

    [Fact]
    public async Task MarkMissionDone_LeavesCompletionQueuedWhenOriginThreadIsBusy()
    {
        var originThread = await _sessionService.CreateThreadAsync(new SessionIdentity
        {
            WorkspacePath = _tempRoot,
            ChannelName = "desktop",
            UserId = "user"
        });
        var originBinding = _appBindingService.EnsureManagedBinding(
            _workspaceCraftPath,
            originThread.Id,
            TeamsConstants.AppId,
            "user",
            "origin-grant",
            ["mission.manage"],
            _teamsService.GetToolSpecsForSurface(ManagedAppBindingToolSurfaces.ThreadBinding),
            _teamsService.GetCatalogDescriptor(AppBindingCatalogSurfaces.ThreadBinding));
        var created = await _teamsService.CreateMissionAsync(
            _appBindingService,
            _sessionService,
            _tempRoot,
            _workspaceCraftPath,
            new TeamsMissionCreateParams
            {
                Title = "Return later",
                Prompt = "Return a final response to the origin thread."
            },
            CancellationToken.None,
            new TeamsMissionOrigin(originThread.Id, originBinding.BindingId));
        originThread.Turns.Add(new SessionTurn
        {
            Id = "turn_origin_running",
            ThreadId = originThread.Id,
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        });
        var leaderThread = Assert.Single(created.Team.MissionThreads, thread => thread.MemberId == "leader");

        var done = await _teamsService.InvokeToolAsync(
            new ManagedAppBindingToolCallContext(
                _workspaceCraftPath,
                _tempRoot,
                leaderThread.BindingId,
                leaderThread.ThreadId,
                "turn_leader",
                "call_done",
                TeamsConstants.AppId,
                leaderThread.GrantId,
                "MarkMissionDone")
            {
                AppBindingService = _appBindingService,
                SessionService = _sessionService
            },
            new JsonObject
            {
                ["finalResponse"] = "Final answer from the Team."
            },
            CancellationToken.None);

        Assert.True(done.Success, done.ErrorMessage);
        var updatedOrigin = await _sessionService.GetThreadAsync(originThread.Id);
        var queued = Assert.Single(updatedOrigin.QueuedInputs);
        Assert.Equal("team", queued.TriggerKind);
        Assert.Equal(created.Mission.MissionId, queued.TriggerRefId);
        Assert.Contains("Mission completed: Return later", queued.TriggerLabel);
        Assert.Contains("Final answer from the Team.", queued.DisplayText);
        Assert.Contains("mission.completed", Assert.Single(queued.MaterializedInputParts).Text);

        var view = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var mission = Assert.Single(view.Missions);
        Assert.Equal(queued.Id, mission.CompletionQueuedInputId);
        Assert.NotNull(mission.CompletionNotifiedAt);
    }

    [Fact]
    public async Task EnableTeam_RepairsMissionThreadRoleInstructionsWithoutCreatingRosterThreads()
    {
        var created = await _teamsService.CreateMissionAsync(
            _appBindingService,
            _sessionService,
            _tempRoot,
            _workspaceCraftPath,
            new TeamsMissionCreateParams
            {
                Title = "Repair prompts",
                Prompt = "Repair the mission thread prompt boundary."
            },
            CancellationToken.None);
        var leaderMissionThread = Assert.Single(created.Team.MissionThreads, thread => thread.MemberId == "leader");
        await _sessionService.UpdateThreadConfigurationAsync(
            leaderMissionThread.ThreadId,
            new ThreadConfiguration
            {
                Mode = "agent",
                AgentInstructions = "legacy override",
                OverrideBasePrompt = true
            });

        var repaired = await _teamsService.EnableTeamAsync(
            _appBindingService,
            _sessionService,
            _tempRoot,
            _workspaceCraftPath,
            CancellationToken.None);

        var repairedLeaderThread = Assert.Single(repaired.MissionThreads, thread => thread.MemberId == "leader");
        Assert.Equal(leaderMissionThread.ThreadId, repairedLeaderThread.ThreadId);
        Assert.All(repaired.Members, member => Assert.Empty(member.ThreadId));
        var thread = await _sessionService.GetThreadAsync(repairedLeaderThread.ThreadId);
        Assert.Contains("DotCraft Team Leader", thread.Configuration?.RoleInstructions, StringComparison.Ordinal);
        Assert.Null(thread.Configuration?.AgentInstructions);
        Assert.False(thread.Configuration?.OverrideBasePrompt);
    }

    [Fact]
    public async Task AssignTask_CreatesTaskDispatchesMemberInputAndKeepsMailboxOutOfHistory()
    {
        var created = await _teamsService.CreateMissionAsync(
            _appBindingService,
            _sessionService,
            _tempRoot,
            _workspaceCraftPath,
            new TeamsMissionCreateParams
            {
                Title = "Ship M1",
                Prompt = "Turn the Teams plan into a working milestone."
            },
            CancellationToken.None);

        Assert.Equal("planning", created.Mission.Status);
        Assert.Equal("team", created.QueuedInput?.TriggerKind);
        Assert.Equal($"Mission: {created.Mission.Title}", created.QueuedInput?.TriggerLabel);
        Assert.Contains("<team-notification", Assert.Single(created.QueuedInput!.MaterializedInputParts).Text, StringComparison.Ordinal);
        Assert.DoesNotContain("<team-notification", created.QueuedInput.DisplayText, StringComparison.Ordinal);
        Assert.DoesNotContain("<team-notification", Assert.Single(created.QueuedInput.NativeInputParts).Text, StringComparison.Ordinal);
        Assert.Empty((await _sessionService.GetThreadAsync(created.Mission.LeaderThreadId)).Turns);
        var leaderMissionThread = Assert.Single(created.Team.MissionThreads, thread => thread.MemberId == "leader");
        Assert.Equal(created.Mission.MissionId, leaderMissionThread.MissionId);
        Assert.Equal(created.Mission.LeaderThreadId, leaderMissionThread.ThreadId);
        Assert.NotEmpty(leaderMissionThread.BindingId);
        Assert.NotEmpty(leaderMissionThread.GrantId);
        Assert.Contains(leaderMissionThread.ThreadId, _sessionService.RefreshedThreadAgents);
        var leaderThread = await _sessionService.GetThreadAsync(leaderMissionThread.ThreadId);
        Assert.Contains("DotCraft Team Leader", leaderThread.Configuration?.RoleInstructions, StringComparison.Ordinal);
        Assert.Contains("Team workflow", leaderThread.Configuration?.RoleInstructions, StringComparison.Ordinal);
        Assert.Null(leaderThread.Configuration?.AgentInstructions);
        Assert.False(leaderThread.Configuration?.OverrideBasePrompt);
        var leaderBindingTools = ReadBindingToolNames(leaderMissionThread.BindingId);
        Assert.Contains("AssignTask", leaderBindingTools.direct);
        Assert.Contains("SendMessage", leaderBindingTools.direct);
        Assert.DoesNotContain("WaitForTeam", leaderBindingTools.direct);
        Assert.DoesNotContain("SendMemberMessage", leaderBindingTools.direct);
        Assert.Contains("MarkMissionDone", leaderBindingTools.direct);
        Assert.DoesNotContain("MarkTaskDone", leaderBindingTools.direct);
        Assert.Empty(leaderBindingTools.deferred);
        Assert.Contains(
            _sessionService.LastSubmittedContent,
            content => content is TextContent text && text.Text.Contains("Mission created: Ship M1", StringComparison.Ordinal));

        var toolResult = await _teamsService.InvokeToolAsync(
            new ManagedAppBindingToolCallContext(
                _workspaceCraftPath,
                _tempRoot,
                leaderMissionThread.BindingId,
                leaderMissionThread.ThreadId,
                "turn_leader",
                "call_assign",
                TeamsConstants.AppId,
                leaderMissionThread.GrantId,
                "AssignTask"),
            new JsonObject
            {
                ["assignee"] = "builder",
                ["title"] = "Build the substrate",
                ["prompt"] = "Implement the managed runtime and dispatch substrate."
            },
            CancellationToken.None);

        Assert.True(toolResult.Success, toolResult.ErrorMessage);
        var view = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var task = Assert.Single(view.Tasks);
        Assert.Equal(created.Mission.MissionId, task.MissionId);
        Assert.Equal("t1", task.Alias);
        Assert.Equal("builder", task.AssigneeMemberId);
        Assert.Equal("running", task.Status);
        Assert.Equal("active", Assert.Single(view.Missions).Status);

        var builder = view.Members.Single(member => member.MemberId == "builder");
        Assert.Equal(task.TaskId, builder.CurrentTaskId);
        Assert.Equal("running", builder.Status);
        var builderMissionThread = Assert.Single(view.MissionThreads, thread => thread.MemberId == "builder");
        Assert.Equal(task.TaskId, builderMissionThread.CurrentTaskId);
        Assert.NotEmpty(builderMissionThread.ThreadId);
        Assert.Contains(builderMissionThread.ThreadId, _sessionService.RefreshedThreadAgents);
        var builderThread = await _sessionService.GetThreadAsync(builderMissionThread.ThreadId);
        Assert.Contains("mission-scoped teammate thread", builderThread.Configuration?.RoleInstructions, StringComparison.Ordinal);
        Assert.Contains("Builder", builderThread.Configuration?.RoleInstructions, StringComparison.Ordinal);
        Assert.Null(builderThread.Configuration?.AgentInstructions);
        Assert.False(builderThread.Configuration?.OverrideBasePrompt);
        var builderBindingTools = ReadBindingToolNames(builderMissionThread.BindingId);
        Assert.Contains("ReportProgress", builderBindingTools.direct);
        Assert.Contains("PublishArtifact", builderBindingTools.direct);
        Assert.Contains("MarkTaskDone", builderBindingTools.direct);
        Assert.Contains("SendMessage", builderBindingTools.direct);
        Assert.DoesNotContain("AssignTask", builderBindingTools.direct);
        Assert.DoesNotContain("WaitForTeam", builderBindingTools.direct);
        Assert.DoesNotContain("SendMemberMessage", builderBindingTools.direct);
        Assert.DoesNotContain("MarkMissionDone", builderBindingTools.direct);
        Assert.Empty(builderBindingTools.deferred);
        Assert.Empty(builderThread.Turns);
        Assert.Empty(builderThread.QueuedInputs);
        Assert.Contains(
            _sessionService.LastSubmittedContent,
            content => content is TextContent text && text.Text.Contains("Team task assigned: Build the substrate", StringComparison.Ordinal));

        var digest = Assert.Single(view.MailboxDigests, item => item.MemberId == "builder");
        Assert.Contains("New task: Build the substrate", digest.Content, StringComparison.Ordinal);
        var blocks = _appBindingService
            .ListThreadContextBlocks(_workspaceCraftPath, builderMissionThread.ThreadId, includeInactive: false)
            .Blocks;
        Assert.Equal(3, blocks.Count);
        Assert.Contains(blocks, block => block.Kind == AppContextBlockKinds.Role);
        Assert.Contains(blocks, block => block.Kind == AppContextBlockKinds.Mission);
        Assert.Contains(blocks, block => block.Kind == AppContextBlockKinds.Policy);
        Assert.DoesNotContain(blocks, block => block.BlockId == "team-state");
        var openedTask = _teamsService.OpenMemberThread(
            _workspaceCraftPath,
            new TeamsMemberOpenThreadParams { TaskId = task.TaskId });
        Assert.Equal(builderMissionThread.ThreadId, openedTask.ThreadId);
        var openedLeader = _teamsService.OpenMemberThread(
            _workspaceCraftPath,
            new TeamsMemberOpenThreadParams { MissionId = created.Mission.MissionId, MemberId = "leader" });
        Assert.Equal(leaderMissionThread.ThreadId, openedLeader.ThreadId);
        Assert.Throws<AppServerException>(() => _teamsService.OpenMemberThread(
            _workspaceCraftPath,
            new TeamsMemberOpenThreadParams { MemberId = "builder" }));
        Assert.Empty(builderThread.Turns);
        AssertAppBindingAuditContains("binding.threadInput.enqueue");
    }

    [Fact]
    public async Task TaskLifecycle_DoesNotChangeMissionThreadAppContextPrompt()
    {
        var created = await _teamsService.CreateMissionAsync(
            _appBindingService,
            _sessionService,
            _tempRoot,
            _workspaceCraftPath,
            new TeamsMissionCreateParams
            {
                Title = "Keep prompts stable",
                Prompt = "Exercise task lifecycle state without changing app context."
            },
            CancellationToken.None);
        var leaderMissionThread = Assert.Single(created.Team.MissionThreads, thread => thread.MemberId == "leader");

        var assigned = await _teamsService.InvokeToolAsync(
            new ManagedAppBindingToolCallContext(
                _workspaceCraftPath,
                _tempRoot,
                leaderMissionThread.BindingId,
                leaderMissionThread.ThreadId,
                "turn_leader",
                "call_assign",
                TeamsConstants.AppId,
                leaderMissionThread.GrantId,
                "AssignTask"),
            new JsonObject
            {
                ["assignee"] = "builder",
                ["title"] = "Build stable prompt proof",
                ["prompt"] = "Report progress, then complete this task."
            },
            CancellationToken.None);
        Assert.True(assigned.Success, assigned.ErrorMessage);

        var viewAfterAssign = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var task = Assert.Single(viewAfterAssign.Tasks);
        var builderMissionThread = Assert.Single(viewAfterAssign.MissionThreads, thread => thread.MemberId == "builder");
        var leaderPromptBefore = _appBindingService.BuildAppContextPromptSection(_workspaceCraftPath, leaderMissionThread.ThreadId);
        var builderPromptBefore = _appBindingService.BuildAppContextPromptSection(_workspaceCraftPath, builderMissionThread.ThreadId);
        Assert.NotNull(leaderPromptBefore);
        Assert.NotNull(builderPromptBefore);
        Assert.DoesNotContain("Version:", leaderPromptBefore, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdatedAt:", leaderPromptBefore, StringComparison.Ordinal);
        Assert.DoesNotContain("ExpiresAt:", leaderPromptBefore, StringComparison.Ordinal);
        Assert.DoesNotContain("Mission status:", leaderPromptBefore, StringComparison.Ordinal);
        Assert.DoesNotContain("Mission status:", builderPromptBefore, StringComparison.Ordinal);

        var invalidatedThreadIds = new List<string>();
        void OnAppContextChanged(string threadId) => invalidatedThreadIds.Add(threadId);
        _appBindingService.AppContextBlocksChanged += OnAppContextChanged;
        try
        {
            var progress = await _teamsService.InvokeToolAsync(
                new ManagedAppBindingToolCallContext(
                    _workspaceCraftPath,
                    _tempRoot,
                    builderMissionThread.BindingId,
                    builderMissionThread.ThreadId,
                    "turn_builder",
                    "call_progress",
                    TeamsConstants.AppId,
                    builderMissionThread.GrantId,
                    "ReportProgress"),
                new JsonObject
                {
                    ["summary"] = "Halfway done.",
                    ["status"] = "running"
                },
                CancellationToken.None);
            Assert.True(progress.Success, progress.ErrorMessage);

            var done = await _teamsService.InvokeToolAsync(
                new ManagedAppBindingToolCallContext(
                    _workspaceCraftPath,
                    _tempRoot,
                    builderMissionThread.BindingId,
                    builderMissionThread.ThreadId,
                    "turn_builder",
                    "call_done",
                    TeamsConstants.AppId,
                    builderMissionThread.GrantId,
                    "MarkTaskDone"),
                new JsonObject
                {
                    ["summary"] = "Stable prompt proof complete."
                },
                CancellationToken.None);
            Assert.True(done.Success, done.ErrorMessage);
        }
        finally
        {
            _appBindingService.AppContextBlocksChanged -= OnAppContextChanged;
        }

        Assert.Empty(invalidatedThreadIds);
        Assert.Equal(leaderPromptBefore, _appBindingService.BuildAppContextPromptSection(_workspaceCraftPath, leaderMissionThread.ThreadId));
        Assert.Equal(builderPromptBefore, _appBindingService.BuildAppContextPromptSection(_workspaceCraftPath, builderMissionThread.ThreadId));
        var finalTask = Assert.Single((await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None)).Tasks);
        Assert.Equal(task.TaskId, finalTask.TaskId);
        Assert.Equal(TeamTaskStatuses.Done, finalTask.Status);
    }

    [Fact]
    public async Task MarkTaskDone_AwaitsLeaderReviewBeforeMissionArchive()
    {
        var created = await _teamsService.CreateMissionAsync(
            _appBindingService,
            _sessionService,
            _tempRoot,
            _workspaceCraftPath,
            new TeamsMissionCreateParams
            {
                Title = "Research launch plan",
                Prompt = "Create a launch plan and verify it."
            },
            CancellationToken.None);

        var leaderMissionThread = Assert.Single(created.Team.MissionThreads, thread => thread.MemberId == "leader");
        var assigned = await _teamsService.InvokeToolAsync(
            new ManagedAppBindingToolCallContext(
                _workspaceCraftPath,
                _tempRoot,
                leaderMissionThread.BindingId,
                leaderMissionThread.ThreadId,
                "turn_leader",
                "call_assign",
                TeamsConstants.AppId,
                leaderMissionThread.GrantId,
                "AssignTask"),
            new JsonObject
            {
                ["assignee"] = "builder",
                ["title"] = "Draft the plan",
                ["prompt"] = "Draft the launch plan."
            },
            CancellationToken.None);

        Assert.True(assigned.Success, assigned.ErrorMessage);
        await _teamsService.HandleThreadRuntimeSignalAsync(
            _workspaceCraftPath,
            leaderMissionThread.ThreadId,
            SessionThreadRuntimeSignal.TurnCompleted,
            CancellationToken.None);

        var activeArchiveError = await Assert.ThrowsAsync<AppServerException>(() =>
            _teamsService.ArchiveMissionAsync(
                _sessionService,
                _workspaceCraftPath,
                new TeamsMissionArchiveParams { MissionId = created.Mission.MissionId },
                CancellationToken.None));
        Assert.Equal(AppServerErrors.InvalidParamsCode, activeArchiveError.Code);

        var activeView = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var task = Assert.Single(activeView.Tasks);
        var builderMissionThread = Assert.Single(activeView.MissionThreads, thread => thread.MemberId == "builder");
        var done = await _teamsService.InvokeToolAsync(
            new ManagedAppBindingToolCallContext(
                _workspaceCraftPath,
                _tempRoot,
                builderMissionThread.BindingId,
                builderMissionThread.ThreadId,
                "turn_builder",
                "call_done",
                TeamsConstants.AppId,
                builderMissionThread.GrantId,
                "MarkTaskDone"),
            new JsonObject
            {
                ["summary"] = "Launch plan completed."
            },
            CancellationToken.None);

        Assert.True(done.Success, done.ErrorMessage);
        var awaitingView = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var awaitingMission = Assert.Single(awaitingView.Missions);
        Assert.Equal("awaitingLeaderReview", awaitingMission.Status);
        Assert.Null(awaitingMission.CompletedAt);
        Assert.Contains(
            _sessionService.LastSubmittedContent,
            content => content is TextContent text && text.Text.Contains("Mission ready for Leader finalization", StringComparison.Ordinal));
        var awaitingArchiveError = await Assert.ThrowsAsync<AppServerException>(() =>
            _teamsService.ArchiveMissionAsync(
                _sessionService,
                _workspaceCraftPath,
                new TeamsMissionArchiveParams { MissionId = created.Mission.MissionId },
                CancellationToken.None));
        Assert.Equal(AppServerErrors.InvalidParamsCode, awaitingArchiveError.Code);

        var finalized = await InvokeTeamToolAsync(
            leaderMissionThread,
            "MarkMissionDone",
            new JsonObject
            {
                ["finalResponse"] = "The launch plan is ready for the user."
            });
        Assert.True(finalized.Success, finalized.ErrorMessage);
        var completedView = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var completedMission = Assert.Single(completedView.Missions);
        Assert.Equal("done", completedMission.Status);
        Assert.NotNull(completedMission.CompletedAt);
        Assert.Equal("The launch plan is ready for the user.", completedMission.CompletionSummary);
        Assert.Equal("The launch plan is ready for the user.", completedMission.FinalResponse);

        var archivedView = await _teamsService.ArchiveMissionAsync(
            _sessionService,
            _workspaceCraftPath,
            new TeamsMissionArchiveParams { MissionId = created.Mission.MissionId },
            CancellationToken.None);

        Assert.Empty(archivedView.Missions);
        Assert.Empty(archivedView.Tasks);
        Assert.Empty(archivedView.MissionThreads);
        var archivedMission = Assert.Single(archivedView.ArchivedMissions);
        Assert.Equal(created.Mission.MissionId, archivedMission.MissionId);
        Assert.Equal("done", archivedMission.Status);
        Assert.NotNull(archivedMission.ArchivedAt);

        using var state = JsonDocument.Parse(File.ReadAllText(Path.Combine(_workspaceCraftPath, "teams", "state.json")));
        var storedMission = Assert.Single(state.RootElement.GetProperty("missions").EnumerateArray());
        Assert.Equal("done", storedMission.GetProperty("status").GetString());
        Assert.True(storedMission.TryGetProperty("archivedAt", out var archivedAt));
        Assert.Equal(JsonValueKind.String, archivedAt.ValueKind);
        Assert.All(state.RootElement.GetProperty("missionThreads").EnumerateArray(), thread =>
        {
            Assert.Equal("archived", thread.GetProperty("status").GetString());
            Assert.True(thread.TryGetProperty("archivedAt", out var threadArchivedAt));
            Assert.Equal(JsonValueKind.String, threadArchivedAt.ValueKind);
        });
    }

    [Fact]
    public async Task TeamCommunicationTools_RecordMailboxAndCoalesceActionableMessages()
    {
        var created = await _teamsService.CreateMissionAsync(
            _appBindingService,
            _sessionService,
            _tempRoot,
            _workspaceCraftPath,
            new TeamsMissionCreateParams
            {
                Title = "Coordinate research",
                Prompt = "Research and coordinate teammate progress."
            },
            CancellationToken.None);
        var leaderMissionThread = Assert.Single(created.Team.MissionThreads, thread => thread.MemberId == "leader");

        var assigned = await InvokeTeamToolAsync(
            leaderMissionThread,
            "AssignTask",
            new JsonObject
            {
                ["assignee"] = "builder",
                ["title"] = "Build the brief",
                ["prompt"] = "Create the implementation brief."
            });
        Assert.True(assigned.Success, assigned.ErrorMessage);
        await _teamsService.HandleThreadRuntimeSignalAsync(
            _workspaceCraftPath,
            leaderMissionThread.ThreadId,
            SessionThreadRuntimeSignal.TurnCompleted,
            CancellationToken.None);

        var view = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var task = Assert.Single(view.Tasks);
        var builderMissionThread = Assert.Single(view.MissionThreads, thread => thread.MemberId == "builder");

        var members = await InvokeTeamToolAsync(
            leaderMissionThread,
            "ListTeamMembers",
            new JsonObject());
        Assert.True(members.Success, members.ErrorMessage);
        var membersJson = members.StructuredResult?.ToJsonString() ?? string.Empty;
        Assert.Contains("builder", membersJson, StringComparison.Ordinal);
        Assert.Contains(task.TaskId, membersJson, StringComparison.Ordinal);

        var missionState = await InvokeTeamToolAsync(
            leaderMissionThread,
            "ReadMissionState",
            new JsonObject());
        Assert.True(missionState.Success, missionState.ErrorMessage);
        var missionJson = missionState.StructuredResult?.ToJsonString() ?? string.Empty;
        Assert.Contains(task.TaskId, missionJson, StringComparison.Ordinal);
        Assert.Contains(builderMissionThread.ThreadId, missionJson, StringComparison.Ordinal);

        var memberStatus = await InvokeTeamToolAsync(
            leaderMissionThread,
            "ReadMemberStatus",
            new JsonObject
            {
                ["memberId"] = "builder"
            });
        Assert.True(memberStatus.Success, memberStatus.ErrorMessage);
        Assert.Contains("Build the brief", memberStatus.StructuredResult?.ToJsonString() ?? string.Empty, StringComparison.Ordinal);

        var done = await InvokeTeamToolAsync(
            builderMissionThread,
            "MarkTaskDone",
            new JsonObject
            {
                ["summary"] = "Brief draft is complete."
            });
        Assert.True(done.Success, done.ErrorMessage);

        await _teamsService.HandleThreadRuntimeSignalAsync(
            _workspaceCraftPath,
            builderMissionThread.ThreadId,
            SessionThreadRuntimeSignal.TurnStarted,
            CancellationToken.None);

        var firstMessage = await InvokeTeamToolAsync(
            leaderMissionThread,
            "SendMessage",
            new JsonObject
            {
                ["to"] = "builder",
                ["taskId"] = task.TaskId,
                ["message"] = "FYI: the outline was approved."
            });
        Assert.True(firstMessage.Success, firstMessage.ErrorMessage);

        var secondMessage = await InvokeTeamToolAsync(
            leaderMissionThread,
            "SendMessage",
            new JsonObject
            {
                ["to"] = "builder",
                ["taskId"] = task.TaskId,
                ["message"] = "Please include the risk notes in the brief."
            });
        Assert.True(secondMessage.Success, secondMessage.ErrorMessage);

        var thirdMessage = await InvokeTeamToolAsync(
            leaderMissionThread,
            "SendMessage",
            new JsonObject
            {
                ["to"] = "builder",
                ["taskId"] = task.TaskId,
                ["message"] = "Also include the delivery checklist."
            });
        Assert.True(thirdMessage.Success, thirdMessage.ErrorMessage);

        var busyView = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        Assert.Equal(3, busyView.Messages.Count);
        Assert.All(busyView.Messages, message => Assert.Null(message.DeliveredQueuedInputId));

        await _teamsService.HandleThreadRuntimeSignalAsync(
            _workspaceCraftPath,
            builderMissionThread.ThreadId,
            SessionThreadRuntimeSignal.TurnCompleted,
            CancellationToken.None);

        var updatedView = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var storedMessages = updatedView.Messages.OrderBy(message => message.CreatedAt).ToList();
        Assert.Equal(3, storedMessages.Count);
        Assert.Equal("leader", storedMessages[0].FromMemberId);
        Assert.Equal("builder", storedMessages[0].ToMemberId);
        Assert.Equal(task.TaskId, storedMessages[0].TaskId);
        Assert.All(storedMessages, message =>
        {
            Assert.True(message.RequiresAction);
            Assert.Equal("request", message.Kind);
            Assert.Equal("deliveredToTurn", message.Status);
            Assert.False(string.IsNullOrWhiteSpace(message.DeliveredQueuedInputId));
        });
        Assert.Single(storedMessages.Select(message => message.DeliveredQueuedInputId).Distinct(StringComparer.Ordinal));
        Assert.Contains("risk notes", storedMessages[1].Content, StringComparison.Ordinal);
        Assert.Contains("delivery checklist", storedMessages[2].Content, StringComparison.Ordinal);
        Assert.Contains(
            updatedView.MailboxDigests,
            digest => digest.MemberId == "builder" && digest.Content.Contains("Team Leader message", StringComparison.Ordinal));
        Assert.Contains(
            _sessionService.LastSubmittedContent,
            content => content is TextContent text
                       && text.Text.Contains("risk notes", StringComparison.Ordinal)
                       && text.Text.Contains("delivery checklist", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TeamTools_RejectUnauthorizedTaskMutationAndReturnActionableMissionDoneErrors()
    {
        var created = await _teamsService.CreateMissionAsync(
            _appBindingService,
            _sessionService,
            _tempRoot,
            _workspaceCraftPath,
            new TeamsMissionCreateParams
            {
                Title = "Guard task ownership",
                Prompt = "Create a task that only the assignee can update."
            },
            CancellationToken.None);
        var leaderMissionThread = Assert.Single(created.Team.MissionThreads, thread => thread.MemberId == "leader");

        var assigned = await InvokeTeamToolAsync(
            leaderMissionThread,
            "AssignTask",
            new JsonObject
            {
                ["assignee"] = "builder",
                ["title"] = "Owned builder task",
                ["prompt"] = "Only Builder should update this task."
            });
        Assert.True(assigned.Success, assigned.ErrorMessage);

        var view = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var task = Assert.Single(view.Tasks);
        Assert.Equal("t1", task.Alias);
        var builderMissionThread = Assert.Single(view.MissionThreads, thread => thread.MemberId == "builder");
        await _teamsService.HandleThreadRuntimeSignalAsync(
            _workspaceCraftPath,
            leaderMissionThread.ThreadId,
            SessionThreadRuntimeSignal.TurnCompleted,
            CancellationToken.None);

        foreach (var toolCall in new[]
                 {
                     ("ReportProgress", new JsonObject { ["summary"] = "wrong caller" }),
                     ("PublishArtifact", new JsonObject { ["title"] = "wrong artifact", ["pathOrUri"] = "wrong.md" }),
                     ("MarkTaskDone", new JsonObject { ["summary"] = "wrong caller done" })
                 })
        {
            var rejected = await InvokeTeamToolAsync(leaderMissionThread, toolCall.Item1, toolCall.Item2);
            Assert.False(rejected.Success);
            Assert.Contains("No current Teams task", rejected.ErrorMessage, StringComparison.Ordinal);
        }

        var teammateMessage = await InvokeTeamToolAsync(
            builderMissionThread,
            "SendMessage",
            new JsonObject
            {
                ["to"] = "leader",
                ["message"] = "Builder needs a Leader decision."
            });
        Assert.True(teammateMessage.Success, teammateMessage.ErrorMessage);
        var messageView = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var storedMessage = Assert.Single(messageView.Messages);
        Assert.Equal("builder", storedMessage.FromMemberId);
        Assert.Equal("leader", storedMessage.ToMemberId);
        Assert.Equal("deliveredToTurn", storedMessage.Status);
        Assert.False(string.IsNullOrWhiteSpace(storedMessage.DeliveredQueuedInputId));
        Assert.Contains(
            _sessionService.LastSubmittedContent,
            content => content is TextContent text && text.Text.Contains("Builder needs a Leader decision", StringComparison.Ordinal));

        var unassignedMessage = await InvokeTeamToolAsync(
            leaderMissionThread,
            "SendMessage",
            new JsonObject
            {
                ["to"] = "explorer",
                ["message"] = "Please help too."
            });
        Assert.False(unassignedMessage.Success);
        Assert.Contains("Assign a task", unassignedMessage.ErrorMessage, StringComparison.Ordinal);

        var invalidDone = await InvokeTeamToolAsync(
            leaderMissionThread,
            "MarkMissionDone",
            new JsonObject());
        Assert.False(invalidDone.Success);
        Assert.Contains("finalResponse", invalidDone.ErrorMessage, StringComparison.Ordinal);

        var unfinishedDone = await InvokeTeamToolAsync(
            leaderMissionThread,
            "MarkMissionDone",
            new JsonObject
            {
                ["finalResponse"] = "This should not finalize yet."
            });
        Assert.False(unfinishedDone.Success);
        Assert.Contains("Owned builder task", unfinishedDone.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("ReadMissionState", unfinishedDone.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AssignTask_WithDependenciesDispatchesOnlyAfterUpstreamDone()
    {
        var created = await _teamsService.CreateMissionAsync(
            _appBindingService,
            _sessionService,
            _tempRoot,
            _workspaceCraftPath,
            new TeamsMissionCreateParams
            {
                Title = "Dependency graph",
                Prompt = "Research before building."
            },
            CancellationToken.None);
        var leaderMissionThread = Assert.Single(created.Team.MissionThreads, thread => thread.MemberId == "leader");

        var researchAssigned = await InvokeTeamToolAsync(
            leaderMissionThread,
            "AssignTask",
            new JsonObject
            {
                ["assignee"] = "explorer",
                ["title"] = "Research inputs",
                ["prompt"] = "Find the inputs needed by Builder."
            });
        Assert.True(researchAssigned.Success, researchAssigned.ErrorMessage);
        var researchView = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var researchTask = Assert.Single(researchView.Tasks);
        Assert.Equal("running", researchTask.Status);
        Assert.Equal("t1", researchTask.Alias);

        var buildAssigned = await InvokeTeamToolAsync(
            leaderMissionThread,
            "AssignTask",
            new JsonObject
            {
                ["assignee"] = "builder",
                ["title"] = "Build from research",
                ["prompt"] = "Build only after the research task is done.",
                ["dependsOnTaskIds"] = new JsonArray(researchTask.Alias)
            });
        Assert.True(buildAssigned.Success, buildAssigned.ErrorMessage);

        var waitingView = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var buildTask = Assert.Single(waitingView.Tasks, task => task.AssigneeMemberId == "builder");
        Assert.Equal("t2", buildTask.Alias);
        Assert.Equal("waitingDependencies", buildTask.Status);
        Assert.Equal(researchTask.TaskId, Assert.Single(buildTask.DependsOnTaskIds));
        Assert.DoesNotContain(waitingView.MissionThreads, thread => thread.MemberId == "builder");

        var explorerMissionThread = Assert.Single(waitingView.MissionThreads, thread => thread.MemberId == "explorer");
        var researchDone = await InvokeTeamToolAsync(
            explorerMissionThread,
            "MarkTaskDone",
            new JsonObject
            {
                ["summary"] = "Research is ready."
            });
        Assert.True(researchDone.Success, researchDone.ErrorMessage);

        var releasedView = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var releasedBuildTask = Assert.Single(releasedView.Tasks, task => task.TaskId == buildTask.TaskId);
        Assert.Equal("running", releasedBuildTask.Status);
        Assert.Contains(releasedView.MissionThreads, thread => thread.MemberId == "builder" && thread.CurrentTaskId == buildTask.TaskId);
        Assert.Contains(
            _sessionService.LastSubmittedContent,
            content => content is TextContent text && text.Text.Contains("Team task assigned: Build from research", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MarkTaskDone_WakesLeaderWithTaskResultWhenMissionStillActive()
    {
        var created = await _teamsService.CreateMissionAsync(
            _appBindingService,
            _sessionService,
            _tempRoot,
            _workspaceCraftPath,
            new TeamsMissionCreateParams
            {
                Title = "Watch task result",
                Prompt = "Dispatch parallel work and resume leadership after the first result."
            },
            CancellationToken.None);
        var leaderMissionThread = Assert.Single(created.Team.MissionThreads, thread => thread.MemberId == "leader");

        var explorerAssigned = await InvokeTeamToolAsync(
            leaderMissionThread,
            "AssignTask",
            new JsonObject
            {
                ["assignee"] = "explorer",
                ["title"] = "Research first signal",
                ["prompt"] = "Produce the first signal."
            });
        Assert.True(explorerAssigned.Success, explorerAssigned.ErrorMessage);

        var builderAssigned = await InvokeTeamToolAsync(
            leaderMissionThread,
            "AssignTask",
            new JsonObject
            {
                ["assignee"] = "builder",
                ["title"] = "Build second signal",
                ["prompt"] = "Keep the mission active after Explorer completes."
            });
        Assert.True(builderAssigned.Success, builderAssigned.ErrorMessage);

        var assignedView = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var explorerTask = Assert.Single(assignedView.Tasks, task => task.AssigneeMemberId == "explorer");
        var builderTask = Assert.Single(assignedView.Tasks, task => task.AssigneeMemberId == "builder");

        Assert.Empty(assignedView.LeaderWaits);
        Assert.True(string.IsNullOrWhiteSpace(Assert.Single(assignedView.Missions).LeaderContinuationQueuedInputId));

        await _teamsService.HandleThreadRuntimeSignalAsync(
            _workspaceCraftPath,
            leaderMissionThread.ThreadId,
            SessionThreadRuntimeSignal.TurnCompleted,
            CancellationToken.None);

        var explorerMissionThread = Assert.Single(assignedView.MissionThreads, thread => thread.MemberId == "explorer");
        var explorerDone = await InvokeTeamToolAsync(
            explorerMissionThread,
            "MarkTaskDone",
            new JsonObject
            {
                ["summary"] = "Explorer produced the first signal."
            });
        Assert.True(explorerDone.Success, explorerDone.ErrorMessage);

        var wokenView = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        Assert.Equal("active", Assert.Single(wokenView.Missions).Status);
        Assert.Empty(wokenView.LeaderWaits);
        var completedExplorerTask = Assert.Single(wokenView.Tasks, task => task.TaskId == explorerTask.TaskId);
        Assert.NotNull(completedExplorerTask.LeaderNotifiedAt);
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(wokenView.Missions).LeaderContinuationQueuedInputId));
        Assert.Contains(
            _sessionService.LastSubmittedContent,
            content => content is TextContent text && text.Text.Contains("Team task result available", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ViewTeam_HidesAndCancelsLegacyLeaderWaitState()
    {
        Directory.CreateDirectory(Path.Combine(_workspaceCraftPath, "teams"));
        File.WriteAllText(
            Path.Combine(_workspaceCraftPath, "teams", "state.json"),
            """
            {
              "team": {
                "teamId": "default",
                "enabled": true,
                "createdAt": "2026-05-23T00:00:00Z",
                "updatedAt": "2026-05-23T00:00:00Z"
              },
              "members": [],
              "missions": [
                {
                  "missionId": "mission_wait",
                  "title": "Legacy wait",
                  "prompt": "Old wait state should not drive scheduling.",
                  "status": "active",
                  "createdAt": "2026-05-23T00:00:00Z",
                  "updatedAt": "2026-05-23T00:00:00Z",
                  "leaderThreadId": "thread_leader"
                }
              ],
              "missionThreads": [],
              "tasks": [],
              "messages": [],
              "leaderWaits": [
                {
                  "waitId": "wait_legacy",
                  "missionId": "mission_wait",
                  "condition": "missionReady",
                  "taskIds": [],
                  "reason": "legacy wait",
                  "status": "active",
                  "createdAt": "2026-05-23T00:00:00Z",
                  "updatedAt": "2026-05-23T00:00:00Z"
                }
              ],
              "mailboxDigests": [],
              "artifacts": []
            }
            """);

        var view = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        Assert.Empty(view.LeaderWaits);
        Assert.True(string.IsNullOrWhiteSpace(Assert.Single(view.Missions).LeaderContinuationQueuedInputId));

        using var state = JsonDocument.Parse(File.ReadAllText(Path.Combine(_workspaceCraftPath, "teams", "state.json")));
        var storedWait = Assert.Single(state.RootElement.GetProperty("leaderWaits").EnumerateArray());
        Assert.Equal("cancelled", storedWait.GetProperty("status").GetString());
    }

    [Fact]
    public async Task RequiresLeaderSynthesis_WakesLeaderBeforeDispatchingDownstreamTask()
    {
        var created = await _teamsService.CreateMissionAsync(
            _appBindingService,
            _sessionService,
            _tempRoot,
            _workspaceCraftPath,
            new TeamsMissionCreateParams
            {
                Title = "Synthesize research",
                Prompt = "Research first, then ask the Leader to shape Builder handoff."
            },
            CancellationToken.None);
        var leaderMissionThread = Assert.Single(created.Team.MissionThreads, thread => thread.MemberId == "leader");

        var researchAssigned = await InvokeTeamToolAsync(
            leaderMissionThread,
            "AssignTask",
            new JsonObject
            {
                ["assignee"] = "explorer",
                ["title"] = "Research source material",
                ["prompt"] = "Find source material for Builder."
            });
        Assert.True(researchAssigned.Success, researchAssigned.ErrorMessage);

        var researchView = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var researchTask = Assert.Single(researchView.Tasks);
        Assert.Equal("t1", researchTask.Alias);
        var buildAssigned = await InvokeTeamToolAsync(
            leaderMissionThread,
            "AssignTask",
            new JsonObject
            {
                ["assignee"] = "builder",
                ["title"] = "Build synthesized brief",
                ["prompt"] = "Build from the Leader synthesis.",
                ["dependsOnTaskIds"] = new JsonArray(researchTask.Alias),
                ["requiresLeaderSynthesis"] = true
            });
        Assert.True(buildAssigned.Success, buildAssigned.ErrorMessage);
        await _teamsService.HandleThreadRuntimeSignalAsync(
            _workspaceCraftPath,
            leaderMissionThread.ThreadId,
            SessionThreadRuntimeSignal.TurnCompleted,
            CancellationToken.None);

        var waitingView = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var buildTask = Assert.Single(waitingView.Tasks, task => task.AssigneeMemberId == "builder");
        Assert.Equal("t2", buildTask.Alias);
        Assert.True(buildTask.RequiresLeaderSynthesis);
        Assert.Equal("waitingDependencies", buildTask.Status);
        Assert.DoesNotContain(waitingView.MissionThreads, thread => thread.MemberId == "builder");

        var explorerMissionThread = Assert.Single(waitingView.MissionThreads, thread => thread.MemberId == "explorer");
        var researchDone = await InvokeTeamToolAsync(
            explorerMissionThread,
            "MarkTaskDone",
            new JsonObject
            {
                ["summary"] = "Research source material is ready."
            });
        Assert.True(researchDone.Success, researchDone.ErrorMessage);

        var synthesisView = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var synthesisTask = Assert.Single(synthesisView.Tasks, task => task.TaskId == buildTask.TaskId);
        Assert.Equal("waitingDependencies", synthesisTask.Status);
        Assert.DoesNotContain(synthesisView.MissionThreads, thread => thread.MemberId == "builder");
        Assert.Contains(
            _sessionService.LastSubmittedContent,
            content => content is TextContent text && text.Text.Contains("Mission needs Leader synthesis", StringComparison.Ordinal));

        var synthesized = await InvokeTeamToolAsync(
            leaderMissionThread,
            "SendMessage",
            new JsonObject
            {
                ["to"] = "builder",
                ["taskId"] = buildTask.Alias,
                ["message"] = "Use the research summary to draft a concise implementation brief."
            });
        Assert.True(synthesized.Success, synthesized.ErrorMessage);

        var releasedView = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var releasedTask = Assert.Single(releasedView.Tasks, task => task.TaskId == buildTask.TaskId);
        Assert.Equal("running", releasedTask.Status);
        Assert.False(string.IsNullOrWhiteSpace(releasedTask.SynthesisMessageId));
        var deliveredMessage = Assert.Single(releasedView.Messages);
        Assert.Equal(releasedTask.SynthesisMessageId, deliveredMessage.MessageId);
        Assert.False(string.IsNullOrWhiteSpace(deliveredMessage.DeliveredQueuedInputId));
        Assert.Contains(releasedView.MissionThreads, thread => thread.MemberId == "builder" && thread.CurrentTaskId == buildTask.TaskId);
        Assert.Contains(
            _sessionService.LastSubmittedContent,
            content => content is TextContent text
                       && text.Text.Contains("Team task assigned: Build synthesized brief", StringComparison.Ordinal)
                       && text.Text.Contains("implementation brief", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReportProgress_RejectsCompletedAndBlockedWakesLeader()
    {
        var created = await _teamsService.CreateMissionAsync(
            _appBindingService,
            _sessionService,
            _tempRoot,
            _workspaceCraftPath,
            new TeamsMissionCreateParams
            {
                Title = "Handle blocker",
                Prompt = "Make the blocker visible to the Leader."
            },
            CancellationToken.None);
        var leaderMissionThread = Assert.Single(created.Team.MissionThreads, thread => thread.MemberId == "leader");
        var assigned = await InvokeTeamToolAsync(
            leaderMissionThread,
            "AssignTask",
            new JsonObject
            {
                ["assignee"] = "builder",
                ["title"] = "Blocked build",
                ["prompt"] = "Report the blocker."
            });
        Assert.True(assigned.Success, assigned.ErrorMessage);
        await _teamsService.HandleThreadRuntimeSignalAsync(
            _workspaceCraftPath,
            leaderMissionThread.ThreadId,
            SessionThreadRuntimeSignal.TurnCompleted,
            CancellationToken.None);

        var view = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var task = Assert.Single(view.Tasks);
        var builderMissionThread = Assert.Single(view.MissionThreads, thread => thread.MemberId == "builder");

        var completedProgress = await InvokeTeamToolAsync(
            builderMissionThread,
            "ReportProgress",
            new JsonObject
            {
                ["summary"] = "I am done.",
                ["status"] = "completed"
            });
        Assert.False(completedProgress.Success);
        Assert.Contains("MarkTaskDone", completedProgress.ErrorMessage, StringComparison.Ordinal);

        var blockedProgress = await InvokeTeamToolAsync(
            builderMissionThread,
            "ReportProgress",
            new JsonObject
            {
                ["summary"] = "Blocked on missing credentials.",
                ["status"] = "blocked",
                ["blockedOnTaskIds"] = new JsonArray(task.Alias)
            });
        Assert.True(blockedProgress.Success, blockedProgress.ErrorMessage);

        var blockedView = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var blockedTask = Assert.Single(blockedView.Tasks);
        Assert.Equal("blocked", blockedTask.Status);
        Assert.Equal("Blocked on missing credentials.", blockedTask.BlockedReason);
        Assert.Equal(task.TaskId, Assert.Single(blockedTask.BlockedOnTaskIds));
        Assert.Contains(
            _sessionService.LastSubmittedContent,
            content => content is TextContent text && text.Text.Contains("Mission needs Leader coordination", StringComparison.Ordinal));
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(blockedView.Missions).LeaderContinuationQueuedInputId));
    }

    [Fact]
    public async Task LeanToolsPersistTaskUpdatesMessagesAndInferredArtifactsAcrossTeamViews()
    {
        var created = await _teamsService.CreateMissionAsync(
            _appBindingService,
            _sessionService,
            _tempRoot,
            _workspaceCraftPath,
            new TeamsMissionCreateParams
            {
                Title = "Publish task-board output",
                Prompt = "Exercise task metadata, message kind, and artifact fields."
            },
            CancellationToken.None);
        var leaderMissionThread = Assert.Single(created.Team.MissionThreads, thread => thread.MemberId == "leader");

        var assigned = await InvokeTeamToolAsync(
            leaderMissionThread,
            "AssignTask",
            new JsonObject
            {
                ["assignee"] = "builder",
                ["title"] = "Create report",
                ["prompt"] = "Create a reusable report artifact."
            });
        Assert.True(assigned.Success, assigned.ErrorMessage);
        await _teamsService.HandleThreadRuntimeSignalAsync(
            _workspaceCraftPath,
            leaderMissionThread.ThreadId,
            SessionThreadRuntimeSignal.TurnCompleted,
            CancellationToken.None);

        var view = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var task = Assert.Single(view.Tasks);
        var builderMissionThread = Assert.Single(view.MissionThreads, thread => thread.MemberId == "builder");

        var running = await InvokeTeamToolAsync(
            builderMissionThread,
            "ReportProgress",
            new JsonObject
            {
                ["summary"] = "Report draft is underway.",
                ["status"] = "running"
            });
        Assert.True(running.Success, running.ErrorMessage);

        var artifact = await InvokeTeamToolAsync(
            builderMissionThread,
            "PublishArtifact",
            new JsonObject
            {
                ["title"] = "Report draft",
                ["pathOrUri"] = ".craft/teams/missions/report.md",
                ["summary"] = "Markdown report draft."
            });
        Assert.True(artifact.Success, artifact.ErrorMessage);
        var artifactView = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var reportArtifact = Assert.Single(artifactView.Artifacts);
        Assert.Equal("a1", reportArtifact.Alias);

        var message = await InvokeTeamToolAsync(
            leaderMissionThread,
            "SendMessage",
            new JsonObject
            {
                ["to"] = "builder",
                ["taskId"] = task.Alias,
                ["message"] = $"Use {reportArtifact.Alias} as the final report structure."
            });
        Assert.True(message.Success, message.ErrorMessage);
        var messageView = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var storedMessage = Assert.Single(messageView.Messages);
        Assert.Equal("request", storedMessage.Kind);
        Assert.True(storedMessage.RequiresAction);
        Assert.Equal(reportArtifact.ArtifactId, Assert.Single(storedMessage.ArtifactIds));

        var badArtifactAlias = await InvokeTeamToolAsync(
            leaderMissionThread,
            "SendMessage",
            new JsonObject
            {
                ["to"] = "builder",
                ["taskId"] = task.Alias,
                ["message"] = "Please use a99 as the final report structure."
            });
        Assert.False(badArtifactAlias.Success);
        Assert.Contains("Artifact", badArtifactAlias.ErrorMessage, StringComparison.Ordinal);

        var badArtifactReference = await InvokeTeamToolAsync(
            leaderMissionThread,
            "SendMessage",
            new JsonObject
            {
                ["to"] = "builder",
                ["taskId"] = task.Alias,
                ["message"] = "Please use artifact_deadbeefdeadbeefdeadbeefdeadbeef."
            });
        Assert.False(badArtifactReference.Success);
        Assert.Contains("Artifact", badArtifactReference.ErrorMessage, StringComparison.Ordinal);

        var done = await InvokeTeamToolAsync(
            builderMissionThread,
            "MarkTaskDone",
            new JsonObject
            {
                ["summary"] = "Completed markdown report draft."
            });
        Assert.True(done.Success, done.ErrorMessage);

        var finalView = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var finalTask = Assert.Single(finalView.Tasks);
        Assert.Equal("done", finalTask.Status);
        Assert.Equal("Completed markdown report draft.", finalTask.LatestUpdate);
        Assert.Equal("Completed markdown report draft.", finalTask.OutputSummary);
        var finalArtifact = Assert.Single(finalView.Artifacts);
        Assert.Equal("a1", finalArtifact.Alias);
        Assert.Equal("document", finalArtifact.Kind);
        Assert.Equal("md", finalArtifact.Format);
        Assert.Equal("Markdown report draft.", finalArtifact.Summary);
        Assert.Equal("Markdown report draft.", finalArtifact.Description);
        Assert.Equal(task.TaskId, finalArtifact.SourceTaskId);
        Assert.Null(finalArtifact.SourceMessageId);

        var missionState = await InvokeTeamToolAsync(
            leaderMissionThread,
            "ReadMissionState",
            new JsonObject());
        Assert.True(missionState.Success, missionState.ErrorMessage);
        var missionJson = missionState.StructuredResult?.ToJsonString() ?? string.Empty;
        Assert.Contains("Completed markdown report draft.", missionJson, StringComparison.Ordinal);
        Assert.Contains("\"alias\":\"t1\"", missionJson, StringComparison.Ordinal);
        Assert.Contains("\"alias\":\"a1\"", missionJson, StringComparison.Ordinal);
        Assert.Contains("md", missionJson, StringComparison.Ordinal);

        using var state = JsonDocument.Parse(File.ReadAllText(Path.Combine(_workspaceCraftPath, "teams", "state.json")));
        var storedTask = Assert.Single(state.RootElement.GetProperty("tasks").EnumerateArray());
        Assert.Equal("t1", storedTask.GetProperty("alias").GetString());
        Assert.Equal("Completed markdown report draft.", storedTask.GetProperty("outputSummary").GetString());
        var storedArtifact = Assert.Single(state.RootElement.GetProperty("artifacts").EnumerateArray());
        Assert.Equal("a1", storedArtifact.GetProperty("alias").GetString());
        Assert.Equal("document", storedArtifact.GetProperty("kind").GetString());
        Assert.Equal("md", storedArtifact.GetProperty("format").GetString());
    }

    [Fact]
    public async Task ReviewGate_BlocksMissionFinalizationUntilReviewTaskDone()
    {
        var created = await _teamsService.CreateMissionAsync(
            _appBindingService,
            _sessionService,
            _tempRoot,
            _workspaceCraftPath,
            new TeamsMissionCreateParams
            {
                Title = "Review gate",
                Prompt = "Build and review before finalizing."
            },
            CancellationToken.None);
        var leaderMissionThread = Assert.Single(created.Team.MissionThreads, thread => thread.MemberId == "leader");

        var buildAssigned = await InvokeTeamToolAsync(
            leaderMissionThread,
            "AssignTask",
            new JsonObject
            {
                ["assignee"] = "builder",
                ["title"] = "Build artifact",
                ["prompt"] = "Build the artifact."
            });
        Assert.True(buildAssigned.Success, buildAssigned.ErrorMessage);
        var buildView = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var buildTask = Assert.Single(buildView.Tasks);

        var reviewAssigned = await InvokeTeamToolAsync(
            leaderMissionThread,
            "AssignTask",
            new JsonObject
            {
                ["assignee"] = "reviewer",
                ["title"] = "Review artifact",
                ["prompt"] = "Review the artifact before finalization.",
                ["kind"] = "review",
                ["dependsOnTaskIds"] = new JsonArray(buildTask.Alias)
            });
        Assert.True(reviewAssigned.Success, reviewAssigned.ErrorMessage);
        await _teamsService.HandleThreadRuntimeSignalAsync(
            _workspaceCraftPath,
            leaderMissionThread.ThreadId,
            SessionThreadRuntimeSignal.TurnCompleted,
            CancellationToken.None);

        var waitingReviewView = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var reviewTask = Assert.Single(waitingReviewView.Tasks, task => task.Kind == "review");
        Assert.Equal("waitingDependencies", reviewTask.Status);
        Assert.Equal("active", Assert.Single(waitingReviewView.Missions).Status);

        var builderMissionThread = Assert.Single(waitingReviewView.MissionThreads, thread => thread.MemberId == "builder");
        var buildDone = await InvokeTeamToolAsync(
            builderMissionThread,
            "MarkTaskDone",
            new JsonObject
            {
                ["summary"] = "Artifact is ready for review."
            });
        Assert.True(buildDone.Success, buildDone.ErrorMessage);

        var reviewingView = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var runningReviewTask = Assert.Single(reviewingView.Tasks, task => task.TaskId == reviewTask.TaskId);
        Assert.Equal("running", runningReviewTask.Status);
        Assert.Equal("active", Assert.Single(reviewingView.Missions).Status);
        Assert.Contains(
            _sessionService.LastSubmittedContent,
            content => content is TextContent text && text.Text.Contains("Team task result available", StringComparison.Ordinal));
        await _teamsService.HandleThreadRuntimeSignalAsync(
            _workspaceCraftPath,
            leaderMissionThread.ThreadId,
            SessionThreadRuntimeSignal.TurnCompleted,
            CancellationToken.None);

        var reviewerMissionThread = Assert.Single(reviewingView.MissionThreads, thread => thread.MemberId == "reviewer");
        var reviewDone = await InvokeTeamToolAsync(
            reviewerMissionThread,
            "MarkTaskDone",
            new JsonObject
            {
                ["summary"] = "Review passed."
            });
        Assert.True(reviewDone.Success, reviewDone.ErrorMessage);

        var readyForLeaderView = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var mission = Assert.Single(readyForLeaderView.Missions);
        Assert.Equal("awaitingLeaderReview", mission.Status);
        Assert.Null(mission.CompletedAt);
        Assert.Contains(
            _sessionService.LastSubmittedContent,
            content => content is TextContent text && text.Text.Contains("Mission ready for Leader finalization", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TeammateTurnCompletionWithoutMarkTaskDoneGetsOneRecoveryThenBlocks()
    {
        var created = await _teamsService.CreateMissionAsync(
            _appBindingService,
            _sessionService,
            _tempRoot,
            _workspaceCraftPath,
            new TeamsMissionCreateParams
            {
                Title = "Detect dropped task",
                Prompt = "Catch teammates that exit without completing work."
            },
            CancellationToken.None);
        var leaderMissionThread = Assert.Single(created.Team.MissionThreads, thread => thread.MemberId == "leader");
        var assigned = await InvokeTeamToolAsync(
            leaderMissionThread,
            "AssignTask",
            new JsonObject
            {
                ["assignee"] = "builder",
                ["title"] = "Do not finish",
                ["prompt"] = "End the turn without marking done."
            });
        Assert.True(assigned.Success, assigned.ErrorMessage);
        await _teamsService.HandleThreadRuntimeSignalAsync(
            _workspaceCraftPath,
            leaderMissionThread.ThreadId,
            SessionThreadRuntimeSignal.TurnCompleted,
            CancellationToken.None);

        var activeView = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var task = Assert.Single(activeView.Tasks);
        var builderMissionThread = Assert.Single(activeView.MissionThreads, thread => thread.MemberId == "builder");
        Assert.Equal("running", task.Status);

        await _teamsService.HandleThreadRuntimeSignalAsync(
            _workspaceCraftPath,
            builderMissionThread.ThreadId,
            SessionThreadRuntimeSignal.TurnCompleted,
            CancellationToken.None);

        var recoveryView = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var recoveryTask = Assert.Single(recoveryView.Tasks);
        Assert.Equal("running", recoveryTask.Status);
        Assert.True(recoveryTask.CompletionRecoveryPending);
        Assert.Equal(1, recoveryTask.CompletionRecoveryAttempts);
        Assert.False(string.IsNullOrWhiteSpace(recoveryTask.CompletionRecoveryQueuedInputId));
        Assert.Contains(
            _sessionService.LastSubmittedContent,
            content => content is TextContent text && text.Text.Contains("Task completion check", StringComparison.Ordinal));
        Assert.True(string.IsNullOrWhiteSpace(Assert.Single(recoveryView.Missions).LeaderContinuationQueuedInputId));

        await _teamsService.HandleThreadRuntimeSignalAsync(
            _workspaceCraftPath,
            builderMissionThread.ThreadId,
            SessionThreadRuntimeSignal.TurnCompleted,
            CancellationToken.None);

        var blockedView = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var blockedTask = Assert.Single(blockedView.Tasks);
        Assert.Equal("blocked", blockedTask.Status);
        Assert.Contains("completion recovery prompt", blockedTask.BlockedReason, StringComparison.Ordinal);
        Assert.False(blockedTask.CompletionRecoveryPending);
        Assert.Contains(
            _sessionService.LastSubmittedContent,
            content => content is TextContent text && text.Text.Contains("Mission needs Leader coordination", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ViewTeam_RepairsLegacyCompletedTaskStatus()
    {
        Directory.CreateDirectory(Path.Combine(_workspaceCraftPath, "teams"));
        File.WriteAllText(
            Path.Combine(_workspaceCraftPath, "teams", "state.json"),
            """
            {
              "team": {
                "teamId": "default",
                "enabled": true,
                "createdAt": "2026-05-23T00:00:00Z",
                "updatedAt": "2026-05-23T00:00:00Z"
              },
              "members": [
                {
                  "memberId": "leader",
                  "role": "leader",
                  "displayName": "Team Leader",
                  "description": "Legacy leader accent.",
                  "threadId": "",
                  "bindingId": "",
                  "grantId": "",
                  "avatarAccent": "#ef4444",
                  "status": "idle",
                  "deskX": 50,
                  "deskY": 26
                }
              ],
              "missions": [
                {
                  "missionId": "mission_legacy",
                  "title": "Legacy mission",
                  "prompt": "Repair old task status.",
                  "status": "active",
                  "createdAt": "2026-05-23T00:00:00Z",
                  "updatedAt": "2026-05-23T00:00:00Z",
                  "leaderThreadId": "thread_legacy"
                }
              ],
              "missionThreads": [],
              "tasks": [
                {
                  "taskId": "task_legacy",
                  "missionId": "mission_legacy",
                  "assigneeMemberId": "builder",
                  "title": "Legacy task",
                  "prompt": "Already completed in old state.",
                  "status": "completed",
                  "createdAt": "2026-05-23T00:00:00Z",
                  "updatedAt": "2026-05-23T00:00:00Z",
                  "digest": "Done in old state."
                }
              ],
              "messages": [],
              "mailboxDigests": [],
              "artifacts": [
                {
                  "artifactId": "artifact_legacy",
                  "taskId": "task_legacy",
                  "memberId": "builder",
                  "title": "Legacy artifact",
                  "uri": "legacy.md",
                  "createdAt": "2026-05-23T01:00:00Z"
                }
              ]
            }
            """);

        var view = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var repairedTask = Assert.Single(view.Tasks);
        Assert.Equal("done", repairedTask.Status);
        Assert.Equal("t1", repairedTask.Alias);
        Assert.Equal("a1", Assert.Single(view.Artifacts).Alias);
        Assert.Equal("#4f7cf6", Assert.Single(view.Members).AvatarAccent);

        using var state = JsonDocument.Parse(File.ReadAllText(Path.Combine(_workspaceCraftPath, "teams", "state.json")));
        var storedTask = Assert.Single(state.RootElement.GetProperty("tasks").EnumerateArray());
        Assert.Equal("done", storedTask.GetProperty("status").GetString());
        Assert.Equal("t1", storedTask.GetProperty("alias").GetString());
        Assert.Equal("a1", Assert.Single(state.RootElement.GetProperty("artifacts").EnumerateArray()).GetProperty("alias").GetString());
        Assert.Equal("#4f7cf6", Assert.Single(state.RootElement.GetProperty("members").EnumerateArray()).GetProperty("avatarAccent").GetString());
    }

    [Fact]
    public async Task MarkMissionDone_CompletesMissionWithoutTasks()
    {
        var created = await _teamsService.CreateMissionAsync(
            _appBindingService,
            _sessionService,
            _tempRoot,
            _workspaceCraftPath,
            new TeamsMissionCreateParams
            {
                Title = "Answer directly",
                Prompt = "This mission can be answered by the Leader without dispatching tasks."
            },
            CancellationToken.None);

        var leaderMissionThread = Assert.Single(created.Team.MissionThreads, thread => thread.MemberId == "leader");
        var done = await _teamsService.InvokeToolAsync(
            new ManagedAppBindingToolCallContext(
                _workspaceCraftPath,
                _tempRoot,
                leaderMissionThread.BindingId,
                leaderMissionThread.ThreadId,
                "turn_leader",
                "call_mission_done",
                TeamsConstants.AppId,
                leaderMissionThread.GrantId,
                "MarkMissionDone"),
            new JsonObject
            {
                ["finalResponse"] = "Answered directly."
            },
            CancellationToken.None);

        Assert.True(done.Success, done.ErrorMessage);
        var view = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var mission = Assert.Single(view.Missions);
        Assert.Equal("done", mission.Status);
        Assert.Equal("Answered directly.", mission.CompletionSummary);
        Assert.Equal("Answered directly.", mission.FinalResponse);
        Assert.NotNull(mission.CompletedAt);
        Assert.Empty(view.Tasks);
    }

    [Fact]
    public async Task CreateMission_SerializesLeaderMissionThreads()
    {
        var first = await _teamsService.CreateMissionAsync(
            _appBindingService,
            _sessionService,
            _tempRoot,
            _workspaceCraftPath,
            new TeamsMissionCreateParams
            {
                Title = "First mission",
                Prompt = "Plan the first mission."
            },
            CancellationToken.None);
        var firstLeaderThread = Assert.Single(first.Team.MissionThreads, thread => thread.MemberId == "leader");
        Assert.Equal("running", firstLeaderThread.Status);
        Assert.Empty((await _sessionService.GetThreadAsync(firstLeaderThread.ThreadId)).QueuedInputs);

        var second = await _teamsService.CreateMissionAsync(
            _appBindingService,
            _sessionService,
            _tempRoot,
            _workspaceCraftPath,
            new TeamsMissionCreateParams
            {
                Title = "Second mission",
                Prompt = "Plan the second mission."
            },
            CancellationToken.None);

        var blockedView = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var secondLeaderThread = Assert.Single(blockedView.MissionThreads, thread =>
            thread.MemberId == "leader" && thread.MissionId == second.Mission.MissionId);
        Assert.Equal("queued", secondLeaderThread.Status);
        Assert.Single((await _sessionService.GetThreadAsync(secondLeaderThread.ThreadId)).QueuedInputs);

        await _teamsService.HandleThreadRuntimeSignalAsync(
            _workspaceCraftPath,
            firstLeaderThread.ThreadId,
            SessionThreadRuntimeSignal.TurnCompleted,
            CancellationToken.None);

        var releasedView = await _teamsService.ViewTeamAsync(_sessionService, _workspaceCraftPath, CancellationToken.None);
        var releasedSecondLeader = Assert.Single(releasedView.MissionThreads, thread =>
            thread.MemberId == "leader" && thread.MissionId == second.Mission.MissionId);
        Assert.Equal("running", releasedSecondLeader.Status);
        Assert.Empty((await _sessionService.GetThreadAsync(releasedSecondLeader.ThreadId)).QueuedInputs);
        Assert.Contains(
            _sessionService.LastSubmittedContent,
            content => content is TextContent text && text.Text.Contains("Mission created: Second mission", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CancelMission_StopsMissionThreadsAndClearsQueuedInputs()
    {
        var created = await _teamsService.CreateMissionAsync(
            _appBindingService,
            _sessionService,
            _tempRoot,
            _workspaceCraftPath,
            new TeamsMissionCreateParams
            {
                Title = "Cancel me",
                Prompt = "This mission should stop."
            },
            CancellationToken.None);
        var leaderThread = Assert.Single(created.Team.MissionThreads, thread => thread.MemberId == "leader");
        var runningTurn = await _sessionService.StartSubAgentSyntheticTurnAsync(
            leaderThread.ThreadId,
            [new TextContent("still running")],
            "test",
            profileName: null,
            CancellationToken.None);
        await _sessionService.EnqueueTurnInputAsync(leaderThread.ThreadId, [new TextContent("queued follow-up")]);

        var view = await _teamsService.CancelMissionAsync(
            _sessionService,
            _workspaceCraftPath,
            new TeamsMissionCancelParams { MissionId = created.Mission.MissionId },
            CancellationToken.None);

        Assert.Equal("cancelled", Assert.Single(view.Missions).Status);
        Assert.Equal("cancelled", Assert.Single(view.MissionThreads).Status);
        Assert.Contains(_sessionService.CancelledTurns, item =>
            item.threadId == leaderThread.ThreadId && item.turnId == runningTurn.Id);
        Assert.Empty((await _sessionService.GetThreadAsync(leaderThread.ThreadId)).QueuedInputs);
    }

    private void AssertAppBindingAuditContains(string eventName)
    {
        var statePath = Path.Combine(_workspaceCraftPath, "app-bindings", "state.json");
        using var document = JsonDocument.Parse(File.ReadAllText(statePath));
        Assert.Contains(
            document.RootElement.GetProperty("audit").EnumerateArray(),
            audit => audit.GetProperty("event").GetString() == eventName);
    }

    private (string[] direct, string[] deferred) ReadBindingToolNames(string bindingId)
    {
        var statePath = Path.Combine(_workspaceCraftPath, "app-bindings", "state.json");
        using var document = JsonDocument.Parse(File.ReadAllText(statePath));
        var binding = document.RootElement.GetProperty("bindings").EnumerateArray()
            .Single(item => item.GetProperty("bindingId").GetString() == bindingId);
        return (
            binding.GetProperty("directToolNames").EnumerateArray().Select(item => item.GetString()!).ToArray(),
            binding.GetProperty("deferredToolNames").EnumerateArray().Select(item => item.GetString()!).ToArray());
    }

    private IReadOnlyList<string> Required(string toolName)
    {
        var spec = _teamsService.ToolSpecs.Single(item => item.Name == toolName);
        return spec.InputSchema!["required"] is JsonArray required
            ? required.Select(item => item!.GetValue<string>()).ToList()
            : [];
    }

    private JsonObject Property(string toolName, string propertyName)
    {
        var spec = _teamsService.ToolSpecs.Single(item => item.Name == toolName);
        return (JsonObject)((JsonObject)spec.InputSchema!["properties"]!)[propertyName]!;
    }

    private bool HasProperty(string toolName, string propertyName)
    {
        var spec = _teamsService.ToolSpecs.Single(item => item.Name == toolName);
        return ((JsonObject)spec.InputSchema!["properties"]!).ContainsKey(propertyName);
    }

    private ValueTask<AppBoundToolCallResult> InvokeTeamToolAsync(
        MissionThreadRecord missionThread,
        string toolName,
        JsonObject arguments) =>
        _teamsService.InvokeToolAsync(
            new ManagedAppBindingToolCallContext(
                _workspaceCraftPath,
                _tempRoot,
                missionThread.BindingId,
                missionThread.ThreadId,
                $"turn_{missionThread.MemberId}",
                $"call_{toolName}_{Guid.NewGuid():N}",
                TeamsConstants.AppId,
                missionThread.GrantId,
                toolName),
            arguments,
            CancellationToken.None);

    private static async Task<JsonDocument> AppListAsync(AppServerTestHarness harness, string surface)
    {
        await harness.ExecuteRequestAsync(harness.BuildRequest("app/list", new
        {
            includeDisabled = true,
            surface
        }));
        return await harness.Transport.ReadNextSentAsync();
    }

    private static string BundledPluginSourceRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(dir))
        {
            if (File.Exists(Path.Combine(dir, "dotcraft.sln")))
                return Path.Combine(dir, "desktop", "resources", "plugins", "dotcraft-bundled", "plugins");
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
