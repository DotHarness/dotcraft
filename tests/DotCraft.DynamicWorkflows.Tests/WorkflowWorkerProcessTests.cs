using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.CLI;

namespace DotCraft.DynamicWorkflows.Tests;

public sealed class WorkflowWorkerProcessTests
{
    [Fact]
    public async Task HiddenWorker_ExecutesAgentlessScriptAndKeepsStdoutAsJsonl()
    {
        var script = "export const meta = { name: 'smoke', description: 'Smoke test' }; const n = await Promise.resolve(2); return { n };";
        var parsed = new DynamicWorkflowParser().Parse(script);
        var appAssembly = typeof(CommandLineArgs).Assembly.Location;
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(appAssembly);
        startInfo.ArgumentList.Add("workflow-worker");
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Worker did not start.");
        var frame = new JsonObject
        {
            ["version"] = 1,
            ["runId"] = "run_test_000001",
            ["attemptId"] = "attempt_001",
            ["sequence"] = 1,
            ["type"] = "initialize",
            ["payload"] = new JsonObject
            {
                ["script"] = script,
                ["scriptHash"] = parsed.SourceHash,
                ["args"] = new JsonObject(),
                ["cwd"] = Environment.CurrentDirectory,
                ["limits"] = JsonSerializer.SerializeToNode(new DynamicWorkflowLimits { RunTimeout = TimeSpan.FromSeconds(15) }),
                ["budget"] = new JsonObject()
            }
        };
        await process.StandardInput.WriteLineAsync(frame.ToJsonString());
        await process.StandardInput.FlushAsync();

        var ready = JsonNode.Parse(await process.StandardOutput.ReadLineAsync() ?? "")!.AsObject();
        var complete = JsonNode.Parse(await process.StandardOutput.ReadLineAsync() ?? "")!.AsObject();
        Assert.Equal("ready", ready["type"]!.GetValue<string>());
        Assert.Equal("complete", complete["type"]!.GetValue<string>());
        Assert.Equal(2, complete["payload"]!["result"]!["n"]!.GetValue<int>());
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public async Task HiddenWorker_ParallelAgents_PreservesDeclarationOrder()
    {
        var script = """
            export const meta = { name: 'parallel', description: 'Parallel test' };
            const values = await parallel([
              () => agent('first', { label: 'first' }),
              () => agent('second', { label: 'second' })
            ]);
            return values;
            """;
        var parsed = new DynamicWorkflowParser().Parse(script);
        using var process = StartWorker();
        await WriteFrameAsync(process, 1, "initialize", new JsonObject
        {
            ["script"] = script,
            ["scriptHash"] = parsed.SourceHash,
            ["args"] = new JsonObject(),
            ["cwd"] = Environment.CurrentDirectory,
            ["limits"] = JsonSerializer.SerializeToNode(new DynamicWorkflowLimits { RunTimeout = TimeSpan.FromSeconds(15) }),
            ["budget"] = new JsonObject()
        });

        Assert.Equal("ready", (await ReadFrameAsync(process))["type"]!.GetValue<string>());
        var first = await ReadFrameAsync(process);
        var second = await ReadFrameAsync(process);
        Assert.Equal("agent.request", first["type"]!.GetValue<string>());
        Assert.Equal("agent.request", second["type"]!.GetValue<string>());
        Assert.Equal("first", first["payload"]!["options"]!["label"]!.GetValue<string>());
        Assert.Equal("first", first["payload"]!["input"]!.GetValue<string>());
        Assert.Equal("second", second["payload"]!["options"]!["label"]!.GetValue<string>());
        var firstId = first["payload"]!["operationId"]!.GetValue<string>();
        var secondId = second["payload"]!["operationId"]!.GetValue<string>();
        await WriteFrameAsync(process, 2, "agent.result", new JsonObject
        {
            ["operationId"] = secondId,
            ["result"] = new JsonObject { ["value"] = "B" }
        });
        await WriteFrameAsync(process, 3, "agent.result", new JsonObject
        {
            ["operationId"] = firstId,
            ["result"] = "A"
        });
        var complete = await ReadFrameAsync(process);
        Assert.Equal("complete", complete["type"]!.GetValue<string>());
        Assert.Equal("A", complete["payload"]!["result"]![0]!.GetValue<string>());
        Assert.Equal("B", complete["payload"]!["result"]![1]!["value"]!.GetValue<string>());
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public async Task HiddenWorker_PipelineSerializesStagesAndPropagatesNull()
    {
        var script = """
            export const meta = { name: 'pipeline', description: 'Pipeline test' };
            return await pipeline([1, null, 2],
              async value => value === null ? null : value + 1,
              value => value === 2 ? null : value * 2
            );
            """;
        var parsed = new DynamicWorkflowParser().Parse(script);
        using var process = StartWorker();
        await WriteFrameAsync(process, 1, "initialize", new JsonObject
        {
            ["script"] = script,
            ["scriptHash"] = parsed.SourceHash,
            ["args"] = new JsonObject(),
            ["cwd"] = Environment.CurrentDirectory,
            ["limits"] = JsonSerializer.SerializeToNode(new DynamicWorkflowLimits { RunTimeout = TimeSpan.FromSeconds(15) }),
            ["budget"] = new JsonObject()
        });

        Assert.Equal("ready", (await ReadFrameAsync(process))["type"]!.GetValue<string>());
        var complete = await ReadFrameAsync(process);
        Assert.Equal("complete", complete["type"]!.GetValue<string>());
        Assert.Null(complete["payload"]!["result"]![0]);
        Assert.Null(complete["payload"]!["result"]![1]);
        Assert.Equal(6, complete["payload"]!["result"]![2]!.GetValue<int>());
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public async Task HiddenWorker_CancelInterruptsInfiniteLoop()
    {
        var script = "export const meta = { name: 'cancel', description: 'Cancel test' }; while (true) {}";
        var parsed = new DynamicWorkflowParser().Parse(script);
        using var process = StartWorker();
        try
        {
            await WriteFrameAsync(process, 1, "initialize", new JsonObject
            {
                ["script"] = script,
                ["scriptHash"] = parsed.SourceHash,
                ["args"] = new JsonObject(),
                ["cwd"] = Environment.CurrentDirectory,
                ["limits"] = JsonSerializer.SerializeToNode(new DynamicWorkflowLimits
                {
                    RunTimeout = TimeSpan.FromSeconds(30),
                    MaxStatements = int.MaxValue
                }),
                ["budget"] = new JsonObject()
            });

            Assert.Equal("ready", (await ReadFrameAsync(process))["type"]!.GetValue<string>());
            await WriteFrameAsync(process, 2, "cancel", null);
            var failed = await ReadFrameAsync(process).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("failed", failed["type"]!.GetValue<string>());
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotEqual(0, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public async Task HiddenWorker_InjectsFrozenJsonArgumentsAndRestrictedCwd()
    {
        var script = """
            export const meta = { name: 'globals', description: 'Globals test' };
            let argsAssignmentBlocked = false;
            let cwdAssignmentBlocked = false;
            try { args = {}; } catch { argsAssignmentBlocked = true; }
            try { cwd = 'changed'; } catch { cwdAssignmentBlocked = true; }
            return {
              x: args.x,
              frozen: Object.isFrozen(args),
              nestedFrozen: Object.isFrozen(args.nested),
              cwd,
              processCwd: process.cwd(),
              maxAgentCalls: budget.maxAgentCalls,
              argsAssignmentBlocked,
              cwdAssignmentBlocked,
              hostAgent: typeof globalThis.__agent,
              hostPhase: typeof globalThis.__phase
            };
            """;
        var parsed = new DynamicWorkflowParser().Parse(script);
        using var process = StartWorker();
        await WriteFrameAsync(process, 1, "initialize", new JsonObject
        {
            ["script"] = script,
            ["scriptHash"] = parsed.SourceHash,
            ["args"] = new JsonObject { ["x"] = 7, ["nested"] = new JsonObject { ["ok"] = true } },
            ["cwd"] = "C:/workspace",
            ["limits"] = JsonSerializer.SerializeToNode(new DynamicWorkflowLimits { RunTimeout = TimeSpan.FromSeconds(15) }),
            ["budget"] = new JsonObject { ["maxAgentCalls"] = 1000 }
        });

        Assert.Equal("ready", (await ReadFrameAsync(process))["type"]!.GetValue<string>());
        var result = (await ReadFrameAsync(process))["payload"]!["result"]!;
        Assert.Equal(7, result["x"]!.GetValue<int>());
        Assert.True(result["frozen"]!.GetValue<bool>());
        Assert.True(result["nestedFrozen"]!.GetValue<bool>());
        Assert.Equal("C:/workspace", result["cwd"]!.GetValue<string>());
        Assert.Equal("C:/workspace", result["processCwd"]!.GetValue<string>());
        Assert.Equal(1000, result["maxAgentCalls"]!.GetValue<int>());
        Assert.True(result["argsAssignmentBlocked"]!.GetValue<bool>());
        Assert.True(result["cwdAssignmentBlocked"]!.GetValue<bool>());
        Assert.Equal("undefined", result["hostAgent"]!.GetValue<string>());
        Assert.Equal("undefined", result["hostPhase"]!.GetValue<string>());
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public async Task HiddenWorker_DisablesIndirectStringCompilation()
    {
        var script = "export const meta = { name: 'dynamic', description: 'Dynamic code test' }; return (() => {}).constructor('return 1')();";
        var parsed = new DynamicWorkflowParser().Parse(script);
        using var process = StartWorker();
        await WriteFrameAsync(process, 1, "initialize", new JsonObject
        {
            ["script"] = script,
            ["scriptHash"] = parsed.SourceHash,
            ["args"] = new JsonObject(),
            ["cwd"] = Environment.CurrentDirectory,
            ["limits"] = JsonSerializer.SerializeToNode(new DynamicWorkflowLimits { RunTimeout = TimeSpan.FromSeconds(15) }),
            ["budget"] = new JsonObject()
        });

        Assert.Equal("ready", (await ReadFrameAsync(process))["type"]!.GetValue<string>());
        Assert.Equal("failed", (await ReadFrameAsync(process))["type"]!.GetValue<string>());
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotEqual(0, process.ExitCode);
    }

    [Theory]
    [InlineData("return () => 1;")]
    [InlineData("return Symbol('x');")]
    [InlineData("return 1n;")]
    [InlineData("return NaN;")]
    [InlineData("return new Map([['x', 1]]);")]
    public async Task HiddenWorker_RejectsNonJsonResults(string body)
    {
        var script = $"export const meta = {{ name: 'json', description: 'JSON test' }}; {body}";
        var parsed = new DynamicWorkflowParser().Parse(script);
        using var process = StartWorker();
        await WriteFrameAsync(process, 1, "initialize", new JsonObject
        {
            ["script"] = script,
            ["scriptHash"] = parsed.SourceHash,
            ["args"] = new JsonObject(),
            ["cwd"] = Environment.CurrentDirectory,
            ["limits"] = JsonSerializer.SerializeToNode(new DynamicWorkflowLimits { RunTimeout = TimeSpan.FromSeconds(15) }),
            ["budget"] = new JsonObject()
        });

        Assert.Equal("ready", (await ReadFrameAsync(process))["type"]!.GetValue<string>());
        Assert.Equal("failed", (await ReadFrameAsync(process))["type"]!.GetValue<string>());
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotEqual(0, process.ExitCode);
    }

    private static Process StartWorker()
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
        startInfo.ArgumentList.Add("workflow-worker");
        return Process.Start(startInfo) ?? throw new InvalidOperationException("Worker did not start.");
    }

    private static async Task WriteFrameAsync(Process process, long sequence, string type, JsonNode? payload)
    {
        var frame = new JsonObject
        {
            ["version"] = 1,
            ["runId"] = "run_test_000001",
            ["attemptId"] = "attempt_001",
            ["sequence"] = sequence,
            ["type"] = type,
            ["payload"] = payload
        };
        await process.StandardInput.WriteLineAsync(frame.ToJsonString());
        await process.StandardInput.FlushAsync();
    }

    private static async Task<JsonObject> ReadFrameAsync(Process process) =>
        JsonNode.Parse(await process.StandardOutput.ReadLineAsync() ?? throw new EndOfStreamException())!.AsObject();
}
