using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace DotCraft.Tests.CLI;

public sealed class ExecProcessTests
{
    [Fact]
    public async Task Exec_CompletedTurn_WritesLastAgentMessageToStdoutAndExitsZero()
    {
        await using var server = await FakeAppServer.StartAsync(
        [
            Notification("item/agentMessage/delta", new JsonObject { ["threadId"] = "thread_1", ["delta"] = "Checking." }),
            Notification("turn/completed", new JsonObject
            {
                ["turn"] = new JsonObject
                {
                    ["id"] = "turn_1",
                    ["threadId"] = "thread_1",
                    ["status"] = "completed",
                    ["items"] = new JsonArray(
                        AgentMessage("Checking."),
                        AgentMessage("final answer"),
                        AgentMessage("sent mid-turn", deliveryMode: "async"))
                }
            })
        ]);

        var result = await RunExecAsync(server.Url);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("final answer", result.StandardOutput.Trim());
    }

    [Fact]
    public async Task Exec_FailedTurn_ReportsErrorOnStderrAndExitsNonZero()
    {
        await using var server = await FakeAppServer.StartAsync(
        [
            Notification("turn/failed", new JsonObject
            {
                ["turn"] = new JsonObject { ["id"] = "turn_1", ["threadId"] = "thread_1", ["status"] = "failed" },
                ["error"] = "provider exploded"
            })
        ]);

        var result = await RunExecAsync(server.Url);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardOutput.Trim());
        Assert.Contains("provider exploded", result.StandardError, StringComparison.Ordinal);
    }

    private static JsonObject AgentMessage(string text, string? deliveryMode = null)
    {
        var payload = new JsonObject { ["text"] = text };
        if (deliveryMode != null)
            payload["deliveryMode"] = deliveryMode;
        return new JsonObject { ["type"] = "agentMessage", ["status"] = "completed", ["payload"] = payload };
    }

    private static JsonObject Notification(string method, JsonObject @params) =>
        new() { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = @params };

    private static async Task<ProcessResult> RunExecAsync(string url)
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "dotcraft-exec-process-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(workspacePath, ".craft"));
        await File.WriteAllTextAsync(Path.Combine(workspacePath, ".craft", "config.json"), "{}");
        try
        {
            var startInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = workspacePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "dotcraft.dll"));
            startInfo.ArgumentList.Add("exec");
            startInfo.ArgumentList.Add("--remote");
            startInfo.ArgumentList.Add(url);
            startInfo.ArgumentList.Add("hello");

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start DotCraft.");
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await process.WaitForExitAsync(timeout.Token);
            return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
        }
        finally
        {
            Directory.Delete(workspacePath, recursive: true);
        }
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed class FakeAppServer : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private FakeAppServer(WebApplication app, string url)
        {
            _app = app;
            Url = url;
        }

        public string Url { get; }

        public static async Task<FakeAppServer> StartAsync(IReadOnlyList<JsonObject> turnNotifications)
        {
            var app = WebApplication.CreateBuilder().Build();
            app.UseWebSockets();
            app.Map("/ws", async (HttpContext context) =>
            {
                using var socket = await context.WebSockets.AcceptWebSocketAsync();
                await ServeAsync(socket, turnNotifications);
            });
            app.Urls.Add("http://127.0.0.1:0");
            await app.StartAsync();
            var address = new Uri(app.Urls.First());
            return new FakeAppServer(app, $"ws://127.0.0.1:{address.Port}/ws");
        }

        private static async Task ServeAsync(WebSocket socket, IReadOnlyList<JsonObject> turnNotifications)
        {
            while (socket.State == WebSocketState.Open)
            {
                var message = await ReceiveAsync(socket);
                if (message == null)
                    return;

                var method = message["method"]?.GetValue<string>();
                var id = message["id"]?.DeepClone();
                if (id == null)
                    continue;

                JsonObject result = method switch
                {
                    "initialize" => new JsonObject { ["serverInfo"] = new JsonObject { ["name"] = "fake", ["version"] = "1" } },
                    "thread/start" => new JsonObject { ["thread"] = new JsonObject { ["id"] = "thread_1" } },
                    "turn/start" => new JsonObject { ["turn"] = new JsonObject { ["id"] = "turn_1", ["threadId"] = "thread_1" } },
                    _ => new JsonObject()
                };
                await SendAsync(socket, new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result });

                if (method == "turn/start")
                {
                    foreach (var notification in turnNotifications)
                        await SendAsync(socket, (JsonObject)notification.DeepClone());
                }
            }
        }

        private static async Task<JsonNode?> ReceiveAsync(WebSocket socket)
        {
            var buffer = new byte[64 * 1024];
            using var received = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                try
                {
                    result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                }
                catch (WebSocketException)
                {
                    return null;
                }
                if (result.MessageType == WebSocketMessageType.Close)
                    return null;
                received.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            return JsonNode.Parse(received.ToArray());
        }

        private static Task SendAsync(WebSocket socket, JsonObject message) =>
            socket.SendAsync(
                Encoding.UTF8.GetBytes(message.ToJsonString()),
                WebSocketMessageType.Text,
                endOfMessage: true,
                CancellationToken.None);

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
