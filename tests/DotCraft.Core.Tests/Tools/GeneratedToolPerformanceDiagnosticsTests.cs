using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
using DotCraft.AppBinding;
using DotCraft.GeneratedTools.Core;
using DotCraft.Protocol.AppServer;
using DotCraft.Teams;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit.Abstractions;

namespace DotCraft.Tests.Tools;

public sealed class GeneratedToolPerformanceDiagnosticsTests(ITestOutputHelper output)
{
    [Fact]
    public async Task GeneratedToolDiagnostics_MeasureConstructionAndDynamicInvocation()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"generated_tool_perf_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var shellTools = new ShellTools(tempRoot, timeoutSeconds: 1, requireApprovalOutsideWorkspace: false);
            var generatedConstruction = Measure(1_000, () => GeneratedToolFunctions.ShellTools_Exec(shellTools));
            var factoryConstruction = Measure(1_000, () => AIFunctionFactory.Create(shellTools.Exec));

            output.WriteLine($"ShellTools.Exec construction generated={generatedConstruction.Elapsed.TotalMilliseconds:F2}ms factory={factoryConstruction.Elapsed.TotalMilliseconds:F2}ms checksum={generatedConstruction.Checksum + factoryConstruction.Checksum}");
            Assert.True(generatedConstruction.Elapsed > TimeSpan.Zero);
            Assert.True(factoryConstruction.Elapsed > TimeSpan.Zero);

            var generatedRegistry = ReadGeneratedTeamsRegistry();
            var fallbackRegistry = new ManagedDynamicToolRegistry<TeamsService>(TeamsConstants.ToolNamespace);
            var generatedInvocation = await MeasureAsync(200, () => InvokeExpectedInvalidParamsAsync(generatedRegistry));
            var fallbackInvocation = await MeasureAsync(200, () => InvokeExpectedInvalidParamsAsync(fallbackRegistry));

            output.WriteLine($"Teams ReadMemberStatus invalid-args generated={generatedInvocation.Elapsed.TotalMilliseconds:F2}ms fallback={fallbackInvocation.Elapsed.TotalMilliseconds:F2}ms checksum={generatedInvocation.Checksum + fallbackInvocation.Checksum}");
            Assert.Equal(200, generatedInvocation.Checksum);
            Assert.Equal(200, fallbackInvocation.Checksum);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // Best-effort cleanup on Windows.
            }
        }
    }

    private static Measurement Measure(int iterations, Func<AIFunction> factory)
    {
        var checksum = 0;
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
            checksum += factory().Name.Length;
        stopwatch.Stop();
        return new Measurement(stopwatch.Elapsed, checksum);
    }

    private static async Task<Measurement> MeasureAsync(int iterations, Func<Task> action)
    {
        var checksum = 0;
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            await action();
            checksum++;
        }
        stopwatch.Stop();
        return new Measurement(stopwatch.Elapsed, checksum);
    }

    private static async Task InvokeExpectedInvalidParamsAsync(IManagedDynamicToolRegistry<TeamsService> registry)
    {
        var ex = await Assert.ThrowsAsync<AppServerException>(async () =>
            await registry.InvokeAsync(
                new TeamsService(),
                Context("ReadMemberStatus"),
                new JsonObject(),
                CancellationToken.None));
        Assert.Equal(AppServerErrors.InvalidParamsCode, ex.Code);
    }

    private static IManagedDynamicToolRegistry<TeamsService> ReadGeneratedTeamsRegistry()
    {
        var field = typeof(TeamsService).GetField("DynamicTools", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        return Assert.IsAssignableFrom<IManagedDynamicToolRegistry<TeamsService>>(field.GetValue(null));
    }

    private static ManagedAppBindingToolCallContext Context(string toolName) =>
        new(
            WorkspaceCraftPath: "craft",
            WorkspacePath: "workspace",
            BindingId: "binding",
            ThreadId: "thread",
            TurnId: "turn",
            CallId: "call",
            AppId: "app",
            GrantId: "grant",
            ToolName: toolName);

    private readonly record struct Measurement(TimeSpan Elapsed, int Checksum);
}
