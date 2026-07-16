using System.Diagnostics;
using System.Text.Json.Nodes;
using DotCraft.GeneratedTools.Core;
using DotCraft.Protocol;
using DotCraft.Teams;
using DotCraft.Tests.Sessions.Protocol.AppServer;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit.Abstractions;

namespace DotCraft.Tests.Tools;

public sealed class GeneratedToolPerformanceDiagnosticsTests(ITestOutputHelper output)
{
    [Fact]
    public async Task GeneratedToolDiagnostics_MeasureConstructionAndNativeInvocation()
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

            var sessions = new TestableSessionService(new ThreadStore(tempRoot));
            var teamsService = new TeamsService();
            teamsService.SetSessionService(sessions);
            var thread = await sessions.CreateThreadAsync(
                new SessionIdentity
                {
                    WorkspacePath = tempRoot,
                    ChannelName = "desktop",
                    ChannelContext = "performance",
                    UserId = "user"
                },
                new ThreadConfiguration { Mode = "agent" });
            var planning = new ToolPlanningContext(
                thread.Id,
                null,
                tempRoot,
                "agent",
                null,
                [],
                1,
                ToolPlanningThreadKind.UserTopLevel);
            var snapshot = await new EffectiveToolSnapshotBuilder().BuildAsync(
                [new TeamsToolSource(teamsService)],
                planning);
            var providerName = snapshot.ProviderFlatNames[new ToolName(TeamsConstants.ToolNamespace, "CreateTeam")];
            var dispatcher = new ToolDispatcher();
            var nativeInvocation = await MeasureAsync(
                200,
                () => InvokeExpectedInvalidParamsAsync(dispatcher, snapshot, providerName, thread.Id));

            output.WriteLine($"Teams CreateTeam invalid-args native-dispatch={nativeInvocation.Elapsed.TotalMilliseconds:F2}ms checksum={nativeInvocation.Checksum}");
            Assert.Equal(200, nativeInvocation.Checksum);
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

    private static async Task InvokeExpectedInvalidParamsAsync(
        ToolDispatcher dispatcher,
        EffectiveToolSnapshot snapshot,
        string providerName,
        string threadId)
    {
        var result = await dispatcher.DispatchProviderFlatCallAsync(
            snapshot,
            providerName,
            new JsonObject(),
            new ToolInvocationRequest(
                threadId,
                null,
                "call",
                ToolInvocationAudience.Model));
        Assert.False(result.Success);
        Assert.Equal(ToolErrorCodes.InputInvalid, result.Error?.Code);
    }

    private readonly record struct Measurement(TimeSpan Elapsed, int Checksum);
}
