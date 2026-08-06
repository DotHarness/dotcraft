using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Dreams;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Tools;
using DotCraft.Tests.Sessions.Protocol.AppServer;
using Microsoft.Extensions.AI;
using DotCraft.Sessions;
using ModelPreference = DotCraft.Configuration.ModelPreference;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using Xunit;

namespace DotCraft.Tests.Dreams;

public sealed class DreamsSessionRunnerTests : IDisposable
{
    private readonly string _root;
    private readonly string _workspace;
    private readonly string _craft;
    private readonly ThreadStore _threadStore;

    public DreamsSessionRunnerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "DreamsRunner_" + Guid.NewGuid().ToString("N")[..8]);
        _workspace = Path.Combine(_root, "workspace");
        _craft = Path.Combine(_workspace, ".craft");
        Directory.CreateDirectory(_craft);
        _threadStore = new ThreadStore(_craft);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { }
    }

    [Fact]
    public async Task GenerateAsync_CreatesInternalThreadRunsTwoPassesAndArchivesThread()
    {
        var dreamStore = new DreamStore(_craft);
        var memoryStore = new MemoryStore(_craft);
        var runRegistry = new DreamsRunRegistry(_threadStore, memoryStore, dreamStore);
        var submitCalls = 0;
        var sessionService = new TestableSessionService(_threadStore)
        {
            CreateThreadHandler = (thread, _) =>
            {
                var threadConfig = thread.Configuration;
                if (threadConfig == null
                    || !string.Equals(threadConfig.ToolProfile, DreamsConstants.ToolProfileName, StringComparison.Ordinal))
                    return Task.CompletedTask;

                var source = new DreamsToolSource(runRegistry, new AppConfig(), new PathBlacklist([]));
                var registrations = source.GetRegistrationsAsync(
                    new ToolPlanningContext(thread.Id, null, _workspace, "agent", "dreams", [], 1)).AsTask().GetAwaiter().GetResult();
                if (threadConfig.UseToolProfileOnly && registrations.Count == 0)
                    throw new InvalidOperationException("UseToolProfileOnly requires a registered ToolProfile with at least one tool.");

                return Task.CompletedTask;
            },
            SubmitInputHandler = (threadId, content, _) =>
            {
                submitCalls++;
                if (submitCalls == 2)
                {
                    var outputStore = Directory.EnumerateDirectories(dreamStore.StoresDirectoryPath).Single();
                    File.WriteAllText(Path.Combine(outputStore, "INDEX.md"), ValidDreamMarkdown());
                }

                return CreateDreamTurnEvents(threadId, $"turn_{submitCalls:000}");
            }
        };
        var config = new AppConfig
        {
            ProviderId = "test",
            ProviderPreferences = new() { ["test"] = new ModelPreference { Model = "fake-main"  } },
            ConsolidationModel = "fake-consolidation"
        };
        var runner = new DreamsSessionRunner(
            sessionService,
            new SessionPersistenceService(_threadStore),
            TestModelProviderRegistry.Create(),
            config,
            _workspace,
            runRegistry,
            dreamStore);

        var result = await runner.GenerateAsync(
            new DreamsRunInput(
                ExplicitMemory: "Explicit memory",
                ExistingDream: "",
                TopicFiles: [],
                Threads:
                [
                    new DreamsThreadInput(
                        "thread_history",
                        "History thread",
                        "cli",
                        ThreadStatus.Active,
                        DateTimeOffset.UtcNow.AddMinutes(-2),
                        DateTimeOffset.UtcNow,
                        2)
                ],
                CompletedTurnCount: 2),
            runId: "dream_run_001",
            trigger: DreamsConstants.TriggerManual);

        Assert.True(result.Succeeded);
        Assert.Equal("turn_002", result.TurnId);
        Assert.Equal(["turn_001", "turn_002"], result.TurnIds);
        Assert.Equal(
            NormalizeNewlines(ValidDreamMarkdown()).Trim(),
            NormalizeNewlines(result.DreamMarkdown ?? string.Empty).Trim());
        Assert.Null(result.HistoryEntry);
        Assert.False(string.IsNullOrWhiteSpace(result.OutputStoreId));
        Assert.Equal(1, result.Diagnostics?.CandidateThreadCount);
        Assert.False(string.IsNullOrWhiteSpace(result.ThreadId));

        var saved = await _threadStore.LoadThreadAsync(result.ThreadId!);
        Assert.NotNull(saved);
        Assert.Equal(ThreadStatus.Archived, saved.Status);
        Assert.Equal(DreamsConstants.ChannelName, saved.OriginChannel);
        Assert.Equal(DreamsConstants.InternalMetadataValue, saved.Metadata[ThreadVisibility.InternalMetadataKey]);
        Assert.Equal("dream_run_001", saved.Metadata[DreamsConstants.RunIdMetadataKey]);
        Assert.Equal(DreamsConstants.TriggerManual, saved.Metadata[DreamsConstants.TriggerMetadataKey]);
        Assert.True(ThreadVisibility.IsInternal(saved));
        Assert.Equal(DreamsConstants.ToolProfileName, saved.Configuration?.ToolProfile);
        Assert.True(saved.Configuration?.UseToolProfileOnly);
        Assert.Equal(ApprovalPolicy.AutoApprove, saved.Configuration?.ApprovalPolicy);
        Assert.Equal("fake-consolidation", saved.Configuration?.Model);

        var prompt = Assert.IsType<TextContent>(Assert.Single(sessionService.LastSubmittedContent)).Text;
        Assert.Contains("Dream Run consolidation pass", prompt, StringComparison.Ordinal);
        Assert.Contains("PRUNING_NOTES.md", prompt, StringComparison.Ordinal);
        Assert.Equal(2, submitCalls);
    }

    private static SessionEvent[] CreateDreamTurnEvents(string threadId, string turnId)
    {
        var turn = new SessionTurn
        {
            Id = turnId,
            ThreadId = threadId,
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        };
        var completed = new SessionTurn
        {
            Id = turn.Id,
            ThreadId = threadId,
            Status = TurnStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            StartedAt = turn.StartedAt,
            Items = []
        };
        return
        [
            new SessionEvent
            {
                EventType = SessionEventType.TurnStarted,
                ThreadId = threadId,
                TurnId = turn.Id,
                Payload = turn
            },
            new SessionEvent
            {
                EventType = SessionEventType.TurnCompleted,
                ThreadId = threadId,
                TurnId = turn.Id,
                Payload = completed
            }
        ];
    }

    private static string ValidDreamMarkdown() =>
        """
        # Dream Memory

        Generated by scheduled Dreams from recent workspace sessions. Treat as inferred background context, not explicit user instruction.

        ## Workspace Focus
        - Test focus.

        ## Active Threads And Open Loops

        ## Inferred Project Conventions

        ## Repeated Problems And Prior Mistakes

        ## Latest Stable Understanding

        ## Low-Signal Or One-Off Context To Ignore
        """;

    private static string NormalizeNewlines(string value) => value.Replace("\r\n", "\n");
}
