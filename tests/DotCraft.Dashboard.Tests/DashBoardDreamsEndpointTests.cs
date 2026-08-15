using DotCraft.Workspaces;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.DashBoard;
using DotCraft.Dreams;
using DotCraft.Memory;
using DotCraft.Tracing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using DotCraft.Sessions;
using SessionItem = DotCraft.Sessions.SessionItem;
using SessionThread = DotCraft.Sessions.SessionThread;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using AgentMessagePayload = DotCraft.Sessions.AgentMessagePayload;
using ThreadSource = DotCraft.Sessions.ThreadSource;
using UserMessagePayload = DotCraft.Sessions.UserMessagePayload;
using Xunit;

namespace DotCraft.Tests.DashBoard;

public sealed class DashBoardDreamsEndpointTests : IDisposable
{
    private readonly string _root;
    private readonly string _workspace;
    private readonly string _craft;
    private readonly ThreadStore _threadStore;
    private readonly MemoryStore _memoryStore;
    private readonly DreamStore _dreamStore;
    private readonly DreamsStateStore _stateStore;

    public DashBoardDreamsEndpointTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "DashBoardDreams_" + Guid.NewGuid().ToString("N")[..8]);
        _workspace = Path.Combine(_root, "workspace");
        _craft = Path.Combine(_workspace, ".craft");
        Directory.CreateDirectory(_craft);
        _threadStore = new ThreadStore(_craft);
        _memoryStore = new MemoryStore(_craft);
        _dreamStore = new DreamStore(_craft);
        _stateStore = new DreamsStateStore(_dreamStore);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { }
    }

    [Fact]
    public async Task DreamsEndpoints_RunListGetApplyAndArchive()
    {
        await SaveThreadAsync("thread_one");
        var config = CreateConfig();
        var deletedThreadIds = new List<string>();
        await using var dreamsService = CreateDreamsService(config);
        await using var app = await CreateDashboardApp(dreamsService, (threadId, _) =>
        {
            deletedThreadIds.Add(threadId);
            return Task.CompletedTask;
        });
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        using var status = await http.GetAsync("/dashboard/api/dreams/status");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        using var statusDoc = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        Assert.False(statusDoc.RootElement.GetProperty("autoApply").GetBoolean());

        using var created = await http.PostAsync("/dashboard/api/dreams/run", content: null);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var completed = await WaitForStateAsync(DreamsRunStatuses.Succeeded);
        Assert.Equal(DreamsReviewStatuses.Pending, completed.ReviewStatus);

        using var runs = await http.GetAsync("/dashboard/api/dreams/runs");
        Assert.Equal(HttpStatusCode.OK, runs.StatusCode);
        using var runsDoc = JsonDocument.Parse(await runs.Content.ReadAsStringAsync());
        var runId = runsDoc.RootElement.GetProperty("runs")[0].GetProperty("id").GetString()!;
        Assert.Equal(completed.Id, runId);

        using var details = await http.GetAsync($"/dashboard/api/dreams/runs/{runId}");
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        using var detailsDoc = JsonDocument.Parse(await details.Content.ReadAsStringAsync());
        Assert.Contains(
            "Dashboard focus",
            detailsDoc.RootElement.GetProperty("preview").GetProperty("outputIndexMarkdown").GetString(),
            StringComparison.Ordinal);

        using var applied = await http.PostAsync($"/dashboard/api/dreams/runs/{runId}/apply", content: null);
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        using var appliedDoc = JsonDocument.Parse(await applied.Content.ReadAsStringAsync());
        var outputStoreId = appliedDoc.RootElement.GetProperty("run").GetProperty("outputStoreId").GetString();
        Assert.Equal(DreamsReviewStatuses.Applied, appliedDoc.RootElement.GetProperty("run").GetProperty("reviewStatus").GetString());
        Assert.Equal(outputStoreId, appliedDoc.RootElement.GetProperty("activeDreamStoreId").GetString());

        using var archived = await http.PostAsync($"/dashboard/api/dreams/runs/{runId}/archive", content: null);
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        Assert.Equal(DreamsReviewStatuses.Archived, _stateStore.Load(runId)?.ReviewStatus);

        var activeStorePath = _dreamStore.GetStoreDescriptor(outputStoreId!).IndexPath;
        using var deleted = await http.DeleteAsync($"/dashboard/api/dreams/runs/{runId}");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        using var deletedDoc = JsonDocument.Parse(await deleted.Content.ReadAsStringAsync());
        Assert.True(deletedDoc.RootElement.GetProperty("deleted").GetBoolean());
        Assert.True(deletedDoc.RootElement.GetProperty("activeStorePreserved").GetBoolean());
        Assert.True(deletedDoc.RootElement.GetProperty("traceDeleted").GetBoolean());
        Assert.Null(_stateStore.Load(runId));
        Assert.True(File.Exists(activeStorePath));
        Assert.Contains("thread_dream_fake", deletedThreadIds);
    }

    [Fact]
    public async Task DreamsDeleteAll_IncludesArchivedRunsAndPreservesActiveStore()
    {
        await SaveThreadAsync("thread_one");
        var deletedThreadIds = new List<string>();
        await using var dreamsService = CreateDreamsService(CreateConfig());
        await using var app = await CreateDashboardApp(dreamsService, (threadId, _) =>
        {
            deletedThreadIds.Add(threadId);
            return Task.CompletedTask;
        });
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        using var firstRequest = await http.PostAsync("/dashboard/api/dreams/run", content: null);
        Assert.Equal(HttpStatusCode.OK, firstRequest.StatusCode);
        var first = await WaitForStateAsync(DreamsRunStatuses.Succeeded);
        _ = dreamsService.ApplyRun(first.Id);

        using var secondRequest = await http.PostAsync("/dashboard/api/dreams/run", content: null);
        Assert.Equal(HttpStatusCode.OK, secondRequest.StatusCode);
        var second = await WaitForDifferentStateAsync(first.Id, DreamsRunStatuses.Succeeded);
        _ = dreamsService.ArchiveRun(second.Id);

        var activeStorePath = _dreamStore.GetStoreDescriptor(first.OutputStoreId!).IndexPath;
        var inactiveStorePath = _dreamStore.GetStoreDescriptor(second.OutputStoreId!).DirectoryPath;
        using var deleted = await http.DeleteAsync("/dashboard/api/dreams/runs");

        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        using var deletedDoc = JsonDocument.Parse(await deleted.Content.ReadAsStringAsync());
        Assert.Equal(2, deletedDoc.RootElement.GetProperty("deletedCount").GetInt32());
        Assert.True(deletedDoc.RootElement.GetProperty("activeStorePreserved").GetBoolean());
        Assert.Null(_stateStore.Load());
        Assert.Empty(_stateStore.List(includeArchived: true));
        Assert.True(File.Exists(activeStorePath));
        Assert.False(Directory.Exists(inactiveStorePath));
        Assert.Equal(2, deletedThreadIds.Count);
    }

    private async Task<WebApplication> CreateDashboardApp(
        DreamsService dreamsService,
        Func<string, CancellationToken, Task>? deleteThreadAsync = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.MapDashBoard(
            new TraceStore(),
            new WorkspacePaths { WorkspacePath = _workspace, CraftPath = _craft },
            deleteThreadAsync: deleteThreadAsync,
            dreamStore: _dreamStore,
            dreamsService: dreamsService);
        app.Urls.Add($"http://127.0.0.1:{GetFreeTcpPort()}");
        await app.StartAsync();
        return app;
    }

    private DreamsService CreateDreamsService(AppConfig config)
    {
        var runner = new FakeRunner { Store = _dreamStore };
        var collector = new DreamsInputCollector(config, _workspace, _memoryStore, _dreamStore, _threadStore);
        return new DreamsService(config, collector, runner, _dreamStore, _stateStore);
    }

    private static AppConfig CreateConfig() => new()
    {
        Dreams = new DreamsConfig
        {
            Enabled = true,
            Interval = TimeSpan.FromHours(24),
            StartupDelay = TimeSpan.Zero,
            ThreadLookbackCount = 20,
            HistoryTailChars = 20_000,
            MinCompletedTurnsSinceLastRun = 1
        }
    };

    private async Task<DreamsRunState> WaitForStateAsync(string status)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var state = _stateStore.Load();
            if (state?.Status == status)
                return state;
            await Task.Delay(50);
        }

        Assert.Equal(status, _stateStore.Load()?.Status);
        throw new InvalidOperationException("Unreachable.");
    }

    private async Task<DreamsRunState> WaitForDifferentStateAsync(string previousRunId, string status)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var state = _stateStore.Load();
            if (state?.Id != previousRunId && state?.Status == status)
                return state;
            await Task.Delay(50);
        }

        Assert.NotEqual(previousRunId, _stateStore.Load()?.Id);
        Assert.Equal(status, _stateStore.Load()?.Status);
        throw new InvalidOperationException("Unreachable.");
    }

    private async Task SaveThreadAsync(string id)
    {
        var now = DateTimeOffset.UtcNow;
        var user = new SessionItem
        {
            Id = "item_user",
            TurnId = "turn_001",
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            Payload = new UserMessagePayload { Text = "Please organize Dashboard Dreams review." },
            CreatedAt = now
        };
        var assistant = new SessionItem
        {
            Id = "item_agent",
            TurnId = "turn_001",
            Type = ItemType.AgentMessage,
            Status = ItemStatus.Completed,
            Payload = new AgentMessagePayload { Text = "Dreams can create review stores." },
            CreatedAt = now
        };
        await _threadStore.SaveThreadAsync(new SessionThread
        {
            Id = id,
            WorkspacePath = _workspace,
            OriginChannel = "desktop",
            DisplayName = "Dashboard Dreams test",
            Source = ThreadSource.User(),
            Status = ThreadStatus.Active,
            CreatedAt = now.AddMinutes(-5),
            LastActiveAt = now,
            HistoryMode = HistoryMode.Server,
            Turns =
            [
                new SessionTurn
                {
                    Id = "turn_001",
                    ThreadId = id,
                    Status = TurnStatus.Completed,
                    Input = user,
                    Items = [user, assistant],
                    StartedAt = now.AddMinutes(-1),
                    CompletedAt = now
                }
            ]
        });
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string ValidDreamMarkdown() =>
        """
        # Dream Memory

        Generated by scheduled Dreams from recent workspace sessions. Treat as inferred background context, not explicit user instruction.

        ## Workspace Focus
        - Dashboard focus.

        ## Active Threads And Open Loops

        ## Inferred Project Conventions

        ## Repeated Problems And Prior Mistakes

        ## Latest Stable Understanding

        ## Low-Signal Or One-Off Context To Ignore
        """;

    private sealed class FakeRunner : IDreamsRunner
    {
        public string ModelId => "fake-dream-model";

        public DreamStore? Store { get; init; }

        public async Task<DreamsGenerationResult> GenerateAsync(
            DreamsRunInput input,
            string runId,
            string trigger,
            string? outputStoreId = null,
            string? modelId = null,
            Action<DreamsRunSessionBinding>? onSessionBinding = null,
            CancellationToken cancellationToken = default)
        {
            _ = trigger;
            _ = modelId;
            onSessionBinding?.Invoke(new DreamsRunSessionBinding("thread_dream_fake", "turn_dream_fake"));
            string? createdOutputStoreId = null;
            if (Store != null)
            {
                var store = Store.CreateOutputStore(runId, DateTimeOffset.UtcNow);
                await File.WriteAllTextAsync(store.IndexPath, ValidDreamMarkdown(), cancellationToken);
                createdOutputStoreId = store.StoreId;
            }

            return DreamsGenerationResult.Success(
                ValidDreamMarkdown(),
                "Dreams processed test input.",
                "thread_dream_fake",
                "turn_dream_fake",
                outputStoreId: outputStoreId ?? createdOutputStoreId,
                diagnostics: new DreamsRunDiagnostics(input.Threads.Count, [], 0, 0));
        }
    }
}
