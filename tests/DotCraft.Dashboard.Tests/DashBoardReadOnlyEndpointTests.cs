using DotCraft.Workspaces;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using DotCraft.DashBoard;
using DotCraft.Persistence;
using DotCraft.Tracing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.DashBoard;

public sealed class DashBoardReadOnlyEndpointTests : IDisposable
{
    private readonly string _root;
    private readonly string _workspace;
    private readonly string _craft;

    public DashBoardReadOnlyEndpointTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "DashBoardReadOnly_" + Guid.NewGuid().ToString("N")[..8]);
        _workspace = Path.Combine(_root, "workspace");
        _craft = Path.Combine(_workspace, ".craft");
        Directory.CreateDirectory(_craft);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { }
    }

    [Fact]
    public async Task RuntimeEndpoint_ReportsReadOnlyCapabilities()
    {
        await using var app = await CreateDashboardApp();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        using var response = await http.GetAsync("/dashboard/api/runtime");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal("readOnly", root.GetProperty("mode").GetString());
        Assert.True(root.GetProperty("readOnly").GetBoolean());
        Assert.Equal(_workspace, root.GetProperty("workspacePath").GetString());
        var capabilities = root.GetProperty("capabilities");
        Assert.False(capabilities.GetProperty("settings").GetBoolean());
        Assert.False(capabilities.GetProperty("dreams").GetBoolean());
        Assert.False(capabilities.GetProperty("automations").GetBoolean());
        Assert.False(capabilities.GetProperty("sessionDeletion").GetBoolean());
    }

    [Theory]
    [InlineData("DELETE", "/dashboard/api/sessions/thread_one")]
    [InlineData("DELETE", "/dashboard/api/sessions")]
    [InlineData("GET", "/dashboard/api/config/schema")]
    [InlineData("POST", "/dashboard/api/config/workspace")]
    [InlineData("GET", "/dashboard/api/dreams/status")]
    [InlineData("POST", "/dashboard/api/dreams/run")]
    [InlineData("GET", "/dashboard/api/orchestrators/automations/state")]
    [InlineData("POST", "/dashboard/api/orchestrators/automations/refresh")]
    public async Task ReadOnlyDashboard_DoesNotExposeMutationOrDisabledFeatureRoutes(string method, string path)
    {
        await using var app = await CreateDashboardApp();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        using var response = await http.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
            $"Expected {path} to be unavailable, got {(int)response.StatusCode}.");
    }

    [Fact]
    public void ReadOnlyStoreLoader_WhenStateDbIsMissing_Throws()
    {
        var stateDbPath = Path.Combine(_craft, "state.db");
        if (File.Exists(stateDbPath))
            File.Delete(stateDbPath);

        var exception = Assert.Throws<FileNotFoundException>(
            () => DashBoardReadOnlyStoreLoader.Load(_craft));

        Assert.Equal(stateDbPath, exception.FileName);
    }

    [Fact]
    public void ReadOnlyStoreLoader_ReadsExistingStateDbTraces()
    {
        var stateRuntime = new WorkspaceStateDatabase(_craft);
        var writer = new TraceStore(stateRuntime, 5000, synchronousPersist: true);
        writer.Record(new TraceEvent
        {
            SessionKey = "thread_smoke",
            Type = TraceEventType.Request,
            Content = "compact smoke request",
            Timestamp = DateTimeOffset.UtcNow
        });

        var stores = DashBoardReadOnlyStoreLoader.Load(_craft);

        Assert.Contains(stores.TraceStore.GetSessions(), session => session.SessionKey == "thread_smoke");
        Assert.Contains(
            stores.TraceStore.GetEvents("thread_smoke"),
            evt => evt.Type == TraceEventType.Request && evt.Content == "compact smoke request");
    }

    [Fact]
    public async Task ReadOnlyDashboard_ExposesPagedTraceEventsFromStateDb()
    {
        var stateRuntime = new WorkspaceStateDatabase(_craft);
        var writer = new TraceStore(stateRuntime, 5000, synchronousPersist: true);
        var startedAt = new DateTimeOffset(2026, 5, 29, 2, 0, 0, TimeSpan.Zero);
        writer.Record(new TraceEvent
        {
            SessionKey = "thread_page",
            Type = TraceEventType.Request,
            Content = "older",
            Timestamp = startedAt
        });
        writer.Record(new TraceEvent
        {
            SessionKey = "thread_page",
            Type = TraceEventType.MaintenanceForkRequest,
            Content = "maintenance",
            Timestamp = startedAt.AddSeconds(1)
        });
        writer.Record(new TraceEvent
        {
            SessionKey = "thread_page",
            Type = TraceEventType.ProviderResponseDiagnostic,
            Content = "openai-responses stream attempt 1: failed",
            Timestamp = startedAt.AddSeconds(2),
            MetadataJson = """{"eventType":"stream_attempt","attemptNumber":1,"outcome":"failed"}"""
        });
        writer.Record(new TraceEvent
        {
            SessionKey = "thread_page",
            Type = TraceEventType.ProviderError,
            Content = "provider error",
            Timestamp = startedAt.AddSeconds(3)
        });
        writer.Record(new TraceEvent
        {
            SessionKey = "thread_page",
            Type = TraceEventType.Response,
            Content = "response text",
            Timestamp = startedAt.AddSeconds(4)
        });
        writer.Record(new TraceEvent
        {
            SessionKey = "thread_page",
            Type = TraceEventType.ResponseTerminal,
            Content = "response terminal",
            Timestamp = startedAt.AddSeconds(5)
        });

        await using var app = await CreateDashboardApp();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        using var response = await http.GetAsync("/dashboard/api/sessions/thread_page/events/page?limit=1&filter=Maintenance");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.False(root.GetProperty("hasMore").GetBoolean());
        var evt = Assert.Single(root.GetProperty("events").EnumerateArray());
        Assert.Equal("MaintenanceForkRequest", evt.GetProperty("type").GetString());
        Assert.Equal("maintenance", evt.GetProperty("content").GetString());

        using var providerResponse = await http.GetAsync(
            "/dashboard/api/sessions/thread_page/events/page?limit=10&filter=Provider");
        Assert.Equal(HttpStatusCode.OK, providerResponse.StatusCode);
        using var providerDoc = JsonDocument.Parse(await providerResponse.Content.ReadAsStringAsync());
        var providerEvents = providerDoc.RootElement.GetProperty("events").EnumerateArray().ToArray();
        Assert.Equal(
            ["ProviderError", "ProviderResponseDiagnostic"],
            providerEvents.Select(evt => evt.GetProperty("type").GetString()!).Order().ToArray());
        var attemptEvent = Assert.Single(
            providerEvents,
            evt => evt.GetProperty("type").GetString() == "ProviderResponseDiagnostic");
        Assert.Contains("stream_attempt", attemptEvent.GetProperty("metadataJson").GetString(), StringComparison.Ordinal);

        using var responsesResponse = await http.GetAsync(
            "/dashboard/api/sessions/thread_page/events/page?limit=10&filter=Response");
        Assert.Equal(HttpStatusCode.OK, responsesResponse.StatusCode);
        using var responsesDoc = JsonDocument.Parse(await responsesResponse.Content.ReadAsStringAsync());
        var responseEvents = responsesDoc.RootElement.GetProperty("events").EnumerateArray().ToArray();
        Assert.Equal(
            ["Response", "ResponseTerminal"],
            responseEvents.Select(evt => evt.GetProperty("type").GetString()!).Order().ToArray());
    }

    [Fact]
    public async Task ReadOnlyDashboard_ExposesRollbackOperationsFromThreadRollout()
    {
        var timestamp = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
        WriteRollbackRollout("thread_ops", timestamp, 2);

        await using var app = await CreateDashboardApp();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        using var response = await http.GetAsync("/dashboard/api/sessions/thread_ops/operations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var op = Assert.Single(doc.RootElement.EnumerateArray());
        Assert.Equal("rollback", op.GetProperty("type").GetString());
        Assert.Equal("thread_ops", op.GetProperty("threadId").GetString());
        Assert.Equal(2, op.GetProperty("numTurns").GetInt32());
        Assert.Equal("rollout", op.GetProperty("source").GetString());
        Assert.StartsWith("rollback:thread_ops:", op.GetProperty("id").GetString());
    }

    [Fact]
    public async Task ReadOnlyDashboard_RollbackOperationsSkipCorruptAndHiddenRolloutRecords()
    {
        var timestamp = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);
        var activeDir = Path.Combine(_craft, "threads", "active");
        Directory.CreateDirectory(activeDir);
        var path = Path.Combine(activeDir, "thread_corrupt.jsonl");
        await File.WriteAllLinesAsync(path, [
            "{\"kind\":\"thread_rolled_back\",",
            "{\"kind\":\"context_compacted\",\"contextCompacted\":{\"threadId\":\"thread_corrupt\",\"replacementHistory\":[{\"secret\":\"summary\"}]}}",
            BuildRollbackLine("thread_corrupt", timestamp, 1)
        ]);

        await using var app = await CreateDashboardApp();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        using var response = await http.GetAsync("/dashboard/api/sessions/thread_corrupt/operations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var op = Assert.Single(doc.RootElement.EnumerateArray());
        Assert.Equal("rollback", op.GetProperty("type").GetString());
        Assert.Equal(1, op.GetProperty("numTurns").GetInt32());
        Assert.DoesNotContain("replacementHistory", body);
    }

    [Fact]
    public async Task DashboardOperationsEndpoint_UsesPersistenceRootThreadBinding()
    {
        var timestamp = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
        WriteRollbackRollout("thread_root", timestamp, 1);
        var stateRuntime = new WorkspaceStateDatabase(_craft);
        var traceStore = new TraceStore(stateRuntime, 5000, synchronousPersist: true);
        traceStore.BindThreadMainSession("thread_root", timestamp);
        traceStore.BindChildSession("child_session", "thread_root", "thread_root", timestamp);
        var persistence = new SessionPersistenceService(
            new ThreadStore(_craft),
            traceStore,
            stateRuntime: stateRuntime);

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        await using var app = builder.Build();
        app.MapDashBoard(
            traceStore,
            new WorkspacePaths { WorkspacePath = _workspace, CraftPath = _craft },
            persistence: persistence);
        app.Urls.Add($"http://127.0.0.1:{GetFreeTcpPort()}");
        await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        using var response = await http.GetAsync("/dashboard/api/sessions/child_session/operations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var op = Assert.Single(doc.RootElement.EnumerateArray());
        Assert.Equal("thread_root", op.GetProperty("threadId").GetString());
    }

    [Fact]
    public async Task SessionsEndpoint_ProjectsSubAgentRelationshipAndPrefixDiagnostic()
    {
        var stateRuntime = new WorkspaceStateDatabase(_craft);
        var writer = new TraceStore(stateRuntime, 5000, synchronousPersist: true);
        writer.BindThreadMainSession("parent");
        writer.BindChildSession("child", "parent", "parent");
        writer.Record(new TraceEvent
        {
            SessionKey = "parent",
            Type = TraceEventType.Request,
            Content = "parent request"
        });
        writer.Record(new TraceEvent
        {
            SessionKey = "child",
            Type = TraceEventType.SubAgentPrefixDiagnostic,
            MetadataJson = """{"schemaVersion":3,"status":"diverged","matchedInputItemCount":2,"parentInputItemCount":3,"childInputItemCount":4,"divergenceIndex":2,"exactParentInputPrefix":false,"expectedSharedPrefix":true,"cacheIdentityShared":true,"staticPrefixCompatible":false,"changedFields":["tools"]}"""
        });

        await using var app = await CreateDashboardApp();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var response = await http.GetAsync("/dashboard/api/sessions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var child = Assert.Single(
            document.RootElement.EnumerateArray(),
            session => session.GetProperty("sessionKey").GetString() == "child");
        Assert.Equal("parent", child.GetProperty("parentSessionKey").GetString());
        var prefix = child.GetProperty("parentPrefix");
        Assert.Equal("diverged", prefix.GetProperty("status").GetString());
        Assert.Equal(2, prefix.GetProperty("matchedInputItemCount").GetInt32());
        Assert.Equal(2, prefix.GetProperty("divergenceIndex").GetInt32());
        Assert.True(prefix.GetProperty("expectedSharedPrefix").GetBoolean());
        Assert.Equal(
            ["tools"],
            prefix.GetProperty("changedFields").EnumerateArray().Select(field => field.GetString()));
    }

    private async Task<WebApplication> CreateDashboardApp()
    {
        _ = new WorkspaceStateDatabase(_craft);
        var stores = DashBoardReadOnlyStoreLoader.Load(_craft);
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.MapDashBoard(
            stores.TraceStore,
            new WorkspacePaths { WorkspacePath = _workspace, CraftPath = _craft },
            stores.TokenUsageStore,
            runtimeOptions: DashBoardRuntimeOptions.ReadOnlyViewer());
        app.Urls.Add($"http://127.0.0.1:{GetFreeTcpPort()}");
        await app.StartAsync();
        return app;
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private void WriteRollbackRollout(string threadId, DateTimeOffset timestamp, int numTurns)
    {
        var activeDir = Path.Combine(_craft, "threads", "active");
        Directory.CreateDirectory(activeDir);
        File.WriteAllLines(
            Path.Combine(activeDir, $"{threadId}.jsonl"),
            [BuildRollbackLine(threadId, timestamp, numTurns)]);
    }

    private static string BuildRollbackLine(string threadId, DateTimeOffset timestamp, int numTurns)
        => JsonSerializer.Serialize(new
        {
            kind = "thread_rolled_back",
            timestamp,
            threadRolledBack = new
            {
                threadId,
                numTurns,
                lastActiveAt = timestamp
            }
        });
}
