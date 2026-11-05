using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using DotCraft.Sdk.Hub;
using Xunit;

namespace DotCraft.Sdk.Tests;

public sealed class HubBootstrapTests
{
    [Fact]
    public async Task ConcurrentClientsShareStartupAndOneCancelledCallerDoesNotStopTheOther()
    {
        using var temp = new TemporaryDirectory();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var launches = 0;
        using var http = new HttpClient(new AsyncHandler(async (_, ct) =>
        {
            entered.TrySetResult();
            await ready.Task.WaitAsync(ct);
            return Json("{}");
        }));
        var options = new DotCraftHubClientOptions
        {
            HubLockPath = temp.LockPath,
            Executable = "fixture-dotcraft",
            HttpClientFactory = () => http,
            StartHubProcess = _ =>
            {
                Interlocked.Increment(ref launches);
                File.WriteAllText(temp.LockPath, OwnerJson);
                return null;
            }
        };
        using var cancelled = new CancellationTokenSource();
        var first = new HubClient(options).EnsureHubAsync(cancelled.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var second = new HubClient(options).EnsureHubAsync();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Equal(1, launches);
        ready.SetResult();
        Assert.Equal(Environment.ProcessId, (await second.WaitAsync(TimeSpan.FromSeconds(3))).Pid);
    }

    [Fact]
    public async Task CancellingTheLastCallerCancelsTheProbeWithoutStartingAReplacement()
    {
        using var temp = new TemporaryDirectory();
        File.WriteAllText(temp.LockPath, OwnerJson);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probeCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var launches = 0;
        using var http = new HttpClient(new AsyncHandler(async (_, ct) =>
        {
            entered.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            finally { probeCancelled.TrySetResult(); }
            return Json("{}");
        }));
        var client = new HubClient(new DotCraftHubClientOptions
        {
            HubLockPath = temp.LockPath,
            HttpClientFactory = () => http,
            StartHubProcess = _ => { launches++; return null; }
        });
        using var cancellation = new CancellationTokenSource();
        var pending = client.EnsureHubAsync(cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        File.Delete(temp.LockPath);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await probeCancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(0, launches);
    }

    [Fact]
    public async Task APrecancelledCallerCannotInvokeTheLauncher()
    {
        using var temp = new TemporaryDirectory();
        var launches = 0;
        var client = new HubClient(new DotCraftHubClientOptions
        {
            HubLockPath = temp.LockPath,
            StartHubProcess = _ => { launches++; return null; }
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.EnsureHubAsync(cancellation.Token));
        Assert.Equal(0, launches);
    }

    [Fact]
    public async Task AnIncompleteHeldLockWaitsForPublicationWithoutLaunchingAnotherHub()
    {
        using var temp = new TemporaryDirectory();
        using var owner = new FileStream(temp.LockPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
        var launches = 0;
        using var http = new HttpClient(new AsyncHandler((_, _) => Task.FromResult(Json("{}"))));
        var client = new HubClient(new DotCraftHubClientOptions
        {
            HubLockPath = temp.LockPath,
            HttpClientFactory = () => http,
            StartHubProcess = _ => { launches++; return null; }
        });
        var pending = client.EnsureHubAsync();
        Assert.False(pending.IsCompleted);
        owner.Write(Encoding.UTF8.GetBytes(OwnerJson));
        owner.Flush();
        Assert.Equal(Environment.ProcessId, (await pending.WaitAsync(TimeSpan.FromSeconds(3))).Pid);
        Assert.Equal(0, launches);
    }

    [Fact]
    public async Task EarlyExitReportsOnlyThisAttemptStderrAndPreservesExitCode()
    {
        using var temp = new TemporaryDirectory();
        var logPath = Path.Combine(temp.Path, "hub-startup.stderr.log");
        File.WriteAllText(logPath, "previous-attempt-secret\n");
        var client = new HubClient(new DotCraftHubClientOptions
        {
            HubLockPath = temp.LockPath,
            StartupTimeout = TimeSpan.FromSeconds(15),
            StartHubProcess = _ =>
            {
                File.AppendAllText(logPath, "new-attempt Bearer access-secret https://name:password@127.0.0.1:1234/failure?token=query-secret token=field-secret api_key=api-secret password=field-password\n");
                var info = new ProcessStartInfo
                {
                    FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                info.ArgumentList.Add(OperatingSystem.IsWindows() ? "/c" : "-c");
                info.ArgumentList.Add("exit 23");
                return Process.Start(info);
            }
        });
        var error = await Assert.ThrowsAsync<HubClientException>(
            () => client.EnsureHubAsync().WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal("earlyExit", error.Details?.GetProperty("failureKind").GetString());
        Assert.Equal(23, error.Details?.GetProperty("exitCode").GetInt32());
        var stderr = error.Details?.GetProperty("recentStderr").GetString() ?? "";
        Assert.Contains("new-attempt", stderr);
        foreach (var secret in new[] { "previous-attempt-secret", "access-secret", "query-secret", "field-secret", "api-secret", "field-password" })
            Assert.DoesNotContain(secret, stderr);
    }

    [Fact]
    public async Task AShutdownThatNeverAnswersIsBoundedByTheShutdownBudget()
    {
        using var temp = new TemporaryDirectory();
        File.WriteAllText(temp.LockPath, OwnerJson);
        using var http = new HttpClient(new AsyncHandler(async (request, ct) =>
        {
            if (request.RequestUri?.AbsolutePath == "/v1/status")
                return Json("{\"binaryPath\":\"old-runtime\"}");
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Json("{}");
        }));
        var client = new HubClient(new DotCraftHubClientOptions
        {
            HubLockPath = temp.LockPath,
            ExpectedExecutable = "new-runtime",
            BinaryMatchPolicy = HubBinaryMatchPolicy.RestartIfMismatch,
            ShutdownTimeout = TimeSpan.FromMilliseconds(100),
            HttpClientFactory = () => http
        });
        var error = await Assert.ThrowsAsync<HubClientException>(
            () => client.EnsureHubAsync().WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal("hubMismatchShutdownTimeout", error.Code);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EquivalentRuntimeSelectionsShareTheSameInitialization(bool usePaths)
    {
        using var temp = new TemporaryDirectory();
        File.WriteAllText(temp.LockPath, OwnerJson);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probes = 0;
        using var http = new HttpClient(new AsyncHandler(async (_, ct) =>
        {
            Interlocked.Increment(ref probes);
            entered.TrySetResult();
            await ready.Task.WaitAsync(ct);
            return Json("{}");
        }));
        var absolute = Path.Combine(temp.Path, "runtime.dll");
        var first = new HubClient(new DotCraftHubClientOptions
        {
            HubLockPath = temp.LockPath,
            Executable = usePaths ? Path.GetRelativePath(Environment.CurrentDirectory, absolute) : null,
            ExpectedExecutable = usePaths ? Path.Combine(temp.Path, ".", "runtime.dll") : null,
            HttpClientFactory = () => http
        }).EnsureHubAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var second = new HubClient(new DotCraftHubClientOptions
        {
            HubLockPath = temp.LockPath,
            Executable = usePaths
                ? OperatingSystem.IsWindows() ? absolute.ToUpperInvariant() : absolute
                : OperatingSystem.IsWindows() ? "DOTCRAFT" : "dotcraft",
            ExpectedExecutable = usePaths ? absolute : null,
            HttpClientFactory = () => http
        }).EnsureHubAsync();
        Assert.Equal(1, probes);
        ready.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task StartupDiagnosticsRedactThePublishedTokenWithoutAFieldPrefix()
    {
        using var temp = new TemporaryDirectory();
        using var http = new HttpClient(new AsyncHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));
        var client = new HubClient(new DotCraftHubClientOptions
        {
            HubLockPath = temp.LockPath,
            StartupTimeout = TimeSpan.FromMilliseconds(100),
            HttpClientFactory = () => http,
            StartHubProcess = _ =>
            {
                File.WriteAllText(temp.LockPath, OwnerJson);
                File.WriteAllText(Path.Combine(temp.Path, "hub-startup.stderr.log"), "Failed credential fixture-token during startup");
                return null;
            }
        });
        var error = await Assert.ThrowsAsync<HubClientException>(
            () => client.EnsureHubAsync().WaitAsync(TimeSpan.FromSeconds(3)));
        var stderr = error.Details?.GetProperty("recentStderr").GetString() ?? "";
        Assert.Contains("Failed credential", stderr);
        Assert.DoesNotContain("fixture-token", error.Details?.GetRawText() ?? "");
    }

    private static string OwnerJson => JsonSerializer.Serialize(new
    {
        pid = Environment.ProcessId,
        apiBaseUrl = "http://127.0.0.1:49134",
        token = "fixture-token"
    });

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body)
    };

    private sealed class AsyncHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handle(request, cancellationToken);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dotcraft-bootstrap-" + Guid.NewGuid().ToString("N"));
        public string LockPath => System.IO.Path.Combine(Path, "hub.lock");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
