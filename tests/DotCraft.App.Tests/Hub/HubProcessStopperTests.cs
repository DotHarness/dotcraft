using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using DotCraft.Hub;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DotCraft.Tests.Hub;

public sealed class HubProcessStopperTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "hub-stop-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stop_ConfirmsExitAfterGracefulOrForcedShutdown(bool force)
    {
        using var process = StartWaitingProcess();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            Assert.Equal("ready", await process.StandardOutput.ReadLineAsync(deadline.Token));
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var paths = HubPaths.Resolve(_home);
            Assert.True(HubLockFile.TryAcquire(paths, out var file, out _));
            using var owned = file!;
            var info = Identity(process, port);
            owned.Publish(info);
            var requestBody = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var server = RespondToShutdownAsync(listener, process, force, requestBody, deadline.Token);

            await HubProcessStopper.StopAsync(paths, info.Pid, info.StartedAt, info.BinaryPath!,
                force ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromSeconds(3), deadline.Token);

            await server.WaitAsync(deadline.Token);
            Assert.True(process.HasExited);
            var request = await requestBody.Task.WaitAsync(deadline.Token);
            Assert.Contains("/v1/shutdown", request);
            Assert.Contains("Bearer " + info.Token, request);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }

    [Theory]
    [InlineData("startedAt")]
    [InlineData("pid")]
    [InlineData("dll")]
    [InlineData("host")]
    public async Task Stop_RejectsReusedOrChangedIdentityWithoutKillingProcess(string mismatch)
    {
        using var process = StartWaitingProcess();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Assert.Equal("ready", await process.StandardOutput.ReadLineAsync(deadline.Token));
        try
        {
            var paths = HubPaths.Resolve(_home);
            Assert.True(HubLockFile.TryAcquire(paths, out var file, out _));
            using var owned = file!;
            var info = Identity(process, 1);
            info = mismatch switch
            {
                "dll" => info with { BinaryPath = Path.Combine(_home, "other.dll") },
                "host" => info with { BinaryPath = typeof(HubHost).Assembly.Location },
                _ => info
            };
            owned.Publish(mismatch switch
            {
                "startedAt" => info with { StartedAt = info.StartedAt.AddSeconds(1) },
                "pid" => info with { Pid = info.Pid + 1 },
                _ => info
            });
            await Assert.ThrowsAsync<InvalidOperationException>(() => HubProcessStopper.StopAsync(
                paths, info.Pid, mismatch == "startedAt" ? info.StartedAt.AddSeconds(1) : info.StartedAt,
                info.BinaryPath!, TimeSpan.Zero, deadline.Token));
            Assert.False(process.HasExited);
        }
        finally
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(deadline.Token);
        }
    }

    [Fact]
    public async Task Stop_WithoutDiscoveryWaitsForCapturedExitButDoesNotAuthorizeForce()
    {
        using var process = StartWaitingProcess();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Assert.Equal("ready", await process.StandardOutput.ReadLineAsync(deadline.Token));
        var info = Identity(process, 1);
        var paths = HubPaths.Resolve(_home);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => HubProcessStopper.StopAsync(paths,
                info.Pid, info.StartedAt, info.BinaryPath!, TimeSpan.FromMilliseconds(50), deadline.Token));
            Assert.False(process.HasExited);
            var stopping = HubProcessStopper.StopAsync(paths, info.Pid, info.StartedAt, info.BinaryPath!,
                TimeSpan.FromSeconds(3), deadline.Token);
            await process.StandardInput.WriteLineAsync("quit");
            await process.StandardInput.FlushAsync(deadline.Token);
            await stopping;
            Assert.True(process.HasExited);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(deadline.Token);
        }
    }

    [Fact]
    public async Task Stop_StopsDllHostedHubFromTheSameAssemblyAndHost()
    {
        var paths = HubPaths.Resolve(_home);
        Directory.CreateDirectory(paths.CraftHomePath);
        await File.WriteAllTextAsync(paths.GlobalConfigPath, """{"Hub":{"Port":0,"SatellitesEnabled":false}}""");
        var harness = CompileHubHarness();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var hub = StartHarness(harness, "hub");
        var hubOutput = hub.StandardOutput.ReadToEndAsync(deadline.Token);
        var hubError = hub.StandardError.ReadToEndAsync(deadline.Token);
        var hubExit = hub.WaitForExitAsync(deadline.Token);
        try
        {
            var info = await HubTestStartup.WaitForReadyAsync(paths.LockFilePath, hubExit, deadline.Token);
            Assert.Equal(hub.Id, info.Pid);
            Assert.Equal(typeof(HubHost).Assembly.Location, info.BinaryPath);
            using var stop = StartHarness(harness, "stop", info.Pid.ToString(),
                info.StartedAt.ToString("O"), info.BinaryPath!);
            var stopOutput = stop.StandardOutput.ReadToEndAsync(deadline.Token);
            var stopError = stop.StandardError.ReadToEndAsync(deadline.Token);
            try
            {
                await stop.WaitForExitAsync(deadline.Token);
                Assert.True(stop.ExitCode == 0, await stopOutput + await stopError);
                await hubExit;
                Assert.True(hub.ExitCode == 0, await hubOutput + await hubError);
            }
            finally
            {
                if (!stop.HasExited) stop.Kill(entireProcessTree: true);
                await stop.WaitForExitAsync();
            }
        }
        finally
        {
            if (!hub.HasExited) hub.Kill(entireProcessTree: true);
            await hub.WaitForExitAsync();
        }
    }

    private string CompileHubHarness()
    {
        const string source = """
            using System;
            using System.IO;
            using System.Reflection;
            using System.Runtime.Loader;
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Configuration;
            using DotCraft.Hub;

            public static class Program
            {
                public static int Main(string[] args)
                {
                    AssemblyLoadContext.Default.Resolving += (_, name) =>
                        AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(args[2], name.Name + ".dll"));
                    return RunAsync(args).GetAwaiter().GetResult();
                }

                private static async Task<int> RunAsync(string[] args)
                {
                    var paths = HubPaths.Resolve(args[1]);
                    if (args[0] == "hub")
                    {
                        var config = AppConfig.Load(paths.GlobalConfigPath).GetSection<HubConfig>("Hub");
                        await using var hub = new HubHost(config, paths);
                        await hub.RunAsync();
                    }
                    else
                    {
                        var stopper = typeof(HubHost).Assembly.GetType("DotCraft.Hub.HubProcessStopper")!;
                        var stop = stopper.GetMethod("StopAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
                        await (Task)stop.Invoke(null, new object[] {
                            paths, int.Parse(args[3]), DateTimeOffset.Parse(args[4]), args[5],
                            TimeSpan.FromSeconds(10), CancellationToken.None
                        })!;
                    }
                    return 0;
                }
            }
            """;
        var output = Path.Combine(_home, "hub-harness.dll");
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(HubHost).Assembly.Location)
            .Append(typeof(DotCraft.Configuration.AppConfig).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("HubStopHarness", [CSharpSyntaxTree.ParseText(source)],
            references, new CSharpCompilationOptions(OutputKind.ConsoleApplication));
        using var stream = File.Create(output);
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return output;
    }

    private Process StartHarness(string harness, string mode, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = _home
        };
        foreach (var argument in new[] {
            "exec", "--runtimeconfig", Path.Combine(AppContext.BaseDirectory, "dotcraft.runtimeconfig.json"),
            "--depsfile", Path.Combine(AppContext.BaseDirectory, "dotcraft.deps.json"),
            harness, mode, _home, AppContext.BaseDirectory
        }.Concat(arguments))
            start.ArgumentList.Add(argument);
        return Process.Start(start)!;
    }

    private static Process StartWaitingProcess()
    {
        var start = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        if (OperatingSystem.IsWindows())
        {
            start.ArgumentList.Add("/d");
            start.ArgumentList.Add("/c");
            start.ArgumentList.Add("echo ready&set /p hold=");
        }
        else
        {
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add("printf 'ready\\n'; read hold");
        }
        return Process.Start(start)!;
    }

    private static HubLockInfo Identity(Process process, int port) => new(
        process.Id, $"http://127.0.0.1:{port}", "captured-token",
        new DateTimeOffset(process.StartTime.ToUniversalTime()), "test", process.MainModule!.FileName);

    private static async Task RespondToShutdownAsync(TcpListener listener, Process process, bool force,
        TaskCompletionSource<string> observed, CancellationToken ct)
    {
        using var socket = await listener.AcceptTcpClientAsync(ct);
        await using var stream = socket.GetStream();
        var bytes = new byte[4096];
        var request = new StringBuilder();
        while (!request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
        {
            var count = await stream.ReadAsync(bytes, ct);
            if (count == 0) throw new EndOfStreamException();
            request.Append(Encoding.UTF8.GetString(bytes, 0, count));
        }
        observed.SetResult(request.ToString());
        await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\n{}"), ct);
        if (!force)
        {
            await process.StandardInput.WriteLineAsync("quit");
            await process.StandardInput.FlushAsync(ct);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_home)) Directory.Delete(_home, true);
    }
}
