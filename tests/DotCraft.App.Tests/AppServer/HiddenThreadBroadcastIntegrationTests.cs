using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using DotCraft.AppServer;
using DotCraft.CLI;
using Xunit;

namespace DotCraft.Tests.AppServer;

public sealed class HiddenThreadBroadcastIntegrationTests
{
    [Fact]
    public async Task HiddenThreads_NotifyOnlySubscribedConnections()
    {
        var root = Path.Combine(Path.GetTempPath(), "dotcraft-hidden-broadcast-" + Guid.NewGuid().ToString("N"));
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
              "Dreams": { "Enabled": false },
              "WelcomeSuggestions": { "Enabled": false }
            }
            """);

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            await using var server = await AppServerProcess.StartAsync(
                dotcraftBin: typeof(AppServerHost).Assembly.Location,
                workspacePath: workspace,
                listenUrl: $"ws+stdio://127.0.0.1:{port}",
                environmentVariables: new Dictionary<string, string?>
                {
                    ["HOME"] = profile,
                    ["USERPROFILE"] = profile
                },
                createNoWindow: true,
                ct: deadline.Token);
            var endpoint = new Uri($"ws://127.0.0.1:{port}/ws");
            await using var subscriber = await WebSocketClientConnection.ConnectAsync(endpoint, ct: deadline.Token);
            await using var observer = await WebSocketClientConnection.ConnectAsync(endpoint, ct: deadline.Token);
            await using var optedOut = await WebSocketClientConnection.ConnectAsync(endpoint, ct: deadline.Token);
            using var optedOutInit = await optedOut.Wire.InitializeAsync(
                optOutMethods: ["thread/updated"], ct: deadline.Token);
            using var subscriberInit = await subscriber.Wire.InitializeAsync(ct: deadline.Token);
            using var observerInit = await observer.Wire.InitializeAsync(ct: deadline.Token);

            var hiddenIds = new List<string>();
            foreach (var ephemeral in new[] { true, false })
            {
                using var created = await server.Wire.SendRequestAsync("thread/start", new
                {
                    identity = new { channelName = ephemeral ? "dotcraft-desktop" : "commit-suggest", userId = "local", workspacePath = workspace },
                    ephemeral
                }, ct: deadline.Token);
                var threadId = Result(created).GetProperty("thread").GetProperty("id").GetString()!;
                hiddenIds.Add(threadId);
                using var subscribed = await subscriber.Wire.SendRequestAsync("thread/subscribe", new { threadId }, ct: deadline.Token);
                Result(subscribed);

                using var mode = await server.Wire.SendRequestAsync(
                    "thread/mode/set", new { threadId, mode = "plan" }, ct: deadline.Token);
                Result(mode);
                using var subscriberUpdated = await subscriber.Wire.WaitForNotificationAsync(
                    "thread/updated", TimeSpan.FromSeconds(10), deadline.Token);
                AssertMode(subscriberUpdated, threadId, "plan");

                using var turn = await server.Wire.SendRequestAsync("turn/start", new
                {
                    threadId,
                    input = new[] { new { type = "text", text = "hello" } }
                }, ct: deadline.Token);
                Result(turn);
                var subscriberSaw = await CollectUntilAsync(subscriber.Wire, IsTurnEnd, deadline.Token);
                using var deleted = await server.Wire.SendRequestAsync("thread/delete", new { threadId }, ct: deadline.Token);
                Result(deleted);
                using var subscriberDeleted = await subscriber.Wire.WaitForNotificationAsync(
                    "thread/deleted", TimeSpan.FromSeconds(10), deadline.Token);

                Assert.Contains(subscriberSaw, notification =>
                    notification.RootElement.GetProperty("method").GetString() == "thread/status/changed"
                    && ThreadIdOf(notification) == threadId);
                Assert.NotNull(subscriberDeleted);
                Assert.Equal(threadId, ThreadIdOf(subscriberDeleted));
            }

            using var visible = await server.Wire.SendRequestAsync("thread/start", new
            {
                identity = new { channelName = "dotcraft-desktop", userId = "local", workspacePath = workspace }
            }, ct: deadline.Token);
            var visibleId = Result(visible).GetProperty("thread").GetProperty("id").GetString()!;
            var observerSaw = await CollectUntilAsync(observer.Wire, IsStartOf(visibleId), deadline.Token);
            var creatorSaw = await CollectUntilAsync(server.Wire, IsStartOf(visibleId), deadline.Token);
            using var modeChanged = await observer.Wire.SendRequestAsync(
                "thread/mode/set", new { threadId = visibleId, mode = "plan" }, ct: deadline.Token);
            Result(modeChanged);
            using var passiveUpdated = await server.Wire.WaitForNotificationAsync(
                "thread/updated", TimeSpan.FromSeconds(10), deadline.Token);
            using var initiatorUpdated = await observer.Wire.WaitForNotificationAsync(
                "thread/updated", TimeSpan.FromSeconds(10), deadline.Token);
            AssertMode(passiveUpdated, visibleId, "plan");
            AssertMode(initiatorUpdated, visibleId, "plan");
            using var suppressed = await optedOut.Wire.WaitForNotificationAsync(
                "thread/updated", TimeSpan.FromMilliseconds(300), deadline.Token);
            Assert.Null(suppressed);

            Assert.DoesNotContain(observerSaw, notification => hiddenIds.Contains(ThreadIdOf(notification)!));
            Assert.Contains(observerSaw, notification => ThreadIdOf(notification) == visibleId);
            Assert.DoesNotContain(creatorSaw, notification =>
                hiddenIds.Contains(ThreadIdOf(notification)!)
                && notification.RootElement.GetProperty("method").GetString() is "thread/status/changed" or "thread/updated" or "thread/deleted");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static async Task<List<JsonDocument>> CollectUntilAsync(
        AppServerWireClient wire,
        Func<JsonDocument, bool> stop,
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

    private static Func<JsonDocument, bool> IsStartOf(string threadId) =>
        notification => notification.RootElement.GetProperty("method").GetString() == "thread/started"
                        && ThreadIdOf(notification) == threadId;

    private static bool IsTurnEnd(JsonDocument notification) =>
        notification.RootElement.GetProperty("method").GetString() is "turn/completed" or "turn/failed" or "turn/cancelled";

    private static string? ThreadIdOf(JsonDocument? notification)
    {
        if (notification is null || !notification.RootElement.TryGetProperty("params", out var parameters)
            || parameters.ValueKind != JsonValueKind.Object)
            return null;
        if (parameters.TryGetProperty("threadId", out var threadId))
            return threadId.GetString();
        return parameters.TryGetProperty("thread", out var thread) && thread.TryGetProperty("id", out var id)
            ? id.GetString()
            : null;
    }

    private static void AssertMode(JsonDocument? notification, string threadId, string mode)
    {
        Assert.NotNull(notification);
        var thread = notification.RootElement.GetProperty("params").GetProperty("thread");
        Assert.Equal(threadId, thread.GetProperty("id").GetString());
        Assert.Equal(mode, thread.GetProperty("configuration").GetProperty("mode").GetString());
    }

    private static JsonElement Result(JsonDocument response)
    {
        Assert.False(response.RootElement.TryGetProperty("error", out var error), error.ToString());
        return response.RootElement.GetProperty("result");
    }
}
