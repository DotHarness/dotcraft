using System.Diagnostics;
using System.Text.Json.Nodes;
using DotCraft.CLI;

namespace DotCraft.CodeMode.Tests;

public sealed class CodeModeWorkerProcessTests
{
    [Fact]
    public async Task Cell_RunsHostCallsConcurrentlyAndReportsOutputAndStoreWrites()
    {
        await using var worker = await Worker.StartAsync();
        await worker.WriteAsync("cell_1", "cell.start", StartPayload("""
            const [first, second] = await Promise.all([tools.Alpha({ n: 1 }), tools.mcp__docs__search({ q: 'x' })]);
            text(first.total + ':' + second);
            text({ previous: load('count') ?? 0 });
            store('count', (load('count') ?? 0) + 1);
            store('gone', undefined);
            """, store: new JsonObject { ["count"] = 4, ["gone"] = true }));

        var first = await worker.ReadAsync();
        var second = await worker.ReadAsync();
        Assert.Equal("call.request", first["type"]!.GetValue<string>());
        Assert.Equal("call.request", second["type"]!.GetValue<string>());
        Assert.Equal("Alpha", first["payload"]!["tool"]!.GetValue<string>());
        Assert.Equal(1, first["payload"]!["arguments"]!["n"]!.GetValue<int>());
        Assert.Equal("mcp__docs__search", second["payload"]!["tool"]!.GetValue<string>());

        await worker.WriteAsync("cell_1", "call.result", new JsonObject
        {
            ["requestId"] = second["payload"]!["requestId"]!.GetValue<string>(),
            ["value"] = "found"
        });
        await worker.WriteAsync("cell_1", "call.result", new JsonObject
        {
            ["requestId"] = first["payload"]!["requestId"]!.GetValue<string>(),
            ["value"] = new JsonObject { ["total"] = 3 }
        });

        var output = await worker.ReadAsync();
        Assert.Equal("cell.output", output["type"]!.GetValue<string>());
        Assert.Equal("3:found", output["payload"]!["text"]!.GetValue<string>());
        Assert.Equal("{\"previous\":4}", (await worker.ReadAsync())["payload"]!["text"]!.GetValue<string>());
        var done = await worker.ReadAsync();
        Assert.Equal("cell.done", done["type"]!.GetValue<string>());
        Assert.Equal("completed", done["payload"]!["outcome"]!.GetValue<string>());
        Assert.Equal(5, done["payload"]!["store"]!["set"]!["count"]!.GetValue<int>());
        Assert.Equal("gone", done["payload"]!["store"]!["deleted"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task Cell_RejectsFailedCallsAndUnknownTools()
    {
        await using var worker = await Worker.StartAsync();
        await worker.WriteAsync("cell_1", "cell.start", StartPayload("""
            try { await tools.Alpha({}); } catch (e) { text(e.message); }
            try { tools.Alpah({}); } catch (e) { text(e.constructor.name + ' ' + e.message); }
            try { await tools.Alpha({ f: () => 1 }); } catch (e) { text('not json'); }
            """));

        var request = await worker.ReadAsync();
        await worker.WriteAsync("cell_1", "call.result", new JsonObject
        {
            ["requestId"] = request["payload"]!["requestId"]!.GetValue<string>(),
            ["error"] = "tool_approval_rejected: denied"
        });

        Assert.Equal("tool_approval_rejected: denied", (await worker.ReadAsync())["payload"]!["text"]!.GetValue<string>());
        var unknown = (await worker.ReadAsync())["payload"]!["text"]!.GetValue<string>();
        Assert.StartsWith("TypeError", unknown);
        Assert.Contains("tools.Alpha", unknown);
        Assert.Contains("ALL_TOOLS", unknown);
        Assert.Equal("not json", (await worker.ReadAsync())["payload"]!["text"]!.GetValue<string>());
        Assert.Equal("completed", (await worker.ReadAsync())["payload"]!["outcome"]!.GetValue<string>());
    }

    [Fact]
    public async Task Cell_RunawayLoopStopsOnCancelWhileOtherCellsKeepRunning()
    {
        await using var worker = await Worker.StartAsync();
        await worker.WriteAsync("cell_loop", "cell.start", StartPayload("text('started'); while (true) {}"));
        Assert.Equal("started", (await worker.ReadAsync())["payload"]!["text"]!.GetValue<string>());

        await worker.WriteAsync("cell_loop", "cell.cancel", new JsonObject { ["reason"] = "timeout" });
        var done = await worker.ReadAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("cell_loop", done["scope"]!.GetValue<string>());
        Assert.Equal("timedOut", done["payload"]!["outcome"]!.GetValue<string>());

        await worker.WriteAsync("cell_next", "cell.start", StartPayload("text(typeof eval + typeof console + Object.isFrozen(tools));"));
        Assert.Equal("undefinedundefinedtrue", (await worker.ReadAsync())["payload"]!["text"]!.GetValue<string>());
        Assert.Equal("completed", (await worker.ReadAsync())["payload"]!["outcome"]!.GetValue<string>());
    }

    [Fact]
    public async Task Cell_ReportsUncaughtErrorWithStack()
    {
        await using var worker = await Worker.StartAsync();
        await worker.WriteAsync("cell_1", "cell.start", StartPayload("\n\nthrow new Error('boom');"));

        var done = await worker.ReadAsync();
        Assert.Equal("failed", done["payload"]!["outcome"]!.GetValue<string>());
        Assert.Contains("boom", done["payload"]!["error"]!.GetValue<string>());
        Assert.Contains(":3", done["payload"]!["stack"]!.GetValue<string>());
    }

    private static JsonObject StartPayload(string source, JsonObject? store = null) => new()
    {
        ["source"] = source,
        ["tools"] = new JsonArray(
            new JsonObject { ["name"] = "Alpha", ["description"] = "Alpha tool" },
            new JsonObject { ["name"] = "mcp__docs__search", ["description"] = "Search docs" }),
        ["aliases"] = new JsonObject(),
        ["store"] = store ?? new JsonObject(),
        ["limits"] = new JsonObject
        {
            ["maxMemoryBytes"] = 64L * 1024 * 1024,
            ["maxStatements"] = int.MaxValue,
            ["maxRecursionDepth"] = 128,
            ["engineTimeoutMs"] = 60_000,
            ["regexTimeoutMs"] = 10_000,
            ["maxOutputBytes"] = 1024 * 1024
        }
    };

    private sealed class Worker : IAsyncDisposable
    {
        private readonly Process _process;
        private long _sequence;

        private Worker(Process process) => _process = process;

        public static async Task<Worker> StartAsync()
        {
            var startInfo = new ProcessStartInfo("dotnet")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(typeof(CommandLineArgs).Assembly.Location);
            startInfo.ArgumentList.Add("script-worker");
            startInfo.ArgumentList.Add("code-mode");
            var worker = new Worker(Process.Start(startInfo) ?? throw new InvalidOperationException("Worker did not start."));
            Assert.Equal("ready", (await worker.ReadAsync())["type"]!.GetValue<string>());
            return worker;
        }

        public async Task WriteAsync(string scope, string type, JsonNode? payload)
        {
            var frame = new JsonObject
            {
                ["version"] = 1,
                ["sequence"] = ++_sequence,
                ["scope"] = scope,
                ["type"] = type,
                ["payload"] = payload
            };
            await _process.StandardInput.WriteLineAsync(frame.ToJsonString());
            await _process.StandardInput.FlushAsync();
        }

        public async Task<JsonObject> ReadAsync() =>
            JsonNode.Parse(await _process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30))
                           ?? throw new EndOfStreamException(await _process.StandardError.ReadToEndAsync()))!.AsObject();

        public async ValueTask DisposeAsync()
        {
            _process.StandardInput.Close();
            try
            {
                await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally
            {
                if (!_process.HasExited) _process.Kill(entireProcessTree: true);
                _process.Dispose();
            }
        }
    }
}
