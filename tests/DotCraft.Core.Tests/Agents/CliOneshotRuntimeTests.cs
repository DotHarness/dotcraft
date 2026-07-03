using System.Text;
using DotCraft.Agents;
using DotCraft.Configuration;

namespace DotCraft.Tests.Agents;

[CollectionDefinition(CliOneshotRuntimeEnvironmentCollection.Name, DisableParallelization = true)]
public sealed class CliOneshotRuntimeEnvironmentCollection
{
    public const string Name = "CliOneshotRuntimeEnvironment";
}

[Collection(CliOneshotRuntimeEnvironmentCollection.Name)]
public sealed class CliOneshotRuntimeTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), $"cli_oneshot_runtime_{Guid.NewGuid():N}");
    private readonly string _workspacePath;

    public CliOneshotRuntimeTests()
    {
        _workspacePath = Path.Combine(_rootPath, "workspace");
        Directory.CreateDirectory(_workspacePath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_rootPath))
                Directory.Delete(_rootPath, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public async Task RunAsync_ArgMode_PassesTaskAndWorkingDirectory()
    {
        var profile = CreateProfile(CreateArgEchoScript());
        profile.InputMode = "arg";
        profile.WorkingDirectoryMode = "specified";

        var result = await RunProfileAsync(
            profile,
            "hello-cli",
            workingDirectory: _workspacePath);

        Assert.False(result.IsError);
        Assert.Contains($"cwd={_workspacePath}", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("arg=hello-cli", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_StdinMode_WritesTaskToStandardInput()
    {
        var profile = CreateProfile(CreateStdinEchoScript());
        profile.InputMode = "stdin";

        var result = await RunProfileAsync(profile, "stdin payload");

        Assert.False(result.IsError);
        Assert.Contains("stdin=stdin payload", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ArgMode_ClosesStdinToAvoidInteractiveHang()
    {
        var profile = CreateProfile(CreateStdinEofScript());
        profile.InputMode = "arg";

        var result = await RunProfileAsync(profile, "ignored");

        Assert.False(result.IsError);
        Assert.Contains("stdin=eof", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_EnvMode_WritesTaskToConfiguredEnvironmentVariable()
    {
        var profile = CreateProfile(CreateEnvEchoScript());
        profile.InputMode = "env";
        profile.InputEnvKey = "DOTCRAFT_TEST_TASK";

        var result = await RunProfileAsync(profile, "env payload");

        Assert.False(result.IsError);
        Assert.Contains("env=env payload", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_EnvPassthrough_CopiesParentEnvironmentWhenConfigured()
    {
        const string key = "DOTCRAFT_TEST_PASSTHROUGH";
        var previous = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, "passthrough-value");
        try
        {
            var profile = CreateProfile(CreatePassthroughEchoScript());
            profile.InputMode = "arg";
            profile.EnvPassthrough = [key];

            var result = await RunProfileAsync(profile, "ignored");

            Assert.False(result.IsError);
            Assert.Contains("passthrough=passthrough-value", result.Text, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, previous);
        }
    }

    [Fact]
    public async Task RunAsync_DefaultEnvironment_CopiesCodexApiKey()
    {
        const string key = "CODEX_API_KEY";
        var previous = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, "codex-test-key");
        try
        {
            var profile = CreateProfile(CreateCodexApiKeyEchoScript());
            profile.InputMode = "arg";

            var result = await RunProfileAsync(profile, "ignored");

            Assert.False(result.IsError);
            Assert.Contains("codex_api_key=codex-test-key", result.Text, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, previous);
        }
    }

    [Fact]
    public async Task RunAsync_JsonOutput_ExtractsConfiguredField()
    {
        var profile = CreateProfile(CreateJsonOutputScript());
        profile.InputMode = "arg";
        profile.OutputFormat = "json";
        profile.OutputJsonPath = "result";

        var result = await RunProfileAsync(profile, "ignored");

        Assert.False(result.IsError);
        Assert.Equal("json-ok", result.Text);
    }

    [Fact]
    public async Task RunAsync_JsonOutput_ExtractsConfiguredTokenUsage()
    {
        var profile = CreateProfile(CreateJsonOutputScript());
        profile.InputMode = "arg";
        profile.OutputFormat = "json";
        profile.OutputJsonPath = "result";
        profile.OutputInputTokensJsonPath = "usage.input";
        profile.OutputOutputTokensJsonPath = "usage.output";

        var result = await RunProfileAsync(profile, "ignored");

        Assert.False(result.IsError);
        Assert.NotNull(result.TokensUsed);
        Assert.Equal(123, result.TokensUsed!.InputTokens);
        Assert.Equal(45, result.TokensUsed!.OutputTokens);
    }

    [Fact]
    public async Task RunAsync_ReadOutputFile_PrefersCapturedFileContent()
    {
        var profile = CreateProfile(CreateOutputFileScript());
        profile.InputMode = "arg";
        profile.ReadOutputFile = true;
        profile.OutputFileArgTemplate = "--output-file \"{path}\"";

        var result = await RunProfileAsync(profile, "ignored");

        Assert.False(result.IsError);
        Assert.Equal("from-file", result.Text);
    }

    [Fact]
    public async Task RunAsync_ReadOutputFile_TemplatePathWithSpaces_PassesSingleArgument()
    {
        var tempPath = Path.Combine(_rootPath, "temp path");
        Directory.CreateDirectory(tempPath);

        var envKeys = OperatingSystem.IsWindows() ? new[] { "TMP", "TEMP" } : ["TMPDIR"];
        var previousValues = envKeys.ToDictionary(key => key, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var key in envKeys)
                Environment.SetEnvironmentVariable(key, tempPath);

            var profile = CreateProfile(CreateOutputFileScript());
            profile.InputMode = "arg";
            profile.ReadOutputFile = true;
            profile.OutputFileArgTemplate = "--output-file {path}";

            var result = await RunProfileAsync(profile, "ignored");

            Assert.False(result.IsError);
            Assert.Equal("from-file", result.Text);
        }
        finally
        {
            foreach (var (key, value) in previousValues)
                Environment.SetEnvironmentVariable(key, value);
        }
    }

    [Fact]
    public async Task RunAsync_ArgTemplate_TaskWithSpaces_PassesSingleArgument()
    {
        var profile = CreateProfile(CreateArgsDumpScript());
        profile.InputMode = "arg-template";
        profile.InputArgTemplate = "--task {task}";

        var result = await RunProfileAsync(profile, "hello spaced task");

        Assert.False(result.IsError);
        Assert.Contains("args=--task|hello spaced task", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ResumeInvocation_InsertsResumeArgsBeforePermissionArgs()
    {
        var profile = CreateProfile(CreateArgsDumpScript());
        profile.InputMode = "arg";
        profile.SupportsResume = true;
        profile.ResumeArgTemplate = "--resume {sessionId}";
        profile.ResumeSessionIdJsonPath = "session_id";

        var result = await RunProfileAsync(
            profile,
            "hello-cli",
            extraLaunchArgs: ["--sandbox", "read-only"],
            resumeSessionId: "sess-123");

        Assert.False(result.IsError, result.Text);
        Assert.Contains("args=--resume|sess-123|--sandbox|read-only|hello-cli", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_CodexBuiltInProfileInvocation_UsesSandboxWithoutLegacyApprovalFlag()
    {
        var builtInCodex = SubAgentProfileRegistry.CreateBuiltInProfiles()
            .Single(p => string.Equals(p.Name, "codex-cli", StringComparison.OrdinalIgnoreCase));
        var profile = CreateProfile(CreateArgsDumpScript());
        profile.Name = builtInCodex.Name;
        if (builtInCodex.Args != null)
        {
            foreach (var argument in builtInCodex.Args)
                profile.Args!.Add(argument);
        }
        profile.InputMode = builtInCodex.InputMode;
        profile.SupportsResume = builtInCodex.SupportsResume;
        profile.ResumeArgTemplate = builtInCodex.ResumeArgTemplate;
        profile.PermissionModeMapping = new Dictionary<string, string>(
            builtInCodex.PermissionModeMapping!,
            StringComparer.OrdinalIgnoreCase);

        var interactiveArgs = CliOneshotRuntime.SplitArguments(
            profile.PermissionModeMapping![SubAgentApprovalModeResolver.InteractiveMode]);

        var result = await RunProfileAsync(
            profile,
            "hello-cli",
            extraLaunchArgs: interactiveArgs,
            resumeSessionId: "sess-123");

        Assert.False(result.IsError, result.Text);
        Assert.Contains("args=exec|resume|sess-123|--sandbox|read-only|hello-cli", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("--ask-for-approval", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_JsonOutput_CapturesSessionIdFromJsonPath()
    {
        var profile = CreateProfile(CreateJsonOutputWithSessionScript());
        profile.InputMode = "arg";
        profile.OutputFormat = "json";
        profile.OutputJsonPath = "result";
        profile.SupportsResume = true;
        profile.ResumeArgTemplate = "--resume {sessionId}";
        profile.ResumeSessionIdJsonPath = "session_id";

        var result = await RunProfileAsync(profile, "ignored");

        Assert.False(result.IsError);
        Assert.Equal("sess-json", result.SessionId);
    }

    [Fact]
    public async Task RunAsync_TextOutput_CapturesSessionIdFromRegex_OrFallsBackToResumeId()
    {
        var profile = CreateProfile(CreateRegexSessionOutputScript());
        profile.InputMode = "arg";
        profile.SupportsResume = true;
        profile.ResumeArgTemplate = "--resume {sessionId}";
        profile.ResumeSessionIdRegex = "session=(?<sessionId>[^\\s]+)";

        var result = await RunProfileAsync(profile, "ignored", resumeSessionId: "sess-prev");

        Assert.False(result.IsError);
        Assert.Equal("sess-regex", result.SessionId);
    }

    [Fact]
    public async Task RunAsync_NonZeroExit_ReturnsErrorWithStdoutAndStderr()
    {
        var profile = CreateProfile(CreateFailureScript());
        profile.InputMode = "arg";

        var result = await RunProfileAsync(profile, "ignored");

        Assert.True(result.IsError);
        Assert.Contains("Error: External subagent exited with code 3.", result.Text, StringComparison.Ordinal);
        Assert.Contains("Profile='test-cli'", result.Text, StringComparison.Ordinal);
        Assert.Contains("STDOUT:", result.Text, StringComparison.Ordinal);
        Assert.Contains("stdout-text", result.Text, StringComparison.Ordinal);
        Assert.Contains("STDERR:", result.Text, StringComparison.Ordinal);
        Assert.Contains("stderr-text", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_WhenChildKeepsStdoutPipeOpen_ReturnsErrorInsteadOfHanging()
    {
        if (OperatingSystem.IsWindows())
            return;

        var profile = CreateProfile(CreateLeakedPipeScript());
        profile.InputMode = "arg";
        profile.Timeout = 15;

        var result = await RunProfileAsync(profile, "ignored");

        Assert.True(result.IsError);
        Assert.Contains(
            "output pipe did not close within grace period",
            result.Text,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_ReportsLifecycleProgressToSink()
    {
        var profile = CreateProfile(CreateArgEchoScript());
        profile.InputMode = "arg";
        var sink = new RecordingSink();

        var result = await RunProfileAsync(profile, "hello-cli", sink: sink);

        Assert.False(result.IsError);
        Assert.Contains(sink.ProgressMessages, m => m.Contains("Launching", StringComparison.Ordinal));
        Assert.Contains(sink.ProgressMessages, m => m.Contains("Running", StringComparison.Ordinal));
        Assert.Contains(sink.ProgressMessages, m => m.Contains("Waiting", StringComparison.Ordinal));
        Assert.Contains(sink.CompletedMessages, m => m.Contains("Completed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_ExtraLaunchArgs_ArePrependedToInvocationArguments()
    {
        var profile = CreateProfile(CreateArgsDumpScript());
        profile.InputMode = "arg";

        var result = await RunProfileAsync(
            profile,
            "hello-cli",
            extraLaunchArgs: ["--sandbox", "read-only"]);

        Assert.False(result.IsError);
        Assert.Contains("args=--sandbox|read-only|hello-cli", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void SplitArguments_PreservesQuotedSegments()
    {
        var args = CliOneshotRuntime.SplitArguments("--output-file \"C:\\temp path\\out.txt\" --flag");

        Assert.Equal(["--output-file", "C:\\temp path\\out.txt", "--flag"], args);
    }

    [Fact]
    public void TryResolveExecutablePath_AbsolutePath_ResolvesExistingAndRejectsMissing()
    {
        var knownBinary = OperatingSystem.IsWindows() ? "powershell" : "sh";
        Assert.True(CliOneshotRuntime.TryResolveExecutablePath(knownBinary, out var knownResolved));
        Assert.NotNull(knownResolved);

        Assert.True(CliOneshotRuntime.TryResolveExecutablePath(knownResolved, out var resolvedExisting));
        Assert.Equal(Path.GetFullPath(knownResolved), resolvedExisting);

        var missingPath = Path.Combine(_rootPath, $"missing-{Guid.NewGuid():N}.cmd");
        Assert.False(CliOneshotRuntime.TryResolveExecutablePath(missingPath, out var resolvedMissing));
        Assert.Null(resolvedMissing);
    }

    [Fact]
    public void TryResolveExecutablePath_PathLookup_ResolvesKnownBinaryAndRejectsMissing()
    {
        var knownBinary = OperatingSystem.IsWindows() ? "powershell" : "sh";
        Assert.True(CliOneshotRuntime.TryResolveExecutablePath(knownBinary, out var resolvedKnown));
        Assert.False(string.IsNullOrWhiteSpace(resolvedKnown));

        var missingBinary = $"definitely-missing-{Guid.NewGuid():N}";
        Assert.False(CliOneshotRuntime.TryResolveExecutablePath(missingBinary, out var resolvedMissing));
        Assert.Null(resolvedMissing);
    }

    [Fact]
    public void TryResolveExecutablePath_Windows_PathsWithPathextResolveWithoutExtension()
    {
        if (!OperatingSystem.IsWindows())
            return;

        Assert.True(CliOneshotRuntime.TryResolveExecutablePath("powershell", out var resolved));
        Assert.NotNull(resolved);
        var extension = Path.GetExtension(resolved);
        Assert.True(
            string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".com", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<SubAgentRunResult> RunProfileAsync(
        SubAgentProfile profile,
        string task,
        string? workingDirectory = null,
        IReadOnlyList<string>? extraLaunchArgs = null,
        string? resumeSessionId = null,
        ISubAgentEventSink? sink = null)
    {
        var runtime = new CliOneshotRuntime();
        var effectiveWorkingDirectory = workingDirectory ?? _workspacePath;
        var launchContext = new SubAgentLaunchContext(
            _workspacePath,
            effectiveWorkingDirectory,
            profile.Name,
            extraLaunchArgs,
            resumeSessionId);
        var session = await runtime.CreateSessionAsync(profile, launchContext, CancellationToken.None);
        try
        {
            return await runtime.RunAsync(
                session,
                new SubAgentTaskRequest
                {
                    Task = task,
                    WorkingDirectory = effectiveWorkingDirectory
                },
                sink ?? NullSubAgentEventSink.Instance,
                CancellationToken.None);
        }
        finally
        {
            await runtime.DisposeSessionAsync(session, CancellationToken.None);
        }
    }

    private SubAgentProfile CreateProfile(string scriptPath)
    {
        if (OperatingSystem.IsWindows())
        {
            return new SubAgentProfile
            {
                Name = "test-cli",
                Runtime = CliOneshotRuntime.RuntimeTypeName,
                Bin = "powershell.exe",
                Args = ["-NoLogo", "-NoProfile", "-NonInteractive", "-File", scriptPath],
                WorkingDirectoryMode = "workspace",
                OutputFormat = "text",
                Timeout = 30,
                MaxOutputBytes = 64 * 1024
            };
        }

        return new SubAgentProfile
        {
            Name = "test-cli",
            Runtime = CliOneshotRuntime.RuntimeTypeName,
            Bin = "/bin/sh",
            Args = [scriptPath],
            WorkingDirectoryMode = "workspace",
            OutputFormat = "text",
            Timeout = 30,
            MaxOutputBytes = 64 * 1024
        };
    }

    private string CreateArgEchoScript()
        => CreateScript(
            windows: """
            Write-Output ("cwd=" + (Get-Location).Path)
            if ($args.Count -gt 0) {
                Write-Output ("arg=" + $args[$args.Count - 1])
            }
            """,
            unix: """
            echo "cwd=$(pwd)"
            echo "arg=$1"
            """);

    private string CreateArgsDumpScript()
        => CreateScript(
            windows: """
            if ($args.Count -eq 0) {
                Write-Output 'args='
            } else {
                Write-Output ("args=" + ($args -join '|'))
            }
            """,
            unix: """
            joined=""
            for arg in "$@"; do
              if [ -z "$joined" ]; then
                joined="$arg"
              else
                joined="$joined|$arg"
              fi
            done
            printf 'args=%s\n' "$joined"
            """);

    private string CreateStdinEchoScript()
        => CreateScript(
            windows: """
            $text = [Console]::In.ReadToEnd()
            Write-Output ("stdin=" + $text)
            """,
            unix: """
            input=$(cat)
            printf 'stdin=%s\n' "$input"
            """);

    private string CreateEnvEchoScript()
        => CreateScript(
            windows: """
            Write-Output ("env=" + $env:DOTCRAFT_TEST_TASK)
            """,
            unix: """
            printf 'env=%s\n' "$DOTCRAFT_TEST_TASK"
            """);

    private string CreateCodexApiKeyEchoScript()
        => CreateScript(
            windows: """
            Write-Output ("codex_api_key=" + $env:CODEX_API_KEY)
            """,
            unix: """
            printf 'codex_api_key=%s\n' "$CODEX_API_KEY"
            """);

    private string CreateStdinEofScript()
        => CreateScript(
            windows: """
            $input = [Console]::In.ReadToEnd()
            if ([string]::IsNullOrEmpty($input)) {
                Write-Output 'stdin=eof'
            } else {
                Write-Output ("stdin=" + $input)
            }
            """,
            unix: """
            input=$(cat)
            if [ -z "$input" ]; then
              echo 'stdin=eof'
            else
              printf 'stdin=%s\n' "$input"
            fi
            """);

    private string CreateJsonOutputScript()
        => CreateScript(
            windows: """
            Write-Output '{"result":"json-ok","usage":{"input":123,"output":45,"total":168}}'
            """,
            unix: """
            printf '%s\n' '{"result":"json-ok","usage":{"input":123,"output":45,"total":168}}'
            """);

    private string CreateJsonOutputWithSessionScript()
        => CreateScript(
            windows: """
            Write-Output '{"result":"json-ok","session_id":"sess-json"}'
            """,
            unix: """
            printf '%s\n' '{"result":"json-ok","session_id":"sess-json"}'
            """);

    private string CreateRegexSessionOutputScript()
        => CreateScript(
            windows: """
            Write-Output 'session=sess-regex'
            """,
            unix: """
            printf '%s\n' 'session=sess-regex'
            """);

    private string CreatePassthroughEchoScript()
        => CreateScript(
            windows: """
            Write-Output ("passthrough=" + $env:DOTCRAFT_TEST_PASSTHROUGH)
            """,
            unix: """
            printf 'passthrough=%s\n' "$DOTCRAFT_TEST_PASSTHROUGH"
            """);

    private string CreateOutputFileScript()
        => CreateScript(
            windows: """
            $outputFile = $null
            for ($i = 0; $i -lt $args.Count; $i++) {
                if ($args[$i] -eq '--output-file' -and $i + 1 -lt $args.Count) {
                    $outputFile = $args[$i + 1]
                }
            }
            Set-Content -LiteralPath $outputFile -Value 'from-file' -NoNewline
            Write-Output 'stdout-fallback'
            """,
            unix: """
            output_file=""
            prev=""
            for arg in "$@"; do
              if [ "$prev" = "--output-file" ]; then
                output_file="$arg"
              fi
              prev="$arg"
            done
            printf '%s' 'from-file' > "$output_file"
            echo 'stdout-fallback'
            """);

    private string CreateFailureScript()
        => CreateScript(
            windows: """
            Write-Output 'stdout-text'
            [Console]::Error.WriteLine('stderr-text')
            exit 3
            """,
            unix: """
            echo 'stdout-text'
            echo 'stderr-text' 1>&2
            exit 3
            """);

    private string CreateLeakedPipeScript()
        => CreateScript(
            windows: """
            Write-Output 'skip'
            """,
            unix: """
            ( sleep 5 ) &
            echo 'parent-done'
            exit 0
            """);

    private string CreateScript(string windows, string unix)
    {
        if (OperatingSystem.IsWindows())
        {
            var path = Path.Combine(_rootPath, $"{Guid.NewGuid():N}.ps1");
            File.WriteAllText(path, windows, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return path;
        }

        var shPath = Path.Combine(_rootPath, $"{Guid.NewGuid():N}.sh");
        File.WriteAllText(
            shPath,
            "#!/bin/sh\nset -eu\n" + unix + "\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return shPath;
    }

    private sealed class RecordingSink : ISubAgentEventSink
    {
        public List<string> ProgressMessages { get; } = [];

        public List<string> CompletedMessages { get; } = [];

        public List<string> FailedMessages { get; } = [];

        public void OnInfo(string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
                ProgressMessages.Add(message);
        }

        public void OnProgress(string? currentTool, string? currentToolDisplay = null)
        {
            if (!string.IsNullOrWhiteSpace(currentToolDisplay))
                ProgressMessages.Add(currentToolDisplay);
        }

        public void OnCompleted(string? summary = null, SubAgentTokenUsage? tokensUsed = null)
        {
            if (!string.IsNullOrWhiteSpace(summary))
                CompletedMessages.Add(summary);
        }

        public void OnFailed(string? summary = null, SubAgentTokenUsage? tokensUsed = null)
        {
            if (!string.IsNullOrWhiteSpace(summary))
                FailedMessages.Add(summary);
        }
    }
}
