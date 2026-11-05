using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DotCraft.Hub;
using Xunit;

namespace DotCraft.Tests.Hub;

public sealed class HubLifecycleTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "hub-lifecycle-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CoreApiAndProcessIdentity_AreAvailableWhileOptionalListenersStart()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var paths = HubPaths.Resolve(_home);
        await using var host = new HubHost(new HubConfig { Port = 0 }, paths)
        {
            OptionalListenersStartup = async ct =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
        };
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var run = host.RunAsync(deadline.Token);
        await entered.Task.WaitAsync(deadline.Token);
        var info = Assert.IsType<HubLockInfo>(HubLockFile.TryRead(paths.LockFilePath));
        using var process = Process.GetCurrentProcess();
        Assert.Equal(new DateTimeOffset(process.StartTime.ToUniversalTime()), info.StartedAt);
        using var http = AuthorizedClient(info);
        using var response = await http.GetAsync($"{info.ApiBaseUrl}/v1/status", deadline.Token);
        response.EnsureSuccessStatusCode();
        using var stopped = await http.PostAsJsonAsync($"{info.ApiBaseUrl}/v1/shutdown", new { expectedPid = info.Pid }, deadline.Token);
        stopped.EnsureSuccessStatusCode();
        await run.WaitAsync(deadline.Token);
        Assert.False(File.Exists(paths.LockFilePath));
    }

    [Fact]
    public async Task Shutdown_RejectsWrongIdentityAndNewStarts()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var paths = HubPaths.Resolve(_home);
        await using var host = new HubHost(new HubConfig { Port = 0 }, paths)
        {
            OptionalListenersStartup = async _ => { entered.SetResult(); await release.Task; }
        };
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var run = host.RunAsync(deadline.Token);
        await entered.Task.WaitAsync(deadline.Token);
        var info = Assert.IsType<HubLockInfo>(HubLockFile.TryRead(paths.LockFilePath));
        using var http = AuthorizedClient(info);
        try
        {
            using var wrong = await http.PostAsJsonAsync($"{info.ApiBaseUrl}/v1/shutdown", new { expectedPid = info.Pid + 1 }, deadline.Token);
            Assert.Equal(HttpStatusCode.Conflict, wrong.StatusCode);
            using var stopped = await http.PostAsJsonAsync($"{info.ApiBaseUrl}/v1/shutdown", new { expectedPid = info.Pid }, deadline.Token);
            stopped.EnsureSuccessStatusCode();
            foreach (var endpoint in new[] { "/v1/appservers/ensure", "/v1/appservers/restart", "/v1/services/ensure", "/v1/services/restart" })
            {
                using var rejected = await http.PostAsJsonAsync(info.ApiBaseUrl + endpoint, new { }, deadline.Token);
                Assert.Equal(HttpStatusCode.ServiceUnavailable, rejected.StatusCode);
                Assert.Contains("hubStopping", await rejected.Content.ReadAsStringAsync(deadline.Token));
            }
        }
        finally { release.TrySetResult(); }
        await run.WaitAsync(deadline.Token);
        Assert.False(File.Exists(paths.LockFilePath));
    }

    [Fact]
    public async Task UnpublishedLockOwner_IsWaitedForRatherThanTreatedAsMissing()
    {
        var paths = HubPaths.Resolve(_home);
        Assert.True(HubLockFile.TryAcquire(paths, out var owner, out _));
        await using var host = new HubHost(new HubConfig { Port = 0 }, paths);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var run = host.RunAsync(deadline.Token);
        Assert.False(run.IsCompleted);
        owner!.DeleteAfterDispose();
        var info = await HubTestStartup.WaitForReadyAsync(paths.LockFilePath, run, deadline.Token);
        using var http = AuthorizedClient(info);
        using var stopped = await http.PostAsync($"{info.ApiBaseUrl}/v1/shutdown", null, deadline.Token);
        stopped.EnsureSuccessStatusCode();
        await run.WaitAsync(deadline.Token);
    }

    private static HttpClient AuthorizedClient(HubLockInfo info)
    {
        var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", info.Token);
        return http;
    }

    public void Dispose()
    {
        if (Directory.Exists(_home)) Directory.Delete(_home, true);
    }
}
