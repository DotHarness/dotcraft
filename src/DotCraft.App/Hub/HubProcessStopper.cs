using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace DotCraft.Hub;

internal static class HubProcessStopper
{
    internal static async Task StopAsync(
        HubPaths paths,
        int expectedPid,
        DateTimeOffset expectedStartedAt,
        string expectedExecutable,
        TimeSpan grace,
        CancellationToken cancellationToken)
    {
        if (expectedPid <= 0 || expectedPid == Environment.ProcessId || grace < TimeSpan.Zero || grace > TimeSpan.FromMinutes(5))
            throw new ArgumentException("Invalid Hub stop identity or grace period.");

        using var process = OpenProcess(expectedPid);
        if (process is null)
            return;

        // Opening the handle now pins the OS process identity through shutdown and force termination.
        try { _ = process.SafeHandle; }
        catch (InvalidOperationException) when (process.HasExited) { return; }
        if (!MatchesProcess(process, expectedStartedAt, expectedExecutable))
        {
            if (process.HasExited) return;
            throw new InvalidOperationException("Hub process identity does not match the captured installation.");
        }

        var hub = HubLockFile.TryRead(paths.LockFilePath);
        if (hub is null)
        {
            using var exiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            exiting.CancelAfter(grace);
            try { await process.WaitForExitAsync(exiting.Token); return; }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException("Hub discovery disappeared before the captured process exited; no process was terminated.");
            }
        }
        if (hub.Pid != expectedPid || hub.StartedAt != expectedStartedAt
            || !SamePath(hub.BinaryPath, expectedExecutable))
        {
            if (process.HasExited) return;
            throw new InvalidOperationException("Hub discovery identity changed; no process was stopped.");
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(grace);
        try
        {
            using var http = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{hub.ApiBaseUrl}/v1/shutdown")
            {
                Content = JsonContent.Create(new { expectedPid })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", hub.Token);
            using var response = await http.SendAsync(request, deadline.Token);
            response.EnsureSuccessStatusCode();
            await process.WaitForExitAsync(deadline.Token);
            return;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
        {
            if (process.HasExited)
                return;
            try { await process.WaitForExitAsync(deadline.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (process.HasExited)
            return;
        if (!MatchesProcess(process, expectedStartedAt, expectedExecutable))
            throw new InvalidOperationException("Hub process identity could not be confirmed for forced shutdown.");
        process.Kill();
        using var forced = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        forced.CancelAfter(TimeSpan.FromSeconds(5));
        await process.WaitForExitAsync(forced.Token);
    }

    private static Process? OpenProcess(int pid)
    {
        try
        {
            var process = Process.GetProcessById(pid);
            if (!process.HasExited)
                return process;
            process.Dispose();
        }
        catch (ArgumentException) { }
        return null;
    }

    private static bool MatchesProcess(Process process, DateTimeOffset startedAt, string executable)
    {
        try
        {
            var actualStartedAt = new DateTimeOffset(process.StartTime.ToUniversalTime());
            var hostPath = executable;
            if (Path.GetExtension(executable).Equals(".dll", StringComparison.OrdinalIgnoreCase))
            {
                if (!SamePath(executable, typeof(HubProcessStopper).Assembly.Location)
                    || Environment.ProcessPath is not { } currentHost)
                    return false;
                hostPath = currentHost;
            }
            var toleranceMilliseconds = OperatingSystem.IsLinux() ? 10 : 1;
            return Math.Abs((actualStartedAt - startedAt).TotalMilliseconds) < toleranceMilliseconds
                && SamePath(process.MainModule?.FileName, hostPath);
        }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    private static bool SamePath(string? actual, string expected) =>
        actual is not null && string.Equals(Path.GetFullPath(actual), Path.GetFullPath(expected),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
