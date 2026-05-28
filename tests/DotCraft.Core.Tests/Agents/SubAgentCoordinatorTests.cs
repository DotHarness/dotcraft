using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Security;
using System.Text;

namespace DotCraft.Tests.Agents;

public sealed class SubAgentCoordinatorTests : IDisposable
{
    private readonly string _workspacePath = Path.Combine(Path.GetTempPath(), $"subagent_coordinator_{Guid.NewGuid():N}");

    public SubAgentCoordinatorTests()
    {
        Directory.CreateDirectory(_workspacePath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_workspacePath))
                Directory.Delete(_workspacePath, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }

        SubAgentProgressBridge.Remove("Inspect");
        SubAgentProgressBridge.Remove("cli run");
        SubAgentProgressBridge.Remove("diag");
        SubAgentProgressBridge.Remove("native run");
        SubAgentProgressBridge.Remove("external tokens");
        SubAgentProgressBridge.Remove("disabled run");
    }

    [Fact]
    public async Task RunAsync_AppendsPermissionModeArgs_FromApprovalMode()
    {
        var runtime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "mapped ok");
        var coordinator = new SubAgentCoordinator(
            _workspacePath,
            [runtime],
            [
                new SubAgentProfile
                {
                    Name = "mapped-cli",
                    Runtime = CliOneshotRuntime.RuntimeTypeName,
                    Bin = "codex",
                    WorkingDirectoryMode = "workspace",
                    InputMode = "arg",
                    OutputFormat = "text",
                    PermissionModeMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [SubAgentApprovalModeResolver.InteractiveMode] = "--mode ask",
                        [SubAgentApprovalModeResolver.AutoApproveMode] = "--mode auto --trust"
                    }
                }
            ],
            approvalService: new AutoApproveApprovalService());

        var result = await coordinator.RunAsync(
            new SubAgentTaskRequest { Task = "inspect code" },
            "mapped-cli");

        Assert.Equal("mapped ok", result);
        Assert.Equal(["--mode", "auto", "--trust"], runtime.LastLaunchContext?.ExtraLaunchArgs);
    }

    [Fact]
    public async Task RunAsync_PropagatesApprovalContext_FromScope()
    {
        var runtime = new FakeRuntime(NativeSubAgentRuntime.RuntimeTypeName, "native ok");
        var cliRuntime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "cli ok");
        var coordinator = new SubAgentCoordinator(
            _workspacePath,
            [runtime, cliRuntime],
            approvalService: new ConsoleApprovalService());
        using var _ = ApprovalContextScope.Set(new ApprovalContext
        {
            Source = "qq",
            UserId = "alice"
        });

        var result = await coordinator.RunAsync(new SubAgentTaskRequest { Task = "inspect code" });

        Assert.Equal("native ok", result);
        Assert.Equal("qq", runtime.LastLaunchContext?.ApprovalContext?.Source);
        Assert.Equal("alice", runtime.LastRequest?.ApprovalContext?.UserId);
    }

    [Fact]
    public async Task RunAsync_WithoutProfile_UsesBuiltInDotcraftNativeProfile()
    {
        var runtime = new FakeRuntime(NativeSubAgentRuntime.RuntimeTypeName, "native ok");
        var cliRuntime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "cli ok");
        var coordinator = new SubAgentCoordinator(_workspacePath, [runtime, cliRuntime]);

        var result = await coordinator.RunAsync(new SubAgentTaskRequest
        {
            Task = "inspect code",
            Label = "Inspect"
        });

        Assert.Equal("native ok", result);
        Assert.Equal(1, runtime.CreateCalls);
        Assert.Equal(1, runtime.RunCalls);
        Assert.Equal(1, runtime.DisposeCalls);
        Assert.Equal(_workspacePath, runtime.LastLaunchContext?.WorkingDirectory);
        Assert.Equal(SubAgentCoordinator.DefaultProfileName, runtime.LastLaunchContext?.ProfileName);
        Assert.Equal("inspect code", runtime.LastRequest?.Task);
        Assert.Equal("Inspect", runtime.LastRequest?.Label);
        Assert.Equal(_workspacePath, runtime.LastRequest?.WorkingDirectory);
    }

    [Fact]
    public async Task RunAsync_WithExplicitNativeProfile_UsesNativeRuntime()
    {
        var runtime = new FakeRuntime(NativeSubAgentRuntime.RuntimeTypeName, "explicit ok");
        var cliRuntime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "cli ok");
        var coordinator = new SubAgentCoordinator(_workspacePath, [runtime, cliRuntime]);

        var result = await coordinator.RunAsync(
            new SubAgentTaskRequest { Task = "inspect code" },
            SubAgentCoordinator.DefaultProfileName);

        Assert.Equal("explicit ok", result);
        Assert.Equal(1, runtime.RunCalls);
    }

    [Fact]
    public async Task RunAsync_WithUnknownProfile_ReturnsClearError()
    {
        var runtime = new FakeRuntime(NativeSubAgentRuntime.RuntimeTypeName, "unused");
        var cliRuntime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "cli ok");
        var coordinator = new SubAgentCoordinator(_workspacePath, [runtime, cliRuntime]);

        var result = await coordinator.RunAsync(
            new SubAgentTaskRequest { Task = "inspect code" },
            "missing-profile");

        Assert.Equal("Error: Unknown subagent profile 'missing-profile'.", result);
        Assert.Equal(0, runtime.RunCalls);
    }

    [Fact]
    public async Task RunAsync_WithUnknownRuntimeProfile_EmitsWarning_AndReturnsError()
    {
        var runtime = new FakeRuntime(NativeSubAgentRuntime.RuntimeTypeName, "unused");
        var cliRuntime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "cli ok");
        var coordinator = new SubAgentCoordinator(
            _workspacePath,
            [runtime, cliRuntime],
            [
                new SubAgentProfile
                {
                    Name = "broken-profile",
                    Runtime = "unknown-runtime",
                    WorkingDirectoryMode = "workspace"
                }
            ]);

        Assert.Contains(
            coordinator.ValidationWarnings,
            w => w.Contains("broken-profile", StringComparison.Ordinal)
                 && w.Contains("unknown-runtime", StringComparison.Ordinal));

        var result = await coordinator.RunAsync(
            new SubAgentTaskRequest { Task = "inspect code" },
            "broken-profile");

        Assert.Equal(
            "Error: Subagent profile 'broken-profile' references unknown runtime 'unknown-runtime'.",
            result);
        Assert.Equal(0, runtime.RunCalls);
    }

    [Fact]
    public async Task RunAsync_WithDisabledProfile_ReturnsClearError()
    {
        var runtime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "unused");
        var coordinator = new SubAgentCoordinator(
            _workspacePath,
            [runtime],
            [
                new SubAgentProfile
                {
                    Name = "disabled-run",
                    Runtime = CliOneshotRuntime.RuntimeTypeName,
                    Bin = "codex",
                    WorkingDirectoryMode = "workspace",
                    InputMode = "arg",
                    OutputFormat = "text"
                }
            ],
            disabledProfiles: ["disabled-run"]);

        var result = await coordinator.RunAsync(
            new SubAgentTaskRequest
            {
                Task = "inspect code",
                Label = "disabled run"
            },
            "disabled-run");

        Assert.Equal("Error: Subagent profile 'disabled-run' is disabled.", result);
        Assert.Equal(0, runtime.RunCalls);
    }

    [Fact]
    public async Task RunAsync_ConfiguredProfile_OverridesBuiltInDefaultByName()
    {
        var runtime = new FakeRuntime(NativeSubAgentRuntime.RuntimeTypeName, "unused");
        var cliRuntime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "cli ok");
        var coordinator = new SubAgentCoordinator(
            _workspacePath,
            [runtime, cliRuntime],
            [
                new SubAgentProfile
                {
                    Name = "NATIVE",
                    Runtime = NativeSubAgentRuntime.RuntimeTypeName,
                    WorkingDirectoryMode = "specified"
                }
            ]);

        var result = await coordinator.RunAsync(new SubAgentTaskRequest
        {
            Task = "inspect code"
        });

        Assert.Equal(
            "Error: Subagent profile 'NATIVE' requires a specified working directory.",
            result);
        Assert.Equal(0, runtime.RunCalls);
    }

    [Fact]
    public async Task RunAsync_SpecifiedWorkingDirectory_PassesResolvedDirectoryToRuntime()
    {
        var runtime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "cli ok");
        var coordinator = new SubAgentCoordinator(
            _workspacePath,
            [runtime],
            [
                new SubAgentProfile
                {
                    Name = "cli-specified",
                    Runtime = CliOneshotRuntime.RuntimeTypeName,
                    WorkingDirectoryMode = "specified",
                    Bin = "test-cli"
                }
            ]);

        var specifiedDirectory = Path.Combine(_workspacePath, "nested");
        Directory.CreateDirectory(specifiedDirectory);

        var result = await coordinator.RunAsync(
            new SubAgentTaskRequest
            {
                Task = "inspect code",
                WorkingDirectory = specifiedDirectory
            },
            "cli-specified");

        Assert.Equal("cli ok", result);
        Assert.Equal(specifiedDirectory, runtime.LastLaunchContext?.WorkingDirectory);
        Assert.Equal(specifiedDirectory, runtime.LastRequest?.WorkingDirectory);
    }

    [Fact]
    public async Task RunAsync_SpecifiedWorkingDirectory_WhenDirectoryMissing_ReturnsClearError()
    {
        var runtime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "cli ok");
        var coordinator = new SubAgentCoordinator(
            _workspacePath,
            [runtime],
            [
                new SubAgentProfile
                {
                    Name = "cli-specified",
                    Runtime = CliOneshotRuntime.RuntimeTypeName,
                    WorkingDirectoryMode = "specified",
                    Bin = "test-cli"
                }
            ]);

        var missingDirectory = Path.Combine(_workspacePath, "missing");
        var result = await coordinator.RunAsync(
            new SubAgentTaskRequest
            {
                Task = "inspect code",
                WorkingDirectory = missingDirectory
            },
            "cli-specified");

        Assert.Equal(
            $"Error: Subagent working directory '{missingDirectory}' does not exist.",
            result);
        Assert.Equal(0, runtime.RunCalls);
    }

    [Fact]
    public async Task RunAsync_NativeRuntime_UsesNullSinkAndDoesNotOverwriteBridgeState()
    {
        var runtime = new FakeRuntime(
            NativeSubAgentRuntime.RuntimeTypeName,
            "native ok",
            onRun: sink => sink.OnCompleted("Should not be written.", new SubAgentTokenUsage(99, 42)));
        var cliRuntime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "cli ok");
        var coordinator = new SubAgentCoordinator(_workspacePath, [runtime, cliRuntime]);

        var result = await coordinator.RunAsync(new SubAgentTaskRequest
        {
            Task = "inspect code",
            Label = "native run"
        });

        Assert.Equal("native ok", result);
        Assert.Null(SubAgentProgressBridge.TryGet("native run"));
    }

    [Fact]
    public async Task RunAsync_ExternalRuntimeSink_UpdatesBridgeProgressAndTokens()
    {
        var runtime = new FakeRuntime(
            CliOneshotRuntime.RuntimeTypeName,
            "cli ok",
            onRun: sink =>
            {
                sink.OnProgress("external-cli", "Launching cursor-agent.");
                sink.OnCompleted("Completed cursor-cli.", new SubAgentTokenUsage(12, 34));
            });
        var coordinator = new SubAgentCoordinator(
            _workspacePath,
            [runtime],
            [
                new SubAgentProfile
                {
                    Name = "cli-run",
                    Runtime = CliOneshotRuntime.RuntimeTypeName,
                    WorkingDirectoryMode = "workspace",
                    Bin = "test-cli"
                }
            ]);

        var result = await coordinator.RunAsync(
            new SubAgentTaskRequest
            {
                Task = "inspect code",
                Label = "cli run"
            },
            "cli-run");

        Assert.Equal("cli ok", result);
        var progress = SubAgentProgressBridge.TryGet("cli run");
        Assert.NotNull(progress);
        Assert.True(progress!.IsCompleted);
        Assert.Equal(12, progress.InputTokens);
        Assert.Equal(34, progress.OutputTokens);
        Assert.Equal("Completed cursor-cli.", progress.LastToolDisplay);
    }

    [Fact]
    public async Task RunAsync_ExternalRuntimeSink_AlsoAggregatesTokensToTokenTracker()
    {
        var runtime = new FakeRuntime(
            CliOneshotRuntime.RuntimeTypeName,
            "cli ok",
            onRun: sink => sink.OnCompleted("Completed cursor-cli.", new SubAgentTokenUsage(7, 11)));
        var coordinator = new SubAgentCoordinator(
            _workspacePath,
            [runtime],
            [
                new SubAgentProfile
                {
                    Name = "external-tokens",
                    Runtime = CliOneshotRuntime.RuntimeTypeName,
                    WorkingDirectoryMode = "workspace",
                    Bin = "test-cli"
                }
            ]);

        var previousTracker = DotCraft.Context.TokenTracker.Current;
        DotCraft.Context.TokenTracker.Current = new DotCraft.Context.TokenTracker();
        try
        {
            var result = await coordinator.RunAsync(
                new SubAgentTaskRequest
                {
                    Task = "inspect code",
                    Label = "external tokens"
                },
                "external-tokens");

            Assert.Equal("cli ok", result);
            Assert.Equal(7, DotCraft.Context.TokenTracker.Current?.SubAgentInputTokens);
            Assert.Equal(11, DotCraft.Context.TokenTracker.Current?.SubAgentOutputTokens);
        }
        finally
        {
            DotCraft.Context.TokenTracker.Current = previousTracker;
        }
    }

    [Fact]
    public async Task RunAsync_WhenExternalCliResumeDisabled_DoesNotPassStoredSessionId()
    {
        var runtime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "cli ok");
        var store = new FakeExternalCliSessionStore(
            new ExternalCliStoredSession("cli-run", "same", _workspacePath, "sess-123"));
        var coordinator = new SubAgentCoordinator(
            _workspacePath,
            [runtime],
            [
                new SubAgentProfile
                {
                    Name = "cli-run",
                    Runtime = CliOneshotRuntime.RuntimeTypeName,
                    WorkingDirectoryMode = "workspace",
                    Bin = "test-cli",
                    SupportsResume = true,
                    ResumeArgTemplate = "--resume {sessionId}",
                    ResumeSessionIdJsonPath = "session_id"
                }
            ],
            externalCliSessionStore: store,
            enableExternalCliSessionResume: false);

        var result = await coordinator.RunAsync(
            new SubAgentTaskRequest { Task = "inspect code", Label = "same" },
            "cli-run");

        Assert.Equal("cli ok", result);
        Assert.Null(runtime.LastLaunchContext?.ResumeSessionId);
        Assert.Empty(store.RecordedSessionIds);
    }

    [Fact]
    public async Task RunAsync_WhenExternalCliResumeEnabled_PassesAndUpdatesSessionId()
    {
        var runtime = new FakeRuntime(
            CliOneshotRuntime.RuntimeTypeName,
            "cli ok",
            resultSessionId: "sess-456");
        var store = new FakeExternalCliSessionStore(
            new ExternalCliStoredSession("cli-run", "same", _workspacePath, "sess-123"));
        var coordinator = new SubAgentCoordinator(
            _workspacePath,
            [runtime],
            [
                new SubAgentProfile
                {
                    Name = "cli-run",
                    Runtime = CliOneshotRuntime.RuntimeTypeName,
                    WorkingDirectoryMode = "workspace",
                    Bin = "test-cli",
                    SupportsResume = true,
                    ResumeArgTemplate = "--resume {sessionId}",
                    ResumeSessionIdJsonPath = "session_id"
                }
            ],
            externalCliSessionStore: store,
            enableExternalCliSessionResume: true);

        var result = await coordinator.RunAsync(
            new SubAgentTaskRequest { Task = "inspect code", Label = "same" },
            "cli-run");

        Assert.Equal("cli ok", result);
        Assert.Equal("sess-123", runtime.LastLaunchContext?.ResumeSessionId);
        Assert.Equal(["sess-456"], store.RecordedSessionIds);
    }

    [Fact]
    public void GetProfileDiagnostics_IncludesWarningsAndBuiltInMetadata()
    {
        var runtime = new FakeRuntime(NativeSubAgentRuntime.RuntimeTypeName, "unused");
        var cliRuntime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "unused");
        var coordinator = new SubAgentCoordinator(
            _workspacePath,
            [runtime, cliRuntime],
            [
                new SubAgentProfile
                {
                    Name = "diag",
                    Runtime = CliOneshotRuntime.RuntimeTypeName,
                    WorkingDirectoryMode = "workspace",
                    OutputFormat = "json"
                }
            ]);

        var diagnostics = coordinator.GetProfileDiagnostics();

        var builtIn = Assert.Single(diagnostics, d => d.Name == SubAgentCoordinator.DefaultProfileName);
        Assert.True(builtIn.IsBuiltIn);
        Assert.True(builtIn.Enabled);
        Assert.True(builtIn.RuntimeRegistered);

        var configured = Assert.Single(diagnostics, d => d.Name == "diag");
        Assert.False(configured.IsBuiltIn);
        Assert.True(configured.Enabled);
        Assert.Contains(configured.Warnings, w => w.Contains("missing required field 'bin'", StringComparison.Ordinal));
        Assert.Contains(configured.Warnings, w => w.Contains("outputJsonPath", StringComparison.Ordinal));
        Assert.True(configured.HiddenFromPrompt);
        Assert.False(configured.BinaryResolved);
        Assert.Contains("missing required field 'bin'", configured.HiddenReason, StringComparison.Ordinal);
    }

    [Fact]
    public void GetProfileDiagnostics_MarksCliProfileHiddenWhenBinaryCannotResolve()
    {
        var runtime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "unused");
        var profileName = $"missing-cli-{Guid.NewGuid():N}";
        var coordinator = new SubAgentCoordinator(
            _workspacePath,
            [runtime],
            [
                new SubAgentProfile
                {
                    Name = profileName,
                    Runtime = CliOneshotRuntime.RuntimeTypeName,
                    WorkingDirectoryMode = "workspace",
                    InputMode = "arg",
                    OutputFormat = "text",
                    Bin = $"definitely-missing-{Guid.NewGuid():N}"
                }
            ]);

        var diagnostics = coordinator.GetProfileDiagnostics();

        var configured = Assert.Single(diagnostics, d => d.Name == profileName);
        Assert.True(configured.Enabled);
        Assert.True(configured.HiddenFromPrompt);
        Assert.Contains("not found on PATH", configured.HiddenReason, StringComparison.Ordinal);
        Assert.False(configured.BinaryResolved);
        Assert.Null(configured.ResolvedBinary);
    }

    [Fact]
    public void GetProfileDiagnostics_ProtectedDefaultProfileStaysEnabled_WhenListedAsDisabled()
    {
        var runtime = new FakeRuntime(NativeSubAgentRuntime.RuntimeTypeName, "unused");
        var coordinator = new SubAgentCoordinator(
            _workspacePath,
            [runtime],
            disabledProfiles: [SubAgentCoordinator.DefaultProfileName]);

        var diagnostics = coordinator.GetProfileDiagnostics();

        var builtIn = Assert.Single(diagnostics, d => d.Name == SubAgentCoordinator.DefaultProfileName);
        Assert.True(builtIn.Enabled);
        Assert.False(builtIn.HiddenFromPrompt);
    }

    [Fact]
    public void Constructor_DoesNotWriteValidationWarningsToConsoleError()
    {
        var runtime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "unused");
        var originalError = Console.Error;
        var writer = new StringWriter(new StringBuilder());
        Console.SetError(writer);
        try
        {
            _ = new SubAgentCoordinator(
                _workspacePath,
                [runtime],
                [
                    new SubAgentProfile
                    {
                        Name = "broken-cli",
                        Runtime = CliOneshotRuntime.RuntimeTypeName,
                        WorkingDirectoryMode = "workspace",
                        OutputFormat = "json"
                    }
                ]);
        }
        finally
        {
            Console.SetError(originalError);
        }

        Assert.Equal(string.Empty, writer.ToString());
    }

    private sealed class FakeRuntime(
        string runtimeType,
        string resultText,
        Action<ISubAgentEventSink>? onRun = null,
        string? resultSessionId = null) : ISubAgentRuntime
    {
        public string RuntimeType { get; } = runtimeType;

        public int CreateCalls { get; private set; }

        public int RunCalls { get; private set; }

        public int DisposeCalls { get; private set; }

        public SubAgentLaunchContext? LastLaunchContext { get; private set; }

        public SubAgentTaskRequest? LastRequest { get; private set; }

        public Task<SubAgentSessionHandle> CreateSessionAsync(
            SubAgentProfile profile,
            SubAgentLaunchContext context,
            CancellationToken cancellationToken)
        {
            CreateCalls++;
            LastLaunchContext = context;
            return Task.FromResult(new SubAgentSessionHandle(RuntimeType, profile.Name));
        }

        public Task<SubAgentRunResult> RunAsync(
            SubAgentSessionHandle session,
            SubAgentTaskRequest request,
            ISubAgentEventSink sink,
            CancellationToken cancellationToken)
        {
            RunCalls++;
            LastRequest = request;
            onRun?.Invoke(sink);
            return Task.FromResult(new SubAgentRunResult
            {
                Text = resultText,
                IsError = false,
                SessionId = resultSessionId
            });
        }

        public Task CancelAsync(SubAgentSessionHandle session, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task DisposeSessionAsync(SubAgentSessionHandle session, CancellationToken cancellationToken)
        {
            DisposeCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeExternalCliSessionStore(params ExternalCliStoredSession[] sessions) : IExternalCliSessionStore
    {
        private readonly List<ExternalCliStoredSession> _sessions = [.. sessions];

        public List<string> RecordedSessionIds { get; } = [];

        public bool TryGetResumeSession(
            string profileName,
            string? label,
            string workingDirectory,
            out ExternalCliStoredSession session)
        {
            session = _sessions.FirstOrDefault(s =>
                string.Equals(s.ProfileName, profileName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(s.Label, label, StringComparison.Ordinal)
                && string.Equals(s.WorkingDirectory, workingDirectory, StringComparison.OrdinalIgnoreCase))
                ?? new ExternalCliStoredSession(string.Empty, null, string.Empty, string.Empty);
            return !string.IsNullOrWhiteSpace(session.SessionId);
        }

        public void RecordSuccessfulRun(
            string profileName,
            string? label,
            string workingDirectory,
            string sessionId)
        {
            RecordedSessionIds.Add(sessionId);
        }
    }
}
