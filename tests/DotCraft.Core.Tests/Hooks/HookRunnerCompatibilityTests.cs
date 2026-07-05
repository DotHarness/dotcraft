using DotCraft.Hooks;
using DotCraft.Utilities;

namespace DotCraft.Tests.Hooks;

public sealed class HookRunnerCompatibilityTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "HookCompat_" + Guid.NewGuid().ToString("N")[..8]);

    public HookRunnerCompatibilityTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { }
    }

    [Fact]
    public async Task RunAsync_MatchesPortableBashConditionForExecTool()
    {
        var runner = new HookRunner(new HooksFileConfig
        {
            Hooks =
            {
                [nameof(HookEvent.PostToolUse)] =
                [
                    new HookMatcherGroup
                    {
                        Matcher = "Bash",
                        Hooks =
                        [
                            new HookEntry
                            {
                                Type = "command",
                                Command = EchoCommand("MATCHED"),
                                If = "Bash(git commit:*)"
                            }
                        ]
                    }
                ]
            }
        }, _tempDir);

        var result = await runner.RunAsync(
            HookEvent.PostToolUse,
            new HookInput
            {
                SessionId = "thread_1",
                ToolName = "Exec",
                ToolArgs = new Dictionary<string, object?> { ["command"] = "git commit -m test" }
            },
            CancellationToken.None);

        Assert.Equal("MATCHED", result.Output);
    }

    [Fact]
    public async Task RunAsync_ExposesDotCraftAndCompatibilityWorkspaceEnvironment()
    {
        var runner = new HookRunner(new HooksFileConfig
        {
            Hooks =
            {
                [nameof(HookEvent.SessionStart)] =
                [
                    new HookMatcherGroup
                    {
                        Hooks =
                        [
                            new HookEntry
                            {
                                Type = "command",
                                Command = WorkspaceEnvironmentCommand()
                            }
                        ]
                    }
                ]
            }
        }, _tempDir);

        var result = await runner.RunAsync(
            HookEvent.SessionStart,
            new HookInput { SessionId = "thread_1" },
            CancellationToken.None);

        Assert.Equal($"{_tempDir}|{_tempDir}", result.Output);
    }

    [Fact]
    public async Task RunAsync_UsesStandardGitBashPathWhenBareBashIsNotOnPath()
    {
        if (!OperatingSystem.IsWindows() || !StandardGitBashExists())
            return;

        var runner = new HookRunner(new HooksFileConfig
        {
            Hooks =
            {
                [nameof(HookEvent.SessionStart)] =
                [
                    new HookMatcherGroup
                    {
                        Hooks =
                        [
                            new HookEntry
                            {
                                Type = "command",
                                Command = "bash -lc \"printf '%s' git-bash-ok\"",
                                EnvironmentVariables = { ["PATH"] = _tempDir }
                            }
                        ]
                    }
                ]
            }
        }, _tempDir);

        var result = await runner.RunAsync(
            HookEvent.SessionStart,
            new HookInput { SessionId = "thread_1" },
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("git-bash-ok", result.Output);
    }

    [Theory]
    [InlineData("bash \"./hooks/check.sh\"", true)]
    [InlineData("pwsh -c \"echo bash\"", false)]
    [InlineData("echo bash", false)]
    [InlineData("C:\\Program Files\\Git\\bin\\bash.exe -lc true", false)]
    [InlineData("echo ok; bash -lc true", true)]
    public void CommandReferencesBareBash_OnlyMatchesCommandTokens(string command, bool expected)
    {
        Assert.Equal(expected, HookRunner.CommandReferencesBareBash(command));
    }

    [Fact]
    public void ConfigureShellCommandArguments_PreservesQuotedCommandAsSingleArgument()
    {
        var psi = new System.Diagnostics.ProcessStartInfo { FileName = "/bin/bash" };
        const string command = "printf '%s\\n' 'quoted value'";

        HookRunner.ConfigureShellCommandArguments(psi, command);

        Assert.Equal(new[] { "-c", command }, psi.ArgumentList.ToArray());
        Assert.Equal(string.Empty, psi.Arguments);
    }

    [Fact]
    public void PowerShellStderrSanitizer_ExtractsReadableCliXmlErrors()
    {
        const string cliXml = "#< CLIXML\r\n<Objs Version=\"1.1.0.1\" xmlns=\"http://schemas.microsoft.com/powershell/2004/04\"><S S=\"Error\">bash : The term 'bash' is not recognized_x000D__x000A_At line:1 char:1</S></Objs>";

        var stderr = PowerShellStderrSanitizer.Sanitize(cliXml);

        Assert.Contains("The term 'bash' is not recognized", stderr);
        Assert.Contains("At line:1 char:1", stderr);
        Assert.DoesNotContain("CLIXML", stderr);
        Assert.DoesNotContain("_x000D_", stderr);
    }

    [Fact]
    public async Task RunAsync_ParsesJsonAdditionalContext()
    {
        var runner = new HookRunner(new HooksFileConfig
        {
            Hooks =
            {
                [nameof(HookEvent.PostToolUse)] =
                [
                    new HookMatcherGroup
                    {
                        Hooks =
                        [
                            new HookEntry
                            {
                                Type = "command",
                                Command = JsonAdditionalContextCommand("PostToolUse", "SECURITY_CONTEXT")
                            }
                        ]
                    }
                ]
            }
        }, _tempDir);

        var result = await runner.RunAsync(
            HookEvent.PostToolUse,
            new HookInput { SessionId = "thread_1", ToolName = "WriteFile" },
            CancellationToken.None);

        Assert.Equal("SECURITY_CONTEXT", result.AdditionalContext);
    }

    [Fact]
    public async Task RunAsync_AsyncRewakeDispatchesContinuation()
    {
        var tcs = new TaskCompletionSource<HookRewakeRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new HookRunner(new HooksFileConfig
        {
            Hooks =
            {
                [nameof(HookEvent.Stop)] =
                [
                    new HookMatcherGroup
                    {
                        Hooks =
                        [
                            new HookEntry
                            {
                                Key = "hook-key",
                                Type = "command",
                                Command = JsonBlockCommand("review finding"),
                                AsyncRewake = true,
                                RewakeMessage = "Review feedback:"
                            }
                        ]
                    }
                ]
            }
        }, _tempDir)
        {
            RewakeHandler = (request, _) =>
            {
                tcs.TrySetResult(request);
                return Task.CompletedTask;
            }
        };

        await runner.RunAsync(
            HookEvent.Stop,
            new HookInput { SessionId = "thread_1", TurnId = "turn_1", Response = "done" },
            CancellationToken.None);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(tcs.Task, completed);
        var rewake = await tcs.Task;
        Assert.Equal("thread_1", rewake.ThreadId);
        Assert.Contains("Review feedback:", rewake.Prompt);
        Assert.Contains("review finding", rewake.Prompt);
    }

    [Fact]
    public async Task RunAsync_StopAsyncRewakeSkipsHookOriginTurn()
    {
        var rewakeCalled = false;
        var runner = new HookRunner(new HooksFileConfig
        {
            Hooks =
            {
                [nameof(HookEvent.Stop)] =
                [
                    new HookMatcherGroup
                    {
                        Hooks =
                        [
                            new HookEntry
                            {
                                Key = "hook-key",
                                Type = "command",
                                Command = JsonBlockCommand("review finding"),
                                AsyncRewake = true,
                                RewakeMessage = "Review feedback:"
                            }
                        ]
                    }
                ]
            }
        }, _tempDir)
        {
            RewakeHandler = (_, _) =>
            {
                rewakeCalled = true;
                return Task.CompletedTask;
            }
        };

        await runner.RunAsync(
            HookEvent.Stop,
            new HookInput
            {
                SessionId = "thread_1",
                TurnId = "turn_1",
                Response = "done",
                StopHookActive = true
            },
            CancellationToken.None);

        Assert.False(rewakeCalled);
    }

    [Fact]
    public async Task RunAsync_AsyncRewakeUsesAdditionalContextAsContinuation()
    {
        var tcs = new TaskCompletionSource<HookRewakeRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new HookRunner(new HooksFileConfig
        {
            Hooks =
            {
                [nameof(HookEvent.PostToolUse)] =
                [
                    new HookMatcherGroup
                    {
                        Matcher = "Bash",
                        Hooks =
                        [
                            new HookEntry
                            {
                                Key = "commit-hook",
                                Type = "command",
                                Command = JsonAdditionalContextCommand("PostToolUse", "commit review finding"),
                                If = "Bash(git commit:*)",
                                AsyncRewake = true,
                                RewakeMessage = "Commit review:"
                            }
                        ]
                    }
                ]
            }
        }, _tempDir)
        {
            RewakeHandler = (request, _) =>
            {
                tcs.TrySetResult(request);
                return Task.CompletedTask;
            }
        };

        await runner.RunAsync(
            HookEvent.PostToolUse,
            new HookInput
            {
                SessionId = "thread_1",
                ToolName = "Exec",
                ToolArgs = new Dictionary<string, object?> { ["command"] = "git commit -m test" }
            },
            CancellationToken.None);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(tcs.Task, completed);
        var rewake = await tcs.Task;
        Assert.Equal("commit-hook", rewake.HookKey);
        Assert.Contains("Commit review:", rewake.Prompt);
        Assert.Contains("commit review finding", rewake.Prompt);
    }

    private static string EchoCommand(string output) =>
        OperatingSystem.IsWindows()
            ? $"Write-Output '{output}'"
            : $"printf '%s\\n' '{output}'";

    private static bool StandardGitBashExists() =>
        File.Exists(@"C:\Program Files\Git\bin\bash.exe") ||
        File.Exists(@"C:\Program Files\Git\usr\bin\bash.exe") ||
        File.Exists(@"C:\Program Files (x86)\Git\bin\bash.exe") ||
        File.Exists(@"C:\Program Files (x86)\Git\usr\bin\bash.exe");

    private static string WorkspaceEnvironmentCommand() =>
        OperatingSystem.IsWindows()
            ? "Write-Output \"$env:DOTCRAFT_WORKSPACE_ROOT|$env:CLAUDE_PROJECT_DIR\""
            : "printf '%s|%s\\n' \"$DOTCRAFT_WORKSPACE_ROOT\" \"$CLAUDE_PROJECT_DIR\"";

    private static string JsonAdditionalContextCommand(string eventName, string output)
    {
        var json = "{\"hookSpecificOutput\":{\"hookEventName\":\"" + eventName + "\",\"additionalContext\":\"" + output + "\"}}";
        return OperatingSystem.IsWindows()
            ? $"Write-Output '{json}'"
            : $"printf '%s\\n' '{json}'";
    }

    private static string JsonBlockCommand(string reason)
    {
        var json = "{\"decision\":\"block\",\"reason\":\"" + reason + "\"}";
        return OperatingSystem.IsWindows()
            ? $"Write-Output '{json}'"
            : $"printf '%s\\n' '{json}'";
    }
}
