using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using DotCraft.Hub;
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
    [InlineData(true)]
    [InlineData(false)]
    public async Task Stop_RejectsReusedOrChangedIdentityWithoutKillingProcess(bool changedStart)
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
            owned.Publish(changedStart ? info with { StartedAt = info.StartedAt.AddSeconds(1) } : info with { Pid = info.Pid + 1 });
            await Assert.ThrowsAsync<InvalidOperationException>(() => HubProcessStopper.StopAsync(
                paths, info.Pid, changedStart ? info.StartedAt.AddSeconds(1) : info.StartedAt,
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
