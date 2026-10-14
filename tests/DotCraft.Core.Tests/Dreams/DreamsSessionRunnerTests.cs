using DotCraft.Configuration;
using DotCraft.Dreams;
using DotCraft.Security;
using DotCraft.Tools;
using DotCraft.Tests.Sessions.Protocol.AppServer;
using DotCraft.Sessions;
using ModelPreference = DotCraft.Configuration.ModelPreference;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using Xunit;

namespace DotCraft.Tests.Dreams;

public sealed class DreamsSessionRunnerTests : IDisposable
{
    private readonly DreamsTestWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task GenerateAsync_RunsOneTurnOnAnInternalThreadAndArchivesIt()
    {
        var registry = new DreamsRunRegistry(_workspace.Threads);
        var submits = 0;
        var toolNames = new List<string>();
        var sessionService = new TestableSessionService(_workspace.Threads)
        {
            CreateThreadHandler = async (thread, _) =>
            {
                var registrations = await new DreamsToolSource(registry, new AppConfig(), new PathBlacklist([]))
                    .GetRegistrationsAsync(new ToolPlanningContext(thread.Id, null, _workspace.WorkspacePath, _workspace.CraftPath, "agent", DreamsConstants.ToolProfileName, [], 1));
                toolNames.AddRange(registrations.Select(static registration => registration.Definition.Name.Name));
            },
            SubmitInputHandler = (threadId, _, _) =>
            {
                submits++;
                return TurnEvents(threadId, "Merged two deploy memories.");
            }
        };
        var config = new AppConfig
        {
            ProviderId = "test",
            ProviderPreferences = new() { ["test"] = new ModelPreference { Model = "fake-main" } },
            ConsolidationModel = "fake-consolidation"
        };
        var runner = new DreamsSessionRunner(
            sessionService,
            new SessionPersistenceService(_workspace.Threads),
            TestModelProviderRegistry.Create(),
            config,
            registry);
        var bindings = new List<DreamsRunSessionBinding>();

        var result = await runner.GenerateAsync(Input(), runner.ModelId, bindings.Add);

        Assert.True(result.Succeeded);
        Assert.Equal("Merged two deploy memories.", result.Summary);
        Assert.Equal("turn_001", result.TurnId);
        Assert.Equal(1, submits);
        Assert.Contains(bindings, binding => binding.TurnId == "turn_001");
        Assert.Contains("DeleteMemoryFile", toolNames);
        Assert.Contains("SearchTranscripts", toolNames);
        Assert.False(registry.TryGet(result.ThreadId, out _));

        var saved = await _workspace.Threads.LoadThreadAsync(result.ThreadId!);
        Assert.NotNull(saved);
        Assert.Equal(ThreadStatus.Archived, saved.Status);
        Assert.Equal(DreamsConstants.ChannelName, saved.OriginChannel);
        Assert.True(ThreadVisibility.IsInternal(saved));
        Assert.Equal("dream_run_001", saved.Metadata[DreamsConstants.RunIdMetadataKey]);
        Assert.Equal(DreamsConstants.ToolProfileName, saved.Configuration?.ToolProfile);
        Assert.True(saved.Configuration?.UseToolProfileOnly);
        Assert.False(saved.Configuration?.MemoryEnabled);
        Assert.Equal(ApprovalPolicy.AutoApprove, saved.Configuration?.ApprovalPolicy);
        Assert.Equal("fake-consolidation", saved.Configuration?.Model);
    }

    private DreamsRunInput Input() => new(
        "dream_run_001",
        DreamsConstants.TriggerManual,
        _workspace.WorkspacePath,
        _workspace.Artifacts.BaselineDirectory("dream_run_001"),
        _workspace.Artifacts.CandidateDirectory("dream_run_001"),
        "manifest",
        [],
        null);

    private static SessionEvent[] TurnEvents(string threadId, string summary)
    {
        var started = new SessionTurn { Id = "turn_001", ThreadId = threadId, Status = TurnStatus.Running, StartedAt = DateTimeOffset.UtcNow };
        var completed = new SessionTurn
        {
            Id = "turn_001",
            ThreadId = threadId,
            Status = TurnStatus.Completed,
            StartedAt = started.StartedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            Items =
            [
                new SessionItem
                {
                    Id = "item_summary",
                    TurnId = "turn_001",
                    Type = ItemType.AgentMessage,
                    Status = ItemStatus.Completed,
                    Payload = new AgentMessagePayload { Text = summary },
                    CreatedAt = DateTimeOffset.UtcNow
                }
            ]
        };
        return
        [
            new SessionEvent { EventType = SessionEventType.TurnStarted, ThreadId = threadId, TurnId = "turn_001", Payload = started },
            new SessionEvent { EventType = SessionEventType.TurnCompleted, ThreadId = threadId, TurnId = "turn_001", Payload = completed }
        ];
    }
}
