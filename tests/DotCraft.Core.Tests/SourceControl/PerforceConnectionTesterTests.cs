using DotCraft.SourceControl;

namespace DotCraft.Tests.SourceControl;

/// <summary>
/// Unit tests for <see cref="PerforceConnectionTester"/> using a fake command runner with
/// canned p4 output. Covers the main result categories and the password-handling contract.
/// </summary>
public sealed class PerforceConnectionTesterTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "p4ws_fixture");

    private static PerforceCommandResult Ok(string stdout = "") => new(0, stdout, string.Empty, false, false);
    private static PerforceCommandResult Fail(int code = 1, string stderr = "") => new(code, string.Empty, stderr, false, false);
    private static readonly PerforceCommandResult Missing = new(-1, string.Empty, string.Empty, true, false);
    private static readonly PerforceCommandResult TimedOut = new(-1, string.Empty, string.Empty, false, true);

    private static PerforceCommandResult Info(string root) =>
        Ok($"User name: alice\nClient name: ws\nClient root: {root}\nServer address: ssl:perforce.example.com:1666\n");

    [Fact]
    public async Task Missing_p4_executable_maps_to_P4ExecutableNotFound()
    {
        var runner = new FakeRunner((args, _) => args.Contains("-V") ? Missing : Ok());

        var report = await PerforceConnectionTester.TestAsync(runner, ManualRequest(), default);

        Assert.Equal(PerforceErrorCodes.P4ExecutableNotFound, report.Code);
        Assert.Equal(SourceControlStatuses.Error, report.Status);
    }

    [Fact]
    public async Task Server_unreachable_maps_to_ServerUnavailable()
    {
        var runner = new FakeRunner((args, _) =>
        {
            if (args.Contains("-V")) return Ok("P4/TEST");
            if (args.Contains("info")) return Fail(1, "Connect to server failed; check $P4PORT.");
            return Ok();
        });

        var report = await PerforceConnectionTester.TestAsync(runner, ManualRequest(), default);

        Assert.Equal(PerforceErrorCodes.ServerUnavailable, report.Code);
    }

    [Fact]
    public async Task Info_timeout_maps_to_Timeout()
    {
        var runner = new FakeRunner((args, _) =>
        {
            if (args.Contains("-V")) return Ok("P4/TEST");
            if (args.Contains("info")) return TimedOut;
            return Ok();
        });

        var report = await PerforceConnectionTester.TestAsync(runner, ManualRequest(), default);

        Assert.Equal(PerforceErrorCodes.Timeout, report.Code);
    }

    [Fact]
    public async Task No_ticket_without_password_maps_to_LoginRequired()
    {
        var runner = new FakeRunner((args, _) =>
        {
            if (args.Contains("-V")) return Ok("P4/TEST");
            if (args.Contains("info")) return Info(Root);
            if (IsLoginStatus(args)) return Fail(1, "Perforce password (P4PASSWD) invalid or unset.");
            return Ok();
        });

        var report = await PerforceConnectionTester.TestAsync(runner, ManualRequest(), default);

        Assert.Equal(PerforceErrorCodes.LoginRequired, report.Code);
        Assert.Equal(SourceControlStatuses.LoginRequired, report.Status);
        Assert.True(report.Authentication.LoginRequired);
    }

    [Fact]
    public async Task Healthy_connection_with_mapped_workspace_maps_to_Connected()
    {
        var runner = new FakeRunner((args, _) =>
        {
            if (args.Contains("-V")) return Ok("P4/TEST/2024.1");
            if (args.Contains("info")) return Info(Root);
            if (IsLoginStatus(args)) return Ok("User alice ticket expires in 10 hours.");
            if (IsClientSpec(args)) return Ok($"Client: ws\nRoot:\t{Root}\n");
            return Ok();
        });

        var report = await PerforceConnectionTester.TestAsync(runner, ManualRequest(workspacePath: Root), default);

        Assert.Equal(PerforceErrorCodes.Connected, report.Code);
        Assert.Equal(SourceControlStatuses.Connected, report.Status);
        Assert.True(report.Workspace.MappingOk);
        Assert.Equal("alice", report.Identity.User);
    }

    [Fact]
    public async Task Workspace_outside_client_root_maps_to_WorkspaceOutsideClientRoot()
    {
        var otherRoot = Path.Combine(Path.GetTempPath(), "p4ws_other");
        var runner = new FakeRunner((args, _) =>
        {
            if (args.Contains("-V")) return Ok("P4/TEST");
            if (args.Contains("info")) return Info(otherRoot);
            if (IsLoginStatus(args)) return Ok("ticket valid");
            if (IsClientSpec(args)) return Ok($"Root:\t{otherRoot}\n");
            return Ok();
        });

        var report = await PerforceConnectionTester.TestAsync(runner, ManualRequest(workspacePath: Root), default);

        Assert.Equal(PerforceErrorCodes.WorkspaceOutsideClientRoot, report.Code);
        Assert.False(report.Workspace.MappingOk);
    }

    [Fact]
    public async Task Transient_password_is_sent_via_stdin_never_in_args()
    {
        var loginStatusCalls = 0;
        var runner = new FakeRunner((args, _) =>
        {
            if (args.Contains("-V")) return Ok("P4/TEST");
            if (args.Contains("info")) return Info(Root);
            if (IsLoginStatus(args))
            {
                loginStatusCalls++;
                // First status check fails (no ticket); after a login attempt it succeeds.
                return loginStatusCalls == 1 ? Fail(1, "no ticket") : Ok("ticket valid");
            }
            if (IsLogin(args)) return Ok();
            if (IsClientSpec(args)) return Ok($"Root:\t{Root}\n");
            return Ok();
        });

        var report = await PerforceConnectionTester.TestAsync(
            runner,
            ManualRequest(workspacePath: Root) with { Password = "super-secret" },
            default);

        Assert.Equal(PerforceErrorCodes.Connected, report.Code);

        // The password must never appear in any command-line argument...
        Assert.DoesNotContain(runner.Calls, c => c.Args.Any(a => a.Contains("super-secret")));
        // ...and must be delivered to the `p4 login` attempt via stdin.
        Assert.Contains(runner.Calls, c => IsLogin(c.Args) && c.Stdin == "super-secret");
    }

    private static PerforceTestRequest ManualRequest(string? workspacePath = null) => new()
    {
        WorkspacePath = workspacePath ?? Root,
        ConnectionMode = SourceControlConnectionModes.Manual,
        Port = "ssl:perforce.example.com:1666",
        Client = "ws",
        User = "alice",
        TimeoutSeconds = 30
    };

    private static bool IsLoginStatus(IReadOnlyList<string> args) => args.Contains("login") && args.Contains("-s");
    private static bool IsLogin(IReadOnlyList<string> args) => args.Contains("login") && !args.Contains("-s");
    private static bool IsClientSpec(IReadOnlyList<string> args) => args.Contains("client") && args.Contains("-o");

    private sealed class FakeRunner(Func<IReadOnlyList<string>, string?, PerforceCommandResult> handler) : IPerforceCommandRunner
    {
        public List<(IReadOnlyList<string> Args, string? Stdin)> Calls { get; } = [];

        public Task<PerforceCommandResult> RunAsync(IReadOnlyList<string> args, string? stdinInput, CancellationToken ct)
        {
            Calls.Add((args.ToList(), stdinInput));
            return Task.FromResult(handler(args, stdinInput));
        }
    }
}
