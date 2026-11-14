using System.Text.Json;
using DotCraft.CodexGateway;
using DotCraft.CodexGateway.Protocol;
using Xunit;

namespace DotCraft.CodexGateway.Tests;

public sealed class CodexHandshakeSessionTests
{
    [Fact]
    public async Task Sessions_InitializeIndependentlyWithOverlappingIds_AndDoNotRespondToNotifications()
    {
        var options = new RemoteControlHostOptions
        {
            StateDirectory = Path.Combine(Path.GetTempPath(), "dotcraft-gateway-rpc"),
            Name = "RPC host"
        };
        var firstMessages = new List<JsonElement>();
        var secondMessages = new List<JsonElement>();
        var methods = new List<string>();
        await using var first = new CodexHandshakeSession(options, Status, Capture(firstMessages), methods.Add);
        await using var second = new CodexHandshakeSession(options, Status, Capture(secondMessages));

        await ReceiveAsync(first, """{"id":1,"method":"model/list"}""");
        Assert.Equal(-32600, firstMessages[^1].GetProperty("error").GetProperty("code").GetInt32());
        await ReceiveAsync(first, Initialize);
        var header = firstMessages[^1];
        Assert.Equal(1, header.GetProperty("id").GetInt32());
        Assert.False(header.TryGetProperty("jsonrpc", out _));
        var result = header.GetProperty("result");
        Assert.Equal("dotcraft_codex_gateway/" + options.AppServerVersion, result.GetProperty("userAgent").GetString());
        Assert.Equal(Path.GetFullPath(options.StateDirectory), result.GetProperty("codexHome").GetString());
        Assert.Equal(OperatingSystem.IsWindows() ? "windows" : "unix", result.GetProperty("platformFamily").GetString());
        Assert.Equal(OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux",
            result.GetProperty("platformOs").GetString());

        await ReceiveAsync(second, """{"id":1,"method":"model/list"}""");
        Assert.Equal(-32600, secondMessages[^1].GetProperty("error").GetProperty("code").GetInt32());
        await ReceiveAsync(second, Initialize);
        Assert.Equal(1, secondMessages[^1].GetProperty("id").GetInt32());
        Assert.True(secondMessages[^1].TryGetProperty("result", out _));

        var count = firstMessages.Count;
        await ReceiveAsync(first, """{"method":"initialized"}""");
        await ReceiveAsync(first, """{"method":"unknown/notification"}""");
        Assert.Equal(count, firstMessages.Count);
        Assert.Contains("initialized", methods);
        await first.SendAsync(JsonSerializer.Deserialize<JsonElement>("""{"method":"thread/started","params":{}}"""), CancellationToken.None);
        Assert.Equal(count, firstMessages.Count);
        await first.SendAsync(JsonSerializer.Deserialize<JsonElement>("""{"id":9,"method":"thread/started","params":{}}"""), CancellationToken.None);
        Assert.Equal(9, firstMessages[^1].GetProperty("id").GetInt32());

        await ReceiveAsync(first, """{"id":"status","method":"remoteControl/status/read"}""");
        Assert.Equal("status", firstMessages[^1].GetProperty("id").GetString());
        var status = firstMessages[^1].GetProperty("result");
        Assert.Equal("environment", status.GetProperty("environmentId").GetString());
        Assert.Equal("connected", status.GetProperty("status").GetString());
        Assert.Equal(2, secondMessages.Count);
    }

    [Fact]
    public async Task Requests_DispatchAfterInitialization_AndReportHandlerErrors()
    {
        var messages = new List<JsonElement>();
        var options = new RemoteControlHostOptions { StateDirectory = Path.GetTempPath() };
        var requests = new List<string>();
        var diagnostics = new List<string>();
        Task<object?> Handle(string method, JsonElement parameters, CancellationToken _)
        {
            requests.Add(method);
            return method switch
            {
                "thread/list" => Task.FromResult<object?>(new { data = new[] { new { id = "real-thread", preview = "private-response-text" } }, nextCursor = (string?)null }),
                "turn/start" => throw new ArgumentException("Model is unavailable."),
                "thread/read" => throw new InvalidOperationException("private-source-details"),
                _ => Task.FromResult<object?>(null)
            };
        }
        await using var session = new CodexHandshakeSession(options, Status, Capture(messages), observedMethod: diagnostics.Add, requestHandler: Handle);
        await ReceiveAsync(session, """{"id":0,"method":"thread/list"}""");
        Assert.Empty(requests);
        await ReceiveAsync(session, Initialize);
        await ReceiveAsync(session, """{"method":"initialized"}""");
        await ReceiveAsync(session, """{"id":2,"method":"thread/list"}""");
        Assert.Equal("real-thread", messages[^1].GetProperty("result").GetProperty("data")[0].GetProperty("id").GetString());
        await ReceiveAsync(session, """{"id":3,"method":"turn/start","params":{}}""");
        Assert.Equal(-32602, messages[^1].GetProperty("error").GetProperty("code").GetInt32());
        await ReceiveAsync(session, """{"id":4,"method":"unknown/method"}""");
        Assert.Equal(-32601, messages[^1].GetProperty("error").GetProperty("code").GetInt32());
        await ReceiveAsync(session, """{"id":5,"method":"thread/read","params":{}}""");
        Assert.Equal("sourceRequestFailed", messages[^1].GetProperty("error").GetProperty("data").GetProperty("code").GetString());
        Assert.DoesNotContain("private-source-details", messages[^1].GetRawText());
        await ReceiveAsync(session, """{"id":6,"method":"thread/list"}""");
        Assert.Equal("real-thread", messages[^1].GetProperty("result").GetProperty("data")[0].GetProperty("id").GetString());
        Assert.Contains(diagnostics, entry => entry.Contains("\"stage\":\"response\"", StringComparison.Ordinal));
        Assert.DoesNotContain("private-response-text", string.Join('\n', diagnostics));
        Assert.DoesNotContain("private-source-details", string.Join('\n', diagnostics));
    }

    private const string Initialize = """
        {"id":1,"method":"initialize","params":{"clientInfo":{"name":"remote-test","version":"1.0"},"capabilities":{"experimentalApi":true,"optOutNotificationMethods":["thread/started"]}}}
        """;

    private static RemoteControlHostStatus Status() => new("installation", "RPC host", "environment", "connected");

    private static Func<JsonElement, CancellationToken, Task> Capture(List<JsonElement> messages) => (message, _) =>
    {
        messages.Add(message.Clone());
        return Task.CompletedTask;
    };

    private static Task ReceiveAsync(IRemoteControlRpcSession session, string json) =>
        session.ReceiveAsync(JsonSerializer.Deserialize<JsonElement>(json), CancellationToken.None);
}
