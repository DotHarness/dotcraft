using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace DotCraft.Tests.CLI;

public abstract class ExecProcessTestBase : IDisposable
{
    private static readonly string[] Subcommands = ["resume", "interrupt"];

    protected ExecProcessTestBase()
    {
        Root = Path.Combine(Path.GetTempPath(), "dotcraft-exec-process-tests", Guid.NewGuid().ToString("N"));
        Workspace = Path.Combine(Root, "workspace");
        Directory.CreateDirectory(Path.Combine(Workspace, ".craft"));
        File.WriteAllText(Path.Combine(Workspace, ".craft", "config.json"), "{}");
    }

    protected string Root { get; }

    protected string Workspace { get; }

    public void Dispose() => Directory.Delete(Root, recursive: true);

    protected string CreateDirectory(string name)
    {
        var path = Path.Combine(Root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    protected static string TurnText(ExecFakeAppServer server) =>
        Assert.Single(server.ParamsOf("turn/start"))["input"]![0]!["text"]!.GetValue<string>();

    protected async Task<ExecProcessResult> RunExecAsync(
        ExecFakeAppServer server,
        IReadOnlyList<string> args,
        string stdin = "",
        string? workingDirectory = null)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory ?? Workspace,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "dotcraft.dll"));
        startInfo.ArgumentList.Add("exec");
        var subcommand = args.Count > 0 && Subcommands.Contains(args[0]);
        if (subcommand)
            startInfo.ArgumentList.Add(args[0]);
        startInfo.ArgumentList.Add("--remote");
        startInfo.ArgumentList.Add(server.Url);
        foreach (var arg in subcommand ? args.Skip(1) : args)
            startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start DotCraft.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync(stdin);
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await process.WaitForExitAsync(timeout.Token);
        return new ExecProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
    }
}

public static class ExecWire
{
    public static JsonObject Turn(string threadId, string status, params JsonObject[] items) => new()
    {
        ["id"] = "turn_1",
        ["threadId"] = threadId,
        ["status"] = status,
        ["items"] = new JsonArray(items.Select(JsonNode? (item) => item.DeepClone()).ToArray())
    };

    public static JsonObject Completed(string threadId, string text) =>
        Notification("turn/completed", new JsonObject { ["turn"] = Turn(threadId, "completed", AgentMessage(text)) });

    public static JsonObject AgentMessage(string text, string? deliveryMode = null)
    {
        var payload = new JsonObject { ["text"] = text };
        if (deliveryMode != null)
            payload["deliveryMode"] = deliveryMode;
        return Item("agentMessage", payload);
    }

    public static JsonObject Item(string type, JsonObject payload, string? id = null) => new()
    {
        ["id"] = id ?? $"item_{type}",
        ["turnId"] = "turn_1",
        ["type"] = type,
        ["status"] = "completed",
        ["payload"] = payload
    };

    public static JsonObject ItemEvent(string method, string threadId, JsonObject item) =>
        Notification(method, new JsonObject { ["threadId"] = threadId, ["turnId"] = "turn_1", ["item"] = item.DeepClone() });

    public static JsonObject Notification(string method, JsonObject @params) =>
        new() { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = @params };
}

public sealed record ExecProcessResult(int ExitCode, string StandardOutput, string StandardError);

public sealed record ExecRecordedRequest(string Method, JsonNode? Params);

public sealed record ExecFakeAppServerOptions
{
    public Func<string, IReadOnlyList<JsonObject>> TurnNotifications { get; init; } =
        threadId => [ExecWire.Completed(threadId, "done")];

    public string? TurnStartError { get; init; }
    public IReadOnlyList<string> ListedThreadIds { get; init; } = [];
    public JsonObject? ThreadConfiguration { get; init; }
    public string? ActiveTurnId { get; init; }
    public IReadOnlyList<string>? KnownThreadIds { get; init; }
}

public sealed class ExecFakeAppServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ExecFakeAppServerOptions _options;
    private readonly ConcurrentQueue<ExecRecordedRequest> _requests = new();

    private ExecFakeAppServer(WebApplication app, ExecFakeAppServerOptions options)
    {
        _app = app;
        _options = options;
    }

    public string Url { get; private set; } = string.Empty;

    public IReadOnlyList<ExecRecordedRequest> Requests => _requests.ToArray();

    public IReadOnlyList<JsonNode> ParamsOf(string method) =>
        _requests.Where(r => r.Method == method).Select(r => r.Params ?? new JsonObject()).ToArray();

    public static async Task<ExecFakeAppServer> StartAsync(ExecFakeAppServerOptions? options = null)
    {
        var app = WebApplication.CreateBuilder().Build();
        var server = new ExecFakeAppServer(app, options ?? new ExecFakeAppServerOptions());
        app.UseWebSockets();
        app.Map("/ws", async (HttpContext context) =>
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await server.ServeAsync(socket);
        });
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        var address = new Uri(app.Urls.First());
        server.Url = $"ws://127.0.0.1:{address.Port}/ws";
        return server;
    }

    private async Task ServeAsync(WebSocket socket)
    {
        while (socket.State == WebSocketState.Open)
        {
            var message = await ReceiveAsync(socket);
            if (message == null)
                return;

            var method = message["method"]?.GetValue<string>();
            var id = message["id"]?.DeepClone();
            if (id == null || method == null)
                continue;

            var @params = message["params"]?.DeepClone();
            _requests.Enqueue(new ExecRecordedRequest(method, @params));
            var threadId = @params?["threadId"]?.GetValue<string>() ?? "thread_1";

            if (Error(method, threadId) is { } error)
            {
                await SendAsync(socket, new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = error });
                continue;
            }

            await SendAsync(socket, new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = Result(method, threadId) });

            if (method == "turn/start")
            {
                foreach (var notification in _options.TurnNotifications(threadId))
                    await SendAsync(socket, notification);
            }
        }
    }

    private JsonObject? Error(string method, string threadId)
    {
        if (method == "turn/start" && _options.TurnStartError != null)
            return new JsonObject { ["code"] = -32010, ["message"] = _options.TurnStartError };
        if (method == "thread/read" && _options.KnownThreadIds is { } known && !known.Contains(threadId))
            return new JsonObject { ["code"] = -32001, ["message"] = $"Thread not found: {threadId}" };
        return null;
    }

    private JsonObject Result(string method, string threadId) => method switch
    {
        "initialize" => new JsonObject { ["serverInfo"] = new JsonObject { ["name"] = "fake", ["version"] = "1" } },
        "thread/start" => new JsonObject { ["thread"] = new JsonObject { ["id"] = "thread_1" } },
        "thread/list" => new JsonObject
        {
            ["data"] = new JsonArray(_options.ListedThreadIds.Select(JsonNode? (listed) => new JsonObject { ["id"] = listed }).ToArray())
        },
        "thread/read" => new JsonObject
        {
            ["thread"] = new JsonObject
            {
                ["id"] = threadId,
                ["configuration"] = _options.ThreadConfiguration?.DeepClone(),
                ["runtime"] = new JsonObject
                {
                    ["running"] = _options.ActiveTurnId != null,
                    ["activeTurnId"] = _options.ActiveTurnId
                }
            }
        },
        "turn/start" => new JsonObject { ["turn"] = new JsonObject { ["id"] = "turn_1", ["threadId"] = threadId } },
        _ => new JsonObject()
    };

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
