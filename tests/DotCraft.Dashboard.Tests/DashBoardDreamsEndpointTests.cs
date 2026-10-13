using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.DashBoard;
using DotCraft.Dreams;
using DotCraft.Memory;
using DotCraft.Sessions;
using DotCraft.Tracing;
using DotCraft.Workspaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DotCraft.Tests.DashBoard;

public sealed class DashBoardDreamsEndpointTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DashBoardDreams_" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _workspace;
    private readonly string _craft;
    private readonly string _store;
    private readonly AppConfig _config = new() { Dreams = new DreamsConfig { Enabled = true, StartupDelay = TimeSpan.Zero } };

    public DashBoardDreamsEndpointTests()
    {
        _workspace = Path.Combine(_root, "workspace");
        _craft = Path.Combine(_workspace, ".craft");
        _store = MemoryStore.DirectoryOf(_craft);
        Directory.CreateDirectory(_store);
        File.WriteAllText(Path.Combine(_store, "deploy.md"), "Deploy on Fridays.");
        File.WriteAllText(Path.Combine(_store, "MEMORY.md"), "- [Deploy](deploy.md) — deploy day");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { }
    }

    [Fact]
    public async Task DreamsEndpoints_RunReviewApplyRestoreAndArchive()
    {
        await using var service = CreateService();
        await using var app = await CreateDashboardAppAsync(service);
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        using var started = await http.PostAsync("/dashboard/api/dreams/run", content: null);
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        var runId = await WaitForFinishedRunAsync(service);

        using var status = await ReadJsonAsync(http, "/dashboard/api/dreams/status");
        Assert.Equal(1, status.RootElement.GetProperty("pendingCount").GetInt32());
        Assert.False(status.RootElement.GetProperty("autoApply").GetBoolean());

        using var details = await ReadJsonAsync(http, $"/dashboard/api/dreams/runs/{runId}");
        var file = Assert.Single(details.RootElement.GetProperty("files").EnumerateArray());
        Assert.Equal("deploy.md", file.GetProperty("name").GetString());
        Assert.Equal("Deploy on Fridays.", file.GetProperty("baseline").GetString());
        Assert.Equal("Deploy on Thursdays.", file.GetProperty("candidate").GetString());
        Assert.Contains("deploy.md", details.RootElement.GetProperty("manifest").GetString(), StringComparison.Ordinal);

        using var applied = await http.PostAsync($"/dashboard/api/dreams/runs/{runId}/apply", content: null);
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        Assert.Equal("Deploy on Thursdays.", await File.ReadAllTextAsync(Path.Combine(_store, "deploy.md")));

        using var restored = await http.PostAsync($"/dashboard/api/dreams/runs/{runId}/restore", content: null);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.Equal(DreamsReviewStatuses.Discarded, service.LoadRun(runId)!.ReviewStatus);
        Assert.Equal("Deploy on Fridays.", await File.ReadAllTextAsync(Path.Combine(_store, "deploy.md")));

        using var archived = await http.PostAsync($"/dashboard/api/dreams/runs/{runId}/archive", content: null);
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        using var runs = await ReadJsonAsync(http, "/dashboard/api/dreams/runs");
        Assert.Equal(runId, Assert.Single(runs.RootElement.GetProperty("runs").EnumerateArray()).GetProperty("id").GetString());
    }

    [Fact]
    public async Task DreamsApply_WhenTheStoreChanged_ReturnsConflict()
    {
        await using var service = CreateService();
        await using var app = await CreateDashboardAppAsync(service);
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        await service.RequestRunAsync();
        var runId = await WaitForFinishedRunAsync(service);
        File.WriteAllText(Path.Combine(_store, "deploy.md"), "Edited by the user.");

        using var applied = await http.PostAsync($"/dashboard/api/dreams/runs/{runId}/apply", content: null);

        Assert.Equal(HttpStatusCode.Conflict, applied.StatusCode);
        Assert.Equal(DreamsReviewStatuses.Pending, service.LoadRun(runId)!.ReviewStatus);
        Assert.Equal("Edited by the user.", await File.ReadAllTextAsync(Path.Combine(_store, "deploy.md")));
    }

    [Fact]
    public async Task DreamsDelete_RemovesRunsAndTheirTracesButNeverTheStore()
    {
        var deletedThreadIds = new List<string>();
        await using var service = CreateService();
        await using var app = await CreateDashboardAppAsync(service, (threadId, _) =>
        {
            deletedThreadIds.Add(threadId);
            return Task.CompletedTask;
        });
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        await service.RequestRunAsync();
        var first = await WaitForFinishedRunAsync(service);
        service.ApplyRun(first);
        await service.RequestRunAsync();
        var second = await WaitForFinishedRunAsync(service, first);
        service.ArchiveRun(second);

        using var missing = await http.DeleteAsync("/dashboard/api/dreams/runs/dream_missing");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        using var single = await http.DeleteAsync($"/dashboard/api/dreams/runs/{first}");
        Assert.Equal(HttpStatusCode.OK, single.StatusCode);
        using (var singleDoc = JsonDocument.Parse(await single.Content.ReadAsStringAsync()))
        {
            Assert.Equal([first], singleDoc.RootElement.GetProperty("deletedRunIds").EnumerateArray().Select(static id => id.GetString()));
            Assert.Equal(1, singleDoc.RootElement.GetProperty("deletedCount").GetInt32());
            Assert.False(singleDoc.RootElement.GetProperty("partial").GetBoolean());
            Assert.Empty(singleDoc.RootElement.GetProperty("traceCleanupFailures").EnumerateArray());
        }

        using var all = await http.DeleteAsync("/dashboard/api/dreams/runs");
        Assert.Equal(HttpStatusCode.OK, all.StatusCode);
        using (var allDoc = JsonDocument.Parse(await all.Content.ReadAsStringAsync()))
            Assert.Equal(1, allDoc.RootElement.GetProperty("deletedCount").GetInt32());

        Assert.Empty(service.ListRuns(includeArchived: true));
        Assert.Null(service.LoadLatestState());
        Assert.Equal(2, deletedThreadIds.Count);
        Assert.Equal("Deploy on Thursdays.", await File.ReadAllTextAsync(Path.Combine(_store, "deploy.md")));
    }

    private DreamsService CreateService() =>
        new(_config, new DotCraftPaths(_workspace, _craft, userDataPath: null), new ThreadStore(_craft), new CandidateEditingRunner());

    private async Task<WebApplication> CreateDashboardAppAsync(
        DreamsService dreamsService,
        Func<string, CancellationToken, Task>? deleteThreadAsync = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.MapDashBoard(
            new TraceStore(),
            new DotCraftPaths(_workspace, _craft, userDataPath: null),
            deleteThreadAsync: deleteThreadAsync,
            dreamsService: dreamsService);
        app.Urls.Add($"http://127.0.0.1:{GetFreeTcpPort()}");
        await app.StartAsync();
        return app;
    }

    private static async Task<string> WaitForFinishedRunAsync(DreamsService service, string? previousRunId = null)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (service.LoadLatestState() is { IsRunning: false } state && state.Id != previousRunId)
                return state.Id;
            await Task.Delay(25);
        }

        throw new Xunit.Sdk.XunitException("The Dream run did not finish.");
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpClient http, string path)
    {
        using var response = await http.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class CandidateEditingRunner : IDreamsRunner
    {
        public string ModelId => "fake-dream-model";

        public Task<DreamsGenerationResult> GenerateAsync(
            DreamsRunInput input,
            string modelId,
            Action<DreamsRunSessionBinding>? onSessionBinding = null,
            CancellationToken cancellationToken = default)
        {
            var threadId = "thread_" + input.RunId;
            onSessionBinding?.Invoke(new DreamsRunSessionBinding(threadId, "turn_dream"));
            File.WriteAllText(Path.Combine(input.CandidateDirectory, "deploy.md"), "Deploy on Thursdays.");
            return Task.FromResult(new DreamsGenerationResult(true, "Moved deploys to Thursdays.", ThreadId: threadId, TurnId: "turn_dream"));
        }
    }
}
