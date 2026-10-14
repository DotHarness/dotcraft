using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Dreams;
using DotCraft.Memory;
using DotCraft.AppServer;
using DotCraft.Sessions;
using DotCraft.Tests.Dreams;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerMemoryResetTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"appserver_memory_reset_{Guid.NewGuid():N}");
    private readonly string _workspacePath;
    private readonly string _workspaceCraftPath;

    public AppServerMemoryResetTests()
    {
        _workspacePath = Path.Combine(_tempRoot, "workspace");
        _workspaceCraftPath = Path.Combine(_workspacePath, ".craft");
        Directory.CreateDirectory(_workspaceCraftPath);
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
    public async Task MemoryReset_NoParams_ReturnsEmptyObjectAndClearsMemoryAndDreams()
    {
        var memoryStore = new MemoryStore(_workspaceCraftPath);
        var dreams = new DreamsArtifacts(_workspaceCraftPath);
        File.WriteAllText(memoryStore.IndexFilePath, "remember this");
        File.WriteAllText(Path.Combine(memoryStore.MemoryDirectoryPath, "HISTORY.md"), "legacy event");
        DreamsTestWorkspace.WriteFile(dreams.DreamsDirectoryPath, "state.json", "{}");
        DreamsTestWorkspace.WriteFile(dreams.CandidateDirectory("dream_old"), "fact.md", "candidate");
        var derivedDir = Path.Combine(memoryStore.MemoryDirectoryPath, "derived");
        Directory.CreateDirectory(derivedDir);
        await File.WriteAllTextAsync(Path.Combine(derivedDir, "snapshot.json"), "{}");
        var automationStore = MemoryScopes.ForThread(
            new ThreadConfiguration { MemoryScope = "nightly-report" }, memoryStore, _workspaceCraftPath);
        File.WriteAllText(automationStore.IndexFilePath, "- [Report](report.md) — format");
        File.WriteAllText(Path.Combine(automationStore.MemoryDirectoryPath, "report.md"), "Use tables.");

        var configPath = Path.Combine(_workspaceCraftPath, "config.json");
        await File.WriteAllTextAsync(
            configPath,
            """
            {
              "Memory": {
                "Enabled": false
              }
            }
            """);

        var monitor = new AppConfigMonitor(new AppConfig
        {
            Memory = new MemoryConfig
            {
                Enabled = false
            }
        });
        var welcomeSuggestions = new FakeWelcomeSuggestionService();
        using var harness = new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            appConfigMonitor: monitor,
            memoryStore: memoryStore,
            welcomeSuggestionService: welcomeSuggestions);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);

        await harness.ExecuteRequestAsync(BuildMemoryResetWithoutParams());

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        var response = Assert.Single(sent, d => d.RootElement.TryGetProperty("result", out _));
        AppServerTestHarness.AssertIsSuccessResponse(response);
        Assert.Equal(JsonValueKind.Object, response.RootElement.GetProperty("result").ValueKind);
        Assert.Empty(response.RootElement.GetProperty("result").EnumerateObject());
        AssertSingleConfigChanged(sent, DotCraft.Protocol.AppServer.AppServerMethodNames.MemoryReset, ConfigChangeRegions.Memory);

        Assert.True(Directory.Exists(memoryStore.MemoryDirectoryPath));
        Assert.Empty(Directory.EnumerateFileSystemEntries(memoryStore.MemoryDirectoryPath));
        Assert.True(Directory.Exists(automationStore.MemoryDirectoryPath));
        Assert.Empty(Directory.EnumerateFileSystemEntries(automationStore.MemoryDirectoryPath));
        Assert.True(Directory.Exists(dreams.DreamsDirectoryPath));
        Assert.Empty(Directory.EnumerateFileSystemEntries(dreams.DreamsDirectoryPath));
        Assert.True(File.Exists(configPath));
        Assert.False(harness.Monitor.Current.Memory.Enabled);

        var threads = await harness.Service.FindThreadsAsync(harness.Identity);
        Assert.Contains(threads, summary => summary.Id == thread.Id);
        Assert.Single(welcomeSuggestions.ClearedWorkspacePaths);
    }

    [Fact]
    public async Task MemoryReset_CancelsTheActiveDreamRunAndClearsItsRuns()
    {
        using var workspace = new DreamsTestWorkspace();
        workspace.WriteMemory("deploy", "Deploy on Fridays.");
        var runner = new FakeDreamsRunner { BlockUntilReleased = true };
        await using var dreamsService = workspace.CreateService(runner);
        using var harness = new AppServerTestHarness(
            workspaceCraftPath: workspace.CraftPath,
            appConfigMonitor: new AppConfigMonitor(workspace.Config),
            memoryStore: new MemoryStore(workspace.CraftPath),
            dreamsService: dreamsService);
        await harness.InitializeAsync();
        await dreamsService.RequestRunAsync();
        await runner.WaitUntilCalledAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.MemoryReset, new { }));

        AppServerTestHarness.AssertIsSuccessResponse(await harness.Transport.ReadNextSentAsync());
        Assert.True(runner.Canceled);
        Assert.False(dreamsService.IsRunning);
        Assert.Null(dreamsService.LoadLatestState());
        Assert.Empty(dreamsService.ListRuns(includeArchived: true));
        Assert.Empty(Directory.EnumerateFileSystemEntries(workspace.WorkspaceStore));
    }

    [Fact]
    public async Task MemoryReset_ObjectParams_ReturnsEmptyObject()
    {
        var memoryStore = new MemoryStore(_workspaceCraftPath);
        File.WriteAllText(memoryStore.IndexFilePath, "remember this");
        using var harness = new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            memoryStore: memoryStore);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.MemoryReset, new { }));

        var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        Assert.Empty(response.RootElement.GetProperty("result").EnumerateObject());
        Assert.Empty(Directory.EnumerateFileSystemEntries(memoryStore.MemoryDirectoryPath));
    }

    [Fact]
    public async Task MemoryReset_InvalidParams_ReturnsInvalidParams()
    {
        var memoryStore = new MemoryStore(_workspaceCraftPath);
        using var harness = new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            memoryStore: memoryStore);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.MemoryReset, "bad"));

        var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.InvalidParamsCode);
    }

    private static AppServerIncomingMessage BuildMemoryResetWithoutParams()
    {
        using var idDoc = JsonDocument.Parse("1");
        return new AppServerIncomingMessage
        {
            JsonRpc = "2.0",
            Id = idDoc.RootElement.Clone(),
            Method = DotCraft.Protocol.AppServer.AppServerMethodNames.MemoryReset
        };
    }

    private static IDisposable AttachConfigChangedBridge(AppServerTestHarness harness)
    {
        void OnChanged(object? sender, AppConfigChangedEventArgs change)
        {
            if (!harness.Connection.SupportsConfigChange || !harness.Connection.ShouldSendNotification(DotCraft.Protocol.AppServer.AppServerMethodNames.ConfigChanged))
                return;

            var notification = new
            {
                jsonrpc = "2.0",
                method = DotCraft.Protocol.AppServer.AppServerMethodNames.ConfigChanged,
                @params = new DotCraft.Protocol.AppServer.ConfigChangedParams
                {
                    Source = change.Source,
                    Regions = change.Regions.ToArray(),
                    ChangedAt = change.ChangedAt
                }
            };
            harness.Transport.WriteMessageAsync(notification).GetAwaiter().GetResult();
        }

        harness.Monitor.Changed += OnChanged;
        return new ActionOnDispose(() => harness.Monitor.Changed -= OnChanged);
    }

    private static void AssertSingleConfigChanged(
        IReadOnlyList<JsonDocument> sent,
        string expectedSource,
        string expectedRegion)
    {
        var notifications = sent
            .Where(d =>
                d.RootElement.TryGetProperty("method", out var method)
                && string.Equals(method.GetString(), DotCraft.Protocol.AppServer.AppServerMethodNames.ConfigChanged, StringComparison.Ordinal))
            .ToList();
        Assert.Single(notifications);

        var payload = notifications[0].RootElement.GetProperty("params");
        Assert.Equal(expectedSource, payload.GetProperty("source").GetString());
        Assert.Contains(expectedRegion, payload.GetProperty("regions").EnumerateArray().Select(v => v.GetString()));
        _ = payload.GetProperty("changedAt").GetDateTimeOffset();
    }

    private sealed class FakeWelcomeSuggestionService : IWelcomeSuggester
    {
        public List<string> ClearedWorkspacePaths { get; } = [];

        public void ScheduleRefresh(string workspacePath, string? triggerThreadId = null)
        {
        }

        public void ClearWorkspaceCache(string workspacePath)
        {
            ClearedWorkspacePaths.Add(workspacePath);
        }

        public Task<WelcomeSuggestionSnapshot> SuggestAsync(
            WelcomeSuggestionRequest parameters,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new WelcomeSuggestionSnapshot());
    }

    private sealed class ActionOnDispose(Action disposeAction) : IDisposable
    {
        public void Dispose() => disposeAction();
    }
}
