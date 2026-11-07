using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using DotCraft.AppServer;
using Xunit;

namespace DotCraft.Tests.CLI;

public sealed class WireClientTurnIsolationTests
{
    [Fact]
    public async Task TargetTurnReader_IgnoresOtherTurns_AndLeavesThreadFeedOpen()
    {
        var incoming = new Pipe();
        var outgoing = new Pipe();
        await using var client = new AppServerWireClient(incoming.Reader.AsStream(), outgoing.Writer.AsStream());
        client.RegisterThreadChannel("a");
        client.RegisterThreadChannel("b");
        client.Start();
        await using var writer = new StreamWriter(incoming.Writer.AsStream(), new UTF8Encoding(false)) { AutoFlush = true };

        await WriteAsync("turn/completed", new { turn = new { id = "target", threadId = "b" } });
        await WriteAsync("turn/failed", new { turn = new { id = "previous", threadId = "a" } });
        await WriteAsync("item/agentMessage/delta", new { threadId = "a", turnId = "previous", delta = "wrong" });
        await WriteAsync("item/agentMessage/delta", new { threadId = "a", delta = "missing identity" });
        await WriteAsync("item/agentMessage/delta", new { threadId = "a", turnId = "target", delta = "answer" });
        await WriteAsync("turn/completed", new { turn = new { id = "target", threadId = "a" } });
        await WriteAsync("turn/started", new { turn = new { id = "next", threadId = "a" } });

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var methods = new List<string>();
        await foreach (var document in client.ReadThreadTurnNotificationsAsync("a", "target", ct: deadline.Token))
        {
            using (document)
                methods.Add(document.RootElement.GetProperty("method").GetString()!);
        }
        Assert.Equal(["item/agentMessage/delta", "turn/completed"], methods);

        using var otherThread = await client.WaitForThreadNotificationAsync("b", timeout: TimeSpan.FromSeconds(5));
        Assert.Equal("b", otherThread!.RootElement.GetProperty("params").GetProperty("turn").GetProperty("threadId").GetString());
        await foreach (var document in client.ReadThreadNotificationsAsync("a", deadline.Token))
        {
            using (document)
                Assert.Equal("next", document.RootElement.GetProperty("params").GetProperty("turn").GetProperty("id").GetString());
            break;
        }
        client.UnregisterThreadChannel("a");
        client.UnregisterThreadChannel("b");

        Task WriteAsync(string method, object parameters) =>
            writer.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", method, @params = parameters }));
    }
}
