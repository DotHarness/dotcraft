using System.Net;
using System.Security.Authentication;
using System.Text.Json;
using System.Threading.Channels;
using DotCraft.Auth.OpenAI;
using DotCraft.CodexGateway;
using DotCraft.Hub;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DotCraft.Tests.Hub;

public sealed class CodexGatewayTests : IDisposable
{
    private readonly string _profile = Path.Combine(Path.GetTempPath(), "DotCraftCodexHub_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AuthenticationReplacementAndLogout_ReleaseOldOwner_AndEnabledIntentSurvivesRestart()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var paths = HubPaths.Resolve(_profile);
        Directory.CreateDirectory(paths.CraftHomePath);
        var tokens = new OpenAITokenStore(paths.CraftHomePath);
        SignIn(tokens, "account-a", "first");
        var events = new HubEventBus();
        var updates = events.Subscribe(deadline.Token);
        using var logging = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = logging.CreateLogger("CodexGatewayLifecycle");
        var created = Channel.CreateUnbounded<(string? Account, Connection Host)>();
        ICodexGatewayConnection Connect(Action<RemoteControlHostStatus> changed)
        {
            var host = new Connection(changed);
            created.Writer.TryWrite((tokens.Load()?.Tokens?.AccountId, host));
            return host;
        }

        await using (var gateway = new DotCraft.Hub.CodexGateway(paths, new(paths), events, logger, connect: Connect))
        {
            await gateway.StartIfEnabledAsync(deadline.Token);
            Assert.Equal("connecting", (await gateway.EnableAsync(deadline.Token)).State);
            var first = await created.Reader.ReadAsync(deadline.Token);
            first.Host.Connected();
            await StateAsync(updates, status => status.State == "on", deadline.Token);

            SignIn(tokens, "account-a", "refreshed");
            SignIn(tokens, "account-b", "other-account");
            var second = await created.Reader.ReadAsync(deadline.Token);
            Assert.Equal("account-b", second.Account);
            Assert.True(first.Host.Disposed);
            second.Host.Connected();
            await StateAsync(updates, status => status.State == "on", deadline.Token);

            tokens.Delete();
            var signedOut = await StateAsync(updates, status => !status.SignedIn, deadline.Token);
            Assert.Equal("off", signedOut.State);
            Assert.True(second.Host.Disposed);

            SignIn(tokens, "account-b", "signed-in-again");
            var resumed = await created.Reader.ReadAsync(deadline.Token);
            resumed.Host.Connected();
            await StateAsync(updates, status => status.State == "on", deadline.Token);
        }

        await using var restarted = new DotCraft.Hub.CodexGateway(paths, new(paths), events, logger, connect: Connect);
        await restarted.StartIfEnabledAsync(deadline.Token);
        var restored = await created.Reader.ReadAsync(deadline.Token);
        Assert.Equal("account-b", restored.Account);
        restored.Host.Connected();
        await StateAsync(updates, status => status.State == "on", deadline.Token);
        await restarted.DisableAsync(deadline.Token);
        Assert.True(restored.Host.Disposed);
        Assert.False(new CodexGatewayStateStore(paths.CodexRemoteConfigPath).Read());
    }

    [Fact]
    public async Task Pairing_OnlyChecksLatestUnexpiredInvitation_AndDisableCancelsItsCheck()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var paths = HubPaths.Resolve(_profile);
        Directory.CreateDirectory(paths.CraftHomePath);
        SignIn(new(paths.CraftHomePath), "account", "token");
        var events = new HubEventBus();
        var updates = events.Subscribe(deadline.Token);
        Connection? host = null;
        await using var gateway = new DotCraft.Hub.CodexGateway(paths, new(paths), events, NullLogger.Instance,
            connect: changed => host = new(changed));
        await gateway.EnableAsync(deadline.Token);
        host!.Connected();
        await gateway.CreatePairingAsync(deadline.Token);
        var firstCheck = await host.Checks.Reader.ReadAsync(deadline.Token);
        await gateway.CreatePairingAsync(deadline.Token);
        var secondCheck = await host.Checks.Reader.ReadAsync(deadline.Token);
        Assert.True(firstCheck.Cancelled);
        secondCheck.Answer.SetResult(true);
        await EventAsync(updates, "codexRemote.paired", deadline.Token);

        await gateway.CreatePairingAsync(deadline.Token);
        var pending = await host.Checks.Reader.ReadAsync(deadline.Token);
        await gateway.DisableAsync(deadline.Token);
        Assert.True(pending.Cancelled);
        Assert.DoesNotContain(ReadAvailable(updates), update => update.Kind == "codexRemote.paired");

        await gateway.EnableAsync(deadline.Token);
        host!.Connected();
        host.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        await gateway.CreatePairingAsync(deadline.Token);
        Assert.False(host.Checks.Reader.TryRead(out _));
    }

    [Fact]
    public async Task LocalApi_RequiresBearerAndChatGptAuthentication_WithoutStartingWorkspaceServices()
    {
        await using var hub = await MobileHubFixture.StartAsync(_profile, SatelliteHubFixture.GetAvailablePort());
        using var unauthorized = await hub.SendAsync(HttpMethod.Get, "/v1/codex-remote", token: "wrong");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        var status = await hub.JsonAsync(HttpMethod.Get, "/v1/codex-remote");
        Assert.Equal("off", status.GetProperty("state").GetString());
        Assert.False(status.GetProperty("signedIn").GetBoolean());
        foreach (var action in new[] { "enable", "pairings" })
        {
            using var response = await hub.SendAsync(HttpMethod.Post, "/v1/codex-remote/" + action);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("codexRemoteSignInRequired", await response.Content.ReadAsStringAsync());
        }
        Assert.Empty((await hub.JsonAsync(HttpMethod.Get, "/v1/appservers")).EnumerateArray());
    }

    [Theory]
    [InlineData("authentication", "codexRemoteAuthenticationFailed")]
    [InlineData("relay", "codexRemoteRelayFailed")]
    [InlineData("internal", "codexRemoteInternalFailed")]
    public async Task HostFailure_ReportsItsCategoryWithoutChangingSignIn(string category, string code)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var paths = HubPaths.Resolve(_profile);
        Directory.CreateDirectory(paths.CraftHomePath);
        SignIn(new(paths.CraftHomePath), "account", "token");
        var events = new HubEventBus();
        var updates = events.Subscribe(deadline.Token);
        Connection? host = null;
        await using var gateway = new DotCraft.Hub.CodexGateway(paths, new(paths), events, NullLogger.Instance,
            connect: changed => host = new(changed));
        await gateway.EnableAsync(deadline.Token);
        host!.Connected();
        Exception failure = category switch
        {
            "authentication" => new AuthenticationException("secret-token"),
            "relay" => new HttpRequestException("secret-token", null, HttpStatusCode.Forbidden),
            _ => new InvalidOperationException("secret-body")
        };
        host.Fail(failure);

        var status = await StateAsync(updates, value => value.FailureCode == code, deadline.Token);

        Assert.True(status.SignedIn);
        Assert.Equal("failed", status.State);
        Assert.DoesNotContain("secret", status.FailureMessage!);
        if (category != "authentication")
            Assert.DoesNotContain("sign in", status.FailureMessage!, StringComparison.OrdinalIgnoreCase);
    }

    private static void SignIn(OpenAITokenStore store, string account, string token)
    {
        var payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["https://api.openai.com/auth"] = new { chatgpt_account_id = account }
        })).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        store.Save(new AuthDotJson
        {
            Tokens = new()
            {
                AccountId = account, AccessToken = token, RefreshToken = "refresh",
                IdToken = $"eyJhbGciOiJub25lIn0.{payload}.unsigned"
            },
            LastRefresh = DateTimeOffset.UtcNow
        });
    }

    private static async Task<HubCodexRemoteStatus> StateAsync(
        ChannelReader<HubEvent> updates, Func<HubCodexRemoteStatus, bool> predicate, CancellationToken ct)
    {
        while (true)
        {
            var update = await updates.ReadAsync(ct);
            if (update.Data is HubCodexRemoteStatus status && predicate(status)) return status;
        }
    }

    private static async Task EventAsync(ChannelReader<HubEvent> updates, string kind, CancellationToken ct)
    {
        while ((await updates.ReadAsync(ct)).Kind != kind) { }
    }

    private static IEnumerable<HubEvent> ReadAvailable(ChannelReader<HubEvent> updates)
    {
        while (updates.TryRead(out var update)) yield return update;
    }

    public void Dispose()
    {
        if (Directory.Exists(_profile)) Directory.Delete(_profile, recursive: true);
    }

    private sealed class Connection(Action<RemoteControlHostStatus> changed) : ICodexGatewayConnection
    {
        public readonly Channel<Check> Checks = Channel.CreateUnbounded<Check>();
        public bool Disposed { get; private set; }
        public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddMinutes(5);

        private readonly TaskCompletionSource _run = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task RunAsync(CancellationToken ct) => _run.Task.WaitAsync(ct);
        public void Fail(Exception error) => _run.TrySetException(error);
        public void Connected() => changed(new("installation", "host", "environment", "connected"));
        public Task<RemoteControlPairing> PairAsync(CancellationToken ct) => Task.FromResult(
            new RemoteControlPairing(Guid.NewGuid().ToString("N"), "manual", "environment", ExpiresAt));
        public async Task<bool> IsPairingClaimedAsync(RemoteControlPairing pairing, CancellationToken ct)
        {
            var check = new Check();
            Checks.Writer.TryWrite(check);
            try { return await check.Answer.Task.WaitAsync(ct); }
            finally { check.Cancelled = ct.IsCancellationRequested; }
        }
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Check
    {
        public TaskCompletionSource<bool> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Cancelled { get; set; }
    }
}
