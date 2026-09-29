using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using DotCraft.AppServer;
using DotCraft.CLI;
using Xunit;

namespace DotCraft.Tests.AppServer;

public sealed class ThreadDeletionBroadcastIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delete_NotifiesInitiatorAndUnsubscribedObserver_ButHonorsOptOut(bool deleteOverWebSocket)
    {
        var root = Path.Combine(Path.GetTempPath(), "dotcraft-delete-broadcast-" + Guid.NewGuid().ToString("N"));
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
                "test": { "Protocol": "openai-chat-completions", "ApiKey": "test-key", "EndPoint": "http://127.0.0.1:9/v1" }
              },
              "DashBoard": { "Enabled": false },
              "Automations": { "Enabled": false },
              "Dreams": { "Enabled": false }
            }
            """);

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
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
            await using var observer = await WebSocketClientConnection.ConnectAsync(endpoint, ct: deadline.Token);
            await using var optedOut = await WebSocketClientConnection.ConnectAsync(endpoint, ct: deadline.Token);
            using var observerInit = await observer.Wire.InitializeAsync(ct: deadline.Token);
            using var optedOutInit = await optedOut.Wire.InitializeAsync(
                optOutMethods: ["thread/deleted"], ct: deadline.Token);
            var initiator = deleteOverWebSocket ? observer.Wire : server.Wire;
            var passive = deleteOverWebSocket ? server.Wire : observer.Wire;

            using var created = await server.Wire.SendRequestAsync("thread/start", new
            {
                identity = new { channelName = "dotcraft-desktop", userId = "local", workspacePath = workspace }
            }, ct: deadline.Token);
            var threadId = Result(created).GetProperty("thread").GetProperty("id").GetString()!;
            using var deleted = await initiator.SendRequestAsync("thread/delete", new { threadId }, ct: deadline.Token);
            Result(deleted);

            using var observed = await passive.WaitForNotificationAsync(
                "thread/deleted", TimeSpan.FromSeconds(5), deadline.Token);
            AssertDeletion(observed, threadId);
            using var initiated = await initiator.WaitForNotificationAsync(
                "thread/deleted", TimeSpan.FromSeconds(5), deadline.Token);
            AssertDeletion(initiated, threadId);
            using var suppressed = await optedOut.Wire.WaitForNotificationAsync(
                "thread/deleted", TimeSpan.FromMilliseconds(300), deadline.Token);
            Assert.Null(suppressed);

            using var listed = await server.Wire.SendRequestAsync("thread/list", new
            {
                identity = new { channelName = "dotcraft-desktop", userId = "local", workspacePath = workspace },
                scope = "workspace"
            }, ct: deadline.Token);
            Assert.DoesNotContain(Result(listed).GetProperty("data").EnumerateArray(),
                thread => thread.GetProperty("id").GetString() == threadId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static JsonElement Result(JsonDocument response)
    {
        Assert.False(response.RootElement.TryGetProperty("error", out var error), error.ToString());
        return response.RootElement.GetProperty("result");
    }

    private static void AssertDeletion(JsonDocument? notification, string threadId)
    {
        Assert.NotNull(notification);
        Assert.Equal(threadId, notification.RootElement.GetProperty("params").GetProperty("threadId").GetString());
    }
}
