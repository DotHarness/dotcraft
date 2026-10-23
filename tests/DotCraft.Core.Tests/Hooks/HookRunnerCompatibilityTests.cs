using System.Collections.Concurrent;
using DotCraft.Hooks;
using DotCraft.Utilities;
using Xunit;

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
    public async Task RunAsync_PreservesOutputWhenHookClosesStdin()
    {
        if (OperatingSystem.IsWindows())
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
                                Command = "exec 0<&-; printf '%s\\n' 'NO_STDIN_OK'"
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
        Assert.Equal("NO_STDIN_OK", result.Output);
    }

    [Theory]
    [InlineData("bash \"./hooks/check.sh\"", true)]
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
    public async Task RunAsync_MatchesPortableBashConditionAndDispatchesBlockReasonAndAdditionalContextAsContinuations()
    {
        var rewakes = new ConcurrentDictionary<string, HookRewakeRequest>();
        var bothReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
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
                                Key = "block-hook",
                                Type = "command",
                                Command = JsonBlockCommand("review finding"),
                                AsyncRewake = true,
                                RewakeMessage = "Review feedback:"
                            },
                            new HookEntry
                            {
                                Key = "push-hook",
                                Type = "command",
                                Command = JsonAdditionalContextCommand("PostToolUse", "push review finding"),
                                If = "Bash(git push:*)",
                                AsyncRewake = true
                            },
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
                rewakes[request.HookKey] = request;
                if (rewakes.Count == 2)
                    bothReceived.TrySetResult();
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

        await bothReceived.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var block = rewakes["block-hook"];
        Assert.Equal("thread_1", block.ThreadId);
        Assert.Contains("Review feedback:", block.Prompt);
        Assert.Contains("review finding", block.Prompt);
        var commit = rewakes["commit-hook"];
        Assert.Contains("Commit review:", commit.Prompt);
        Assert.Contains("commit review finding", commit.Prompt);
    }

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
