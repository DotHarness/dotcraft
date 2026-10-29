using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using DotCraft.AppServer;
using DotCraft.CLI;
using Xunit;

namespace DotCraft.Tests.AppServer;

public sealed class ThreadReadStateIntegrationTests
{
    [Fact]
    public async Task FailedTurn_ProjectsReceiptAcrossConnections()
    {
        var root = Path.Combine(Path.GetTempPath(), "dotcraft-read-state-" + Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(root, "workspace");
        var profile = Path.Combine(root, "profile");
        Directory.CreateDirectory(Path.Combine(workspace, ".craft"));
        Directory.CreateDirectory(Path.Combine(profile, ".craft"));
        await File.WriteAllTextAsync(Path.Combine(profile, ".craft", "config.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(workspace, ".craft", "config.json"), """
            {
              "ProviderId": "test",
              "ProviderPreferences": { "test": { "Model": "test-model" } },
              "Providers": {
                "test": { "Protocol": "openai-chat-completions", "ApiKey": "test-key", "EndPoint": "http://127.0.0.1:9/v1", "StreamMaxRetries": 0 }
              },
              "DashBoard": { "Enabled": false },
              "Automations": { "Enabled": false },
              "Dreams": { "Enabled": false }
            }
            """);
        var identity = new { channelName = "dotcraft-desktop", userId = "local", workspacePath = workspace };

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        try
        {
            await using (var server = await StartAsync(workspace, profile, deadline.Token))
            {
                await using var observer = await WebSocketClientConnection.ConnectAsync(server.Endpoint, ct: deadline.Token);
                using var observerInit = await observer.Wire.InitializeAsync(ct: deadline.Token);

                using var created = await server.Process.Wire.SendRequestAsync(
                    "thread/start", new { identity }, ct: deadline.Token);
                var threadId = Result(created).GetProperty("thread").GetProperty("id").GetString()!;
                using var turn = await server.Process.Wire.SendRequestAsync("turn/start", new
                {
                    threadId,
                    input = new[] { new { type = "text", text = "hello" } }
                }, ct: deadline.Token);
                Result(turn);

                var creatorSaw = await CollectUntilAsync(server.Process.Wire, IsMethodFor("turn/failed", threadId), deadline.Token);
                var changedIndex = creatorSaw.FindIndex(IsMethodFor("thread/readState/changed", threadId));
                Assert.InRange(changedIndex, 0, creatorSaw.Count - 2);
                var changed = creatorSaw[changedIndex].RootElement.GetProperty("params").GetProperty("readState");
                Assert.Equal("turn", changed.GetProperty("firstUnread").GetProperty("type").GetString());
                var turnId = changed.GetProperty("firstUnread").GetProperty("turnId").GetString();
                var revision = changed.GetProperty("revision").GetString()!;
                var observerSaw = await CollectUntilAsync(observer.Wire, IsMethodFor("thread/readState/changed", threadId), deadline.Token);
                Assert.Equal(revision, ReadStateOf(observerSaw[^1]).GetProperty("revision").GetString());

                using var read = await server.Process.Wire.SendRequestAsync("thread/read", new { threadId }, ct: deadline.Token);
                var readState = Result(read).GetProperty("thread").GetProperty("readState");
                Assert.Equal(turnId, readState.GetProperty("firstUnread").GetProperty("turnId").GetString());
                Assert.Equal(revision, readState.GetProperty("revision").GetString());
                Assert.Equal(revision, ListedRevision(await ListAsync(server.Process.Wire, identity, deadline.Token), threadId));

                using var stale = await observer.Wire.SendRequestAsync("thread/readState/update", new
                {
                    threadId,
                    expectedRevision = "stale",
                    operation = new { type = "read" }
                }, ct: deadline.Token);
                var conflict = Error(stale, -32600, "readStateConflict");
                Assert.Equal(revision, conflict.GetProperty("data").GetProperty("readState").GetProperty("revision").GetString());

                using var marked = await observer.Wire.SendRequestAsync("thread/readState/update", new
                {
                    threadId,
                    expectedRevision = revision,
                    operation = new { type = "read" }
                }, ct: deadline.Token);
                var markedState = Result(marked).GetProperty("readState");
                Assert.Equal(JsonValueKind.Null, markedState.GetProperty("firstUnread").ValueKind);
                var readRevision = markedState.GetProperty("revision").GetString()!;
                var passiveSaw = await CollectUntilAsync(server.Process.Wire, IsMethodFor("thread/readState/changed", threadId), deadline.Token);
                Assert.Equal(readRevision, ReadStateOf(passiveSaw[^1]).GetProperty("revision").GetString());

                using var ephemeral = await server.Process.Wire.SendRequestAsync(
                    "thread/start", new { identity, ephemeral = true }, ct: deadline.Token);
                var ephemeralId = Result(ephemeral).GetProperty("thread").GetProperty("id").GetString()!;
                using var ephemeralRead = await server.Process.Wire.SendRequestAsync(
                    "thread/read", new { threadId = ephemeralId }, ct: deadline.Token);
                Assert.False(Result(ephemeralRead).GetProperty("thread").TryGetProperty("readState", out _));
                using var unavailable = await server.Process.Wire.SendRequestAsync("thread/readState/update", new
                {
                    threadId = ephemeralId,
                    expectedRevision = "0",
                    operation = new { type = "unread" }
                }, ct: deadline.Token);
                Error(unavailable, -32600, "readStateUnavailable");
                using var missing = await server.Process.Wire.SendRequestAsync("thread/readState/update", new
                {
                    threadId = "thread_missing",
                    expectedRevision = "0",
                    operation = new { type = "unread" }
                }, ct: deadline.Token);
                Error(missing, -32010, "ThreadNotFound");
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static async Task<RunningServer> StartAsync(string workspace, string profile, CancellationToken ct)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var process = await AppServerProcess.StartAsync(
            dotcraftBin: typeof(AppServerHost).Assembly.Location,
            workspacePath: workspace,
            listenUrl: $"ws+stdio://127.0.0.1:{port}",
            environmentVariables: new Dictionary<string, string?>
            {
                ["HOME"] = profile,
                ["USERPROFILE"] = profile
            },
            createNoWindow: true,
            ct: ct);
        return new RunningServer(process, new Uri($"ws://127.0.0.1:{port}/ws"));
    }

    private static async Task<JsonDocument> ListAsync(AppServerWireClient wire, object identity, CancellationToken ct) =>
        await wire.SendRequestAsync("thread/list", new { identity, scope = "workspace" }, ct: ct);

    private static string? ListedRevision(JsonDocument listed, string threadId)
    {
        using (listed)
            return Result(listed).GetProperty("readStates").GetProperty(threadId).GetProperty("revision").GetString();
    }

    private static async Task<List<JsonDocument>> CollectUntilAsync(
        AppServerWireClient wire,
        Predicate<JsonDocument> stop,
        CancellationToken ct)
    {
        var collected = new List<JsonDocument>();
        while (await wire.WaitForNotificationAsync(timeout: TimeSpan.FromSeconds(60), ct: ct) is { } notification)
        {
            collected.Add(notification);
            if (stop(notification))
                return collected;
        }

        throw new TimeoutException("The expected notification did not arrive.");
    }

    private static Predicate<JsonDocument> IsMethodFor(string method, string threadId) =>
        notification => notification.RootElement.GetProperty("method").GetString() == method
                        && ThreadIdOf(notification) == threadId;

    private static string? ThreadIdOf(JsonDocument notification)
    {
        var parameters = notification.RootElement.GetProperty("params");
        if (parameters.TryGetProperty("threadId", out var threadId))
            return threadId.GetString();
        return parameters.TryGetProperty("turn", out var turn) && turn.TryGetProperty("threadId", out var turnThreadId)
            ? turnThreadId.GetString()
            : null;
    }

    private static JsonElement ReadStateOf(JsonDocument notification) =>
        notification.RootElement.GetProperty("params").GetProperty("readState");

    private static JsonElement Result(JsonDocument response)
    {
        Assert.False(response.RootElement.TryGetProperty("error", out var error), error.ToString());
        return response.RootElement.GetProperty("result");
    }

    private static JsonElement Error(JsonDocument response, int code, string dataCode)
    {
        var error = response.RootElement.GetProperty("error");
        Assert.Equal(code, error.GetProperty("code").GetInt32());
        Assert.Equal(dataCode, error.GetProperty("data").GetProperty("code").GetString());
        return error;
    }

    private sealed record RunningServer(AppServerProcess Process, Uri Endpoint) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Process.DisposeAsync();
    }
}
