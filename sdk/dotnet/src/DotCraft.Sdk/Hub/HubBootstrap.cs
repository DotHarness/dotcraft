using System.Diagnostics;
using System.Text.Json;
using DotCraft.Sdk.Wire;

namespace DotCraft.Sdk.Hub;

internal static class HubBootstrap
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    public static async Task<HubLockInfo> RunAsync(
        DotCraftHubClientOptions options,
        string lockPath,
        Func<HubLockInfo, CancellationToken, Task<HubLockInfo?>> probe,
        Func<HubLockInfo, CancellationToken, Task> shutdown,
        CancellationToken cancellationToken)
    {
        var shutdownDeadline = DateTimeOffset.UtcNow + options.ShutdownTimeout;
        var startedAt = DateTimeOffset.UtcNow;
        DateTimeOffset? startupStartedAt = null;
        HubLockInfo? lastOwner = null;
        HubLockInfo? startupOwner = null;
        int? stoppingPid = null;
        HubStartedProcess? child = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = HubClient.ReadHubLock(lockPath);
            var owner = info is not null && IsOwnerAlive(info) ? info : null;
            if (owner is not null)
            {
                lastOwner = owner;
                if (startupStartedAt is not null && owner.Pid != stoppingPid)
                    startupOwner = owner;
                if (owner.Pid != stoppingPid)
                {
                    var deadline = startupStartedAt is { } startup ? startup + options.StartupTimeout : shutdownDeadline;
                    using var probeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    probeCancellation.CancelAfter(Remaining(deadline));
                    HubLockInfo? live;
                    try
                    {
                        live = await probe(owner, probeCancellation.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        live = null;
                    }
                    if (live is not null)
                    {
                        var mismatch = GetBinaryMismatch(options, live);
                        if (mismatch is null)
                            return live;
                        if (options.BinaryMatchPolicy == HubBinaryMatchPolicy.ErrorIfMismatch)
                            throw new HubClientException("hubBinaryMismatch", "Hub is running from a different executable.", mismatch);

                        using var stopCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        stopCancellation.CancelAfter(Remaining(shutdownDeadline));
                        try
                        {
                            await shutdown(live, stopCancellation.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                        {
                            throw new HubClientException("hubMismatchShutdownTimeout", "Hub shutdown did not complete within the deadline.", mismatch);
                        }
                        catch (Exception error) when (error is not OperationCanceledException)
                        {
                            throw new HubClientException("hubMismatchShutdownFailed", "Hub uses a different executable and could not be stopped.", mismatch, error);
                        }
                        stoppingPid = live.Pid;
                    }
                }
                if (startupStartedAt is null && DateTimeOffset.UtcNow >= shutdownDeadline)
                    throw Failure("owner-wait", "The previous DotCraft Hub is still starting or shutting down.", options.ShutdownTimeout,
                        code: stoppingPid is null ? "hubUnavailable" : "hubMismatchShutdownTimeout");
            }
            else if (startupStartedAt is null && IsLockHeld(lockPath))
            {
                if (DateTimeOffset.UtcNow >= shutdownDeadline)
                    throw Failure("owner-wait", "Another DotCraft Hub owns the startup lock but has not published its identity.", options.ShutdownTimeout);
            }
            else if (startupStartedAt is null)
            {
                if (!options.StartHubIfMissing)
                    throw Failure("spawn", "DotCraft Hub is not running.", options.StartupTimeout);
                startupStartedAt = DateTimeOffset.UtcNow;
                try
                {
                    child = HubStartedProcess.Start(options, lockPath, cancellationToken);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    throw Failure("spawn", "DotCraft Hub could not be started.", options.StartupTimeout, error);
                }
            }

            if (child?.Failed == true && (owner is null || owner.Pid == child.Pid)
                || startupOwner is not null && owner is null)
            {
                throw Failure("ready", "DotCraft Hub exited before becoming ready.", options.StartupTimeout, earlyExit: true);
            }
            var nextDeadline = startupStartedAt is { } start ? start + options.StartupTimeout : shutdownDeadline;
            if (DateTimeOffset.UtcNow >= nextDeadline)
                throw Failure("ready", "DotCraft Hub did not become ready before the startup deadline.", options.StartupTimeout);

            var delay = Task.Delay(Min(PollInterval, Remaining(nextDeadline)), cancellationToken);
            if (child is not null && !child.Exited.IsCompleted)
                await Task.WhenAny(delay, child.Exited).ConfigureAwait(false);
            else
                await delay.ConfigureAwait(false);
        }

        HubClientException Failure(string stage, string message, TimeSpan budget, Exception? cause = null, bool earlyExit = false, string code = "hubUnavailable")
        {
            var elapsed = DateTimeOffset.UtcNow - (startupStartedAt ?? startedAt);
            var recentStderr = child?.RecentStderr;
            if (lastOwner?.Token is { Length: > 0 } token)
                recentStderr = recentStderr?.Replace(token, "[redacted]", StringComparison.Ordinal);
            var details = JsonSerializer.SerializeToElement(new
            {
                stage,
                failureKind = earlyExit ? "earlyExit" : cause is not null ? "spawnError" : "timeout",
                elapsedMs = (long)elapsed.TotalMilliseconds,
                budgetMs = (long)budget.TotalMilliseconds,
                lockPath,
                pid = lastOwner?.Pid,
                expectedExecutable = options.ExpectedExecutable,
                spawnedPid = child is { IsLauncher: false } ? child.Pid : startupOwner?.Pid,
                launcherPid = child is { IsLauncher: true } ? child.Pid : null,
                exitCode = child?.Failed == true ? child.ExitCode : null,
                recentStderr = recentStderr is null ? null : HubStartedProcess.Sanitize(recentStderr),
                spawnError = cause is null ? null : new { name = cause.GetType().Name }
            }, DotCraftJson.Options);
            return new HubClientException(code, message, details, cause);
        }
    }

    private static JsonElement? GetBinaryMismatch(DotCraftHubClientOptions options, HubLockInfo hub)
    {
        if (string.IsNullOrWhiteSpace(options.ExpectedExecutable) || options.BinaryMatchPolicy == HubBinaryMatchPolicy.Ignore)
            return null;
        var expected = Path.GetFullPath(options.ExpectedExecutable);
        var actual = string.IsNullOrWhiteSpace(hub.BinaryPath) ? null : Path.GetFullPath(hub.BinaryPath);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return actual is not null && string.Equals(actual, expected, comparison)
            ? null
            : JsonSerializer.SerializeToElement(new { expectedExecutable = expected, actualExecutable = actual }, DotCraftJson.Options);
    }

    internal static bool IsOwnerAlive(HubLockInfo owner)
    {
        try
        {
            using var process = Process.GetProcessById(owner.Pid);
            if (process.HasExited)
                return false;
            return owner.StartedAt is not { } publishedAt || publishedAt.AddSeconds(1) >= process.StartTime.ToUniversalTime();
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return HubClient.IsProcessAlive(owner.Pid); }
    }

    private static bool IsLockHeld(string path)
    {
        if (!File.Exists(path))
            return false;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    private static TimeSpan Remaining(DateTimeOffset deadline) =>
        deadline - DateTimeOffset.UtcNow is var remaining && remaining > TimeSpan.Zero ? remaining : TimeSpan.FromMilliseconds(1);

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;
}
