using System.Diagnostics;
using DotCraft.GeneratedTools.Core;
using DotCraft.Tests.Sessions.Protocol.AppServer;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit.Abstractions;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class GeneratedToolPerformanceDiagnosticsTests(ITestOutputHelper output)
{
    [Fact]
    public void GeneratedToolDiagnostics_MeasureConstruction()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"generated_tool_perf_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var shellTools = new ShellTools(
                tempRoot,
                new StubBackgroundTerminalService(),
                timeoutSeconds: 1,
                requireApprovalOutsideWorkspace: false);
            var generatedConstruction = Measure(1_000, () => GeneratedToolFunctions.ShellTools_Exec(shellTools));
            var factoryConstruction = Measure(1_000, () => AIFunctionFactory.Create(shellTools.Exec));

            output.WriteLine($"ShellTools.Exec construction generated={generatedConstruction.Elapsed.TotalMilliseconds:F2}ms factory={factoryConstruction.Elapsed.TotalMilliseconds:F2}ms checksum={generatedConstruction.Checksum + factoryConstruction.Checksum}");
            Assert.True(generatedConstruction.Elapsed > TimeSpan.Zero);
            Assert.True(factoryConstruction.Elapsed > TimeSpan.Zero);

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

    private readonly record struct Measurement(TimeSpan Elapsed, int Checksum);
}
