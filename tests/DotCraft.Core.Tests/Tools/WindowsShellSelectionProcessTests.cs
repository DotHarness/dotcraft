using DotCraft.Configuration;
using DotCraft.Hooks;
using DotCraft.Security.ShellCommands;
using DotCraft.Tools.BackgroundTerminals;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class WindowsShellSelectionProcessTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DotCraftShell_" + Guid.NewGuid().ToString("N"));

    public WindowsShellSelectionProcessTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData(null)]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task LaunchAndStdin_RetainResolvedShellAndUnicodeOutput(string? selector)
    {
        if (!OperatingSystem.IsWindows())
            return;
        if (!ShellIdentityResolver.Host.TryResolve(selector, out var shell, out _))
        {
            Assert.Equal("pwsh", selector);
            return;
        }

        await using var service = new BackgroundTerminalService(_root, new AppConfig.ShellBackgroundConfig());
        var started = await service.StartAsync(new BackgroundTerminalStartRequest
        {
            Shell = selector is null ? null : shell,
            WorkingDirectory = _root,
            Command = "[Console]::WriteLine((Get-Process -Id $PID).Path); " +
                "[Console]::WriteLine('中文输出'); [Console]::WriteLine([Console]::ReadLine()); exit 7",
            RunInBackground = true,
            Interactive = true,
            YieldTimeMs = 100,
            TimeoutSeconds = 15
        });

        Assert.Equal(shell, service.GetStdinSession(started.SessionId)!.Shell);
        var finished = await service.WriteStdinAsync(started.SessionId, "stdin-marker\n", yieldTimeMs: 5000);
        if (finished.Status == BackgroundTerminalStatus.Running)
            finished = await service.ReadAsync(started.SessionId, waitMs: 5000);

        Assert.Equal(7, finished.ExitCode);
        Assert.Contains(shell.ExecutablePath, finished.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("中文输出", finished.Output);
        Assert.Contains("stdin-marker", finished.Output);
    }

    [Fact]
    public async Task Pwsh_ExecutesPipelineChains()
    {
        if (!OperatingSystem.IsWindows() || !ShellIdentityResolver.Host.TryResolve("pwsh", out var shell, out _))
            return;

        await using var service = new BackgroundTerminalService(_root, new AppConfig.ShellBackgroundConfig());
        var finished = await service.StartAsync(new BackgroundTerminalStartRequest
        {
            Shell = shell,
            WorkingDirectory = _root,
            Command = "Write-Output first && Write-Output second",
            TimeoutSeconds = 10
        });
        Assert.Equal(0, finished.ExitCode);
        Assert.Contains("first", finished.Output);
        Assert.Contains("second", finished.Output);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task Hooks_PreserveJsonStdinAndBlockingExitCode(string? selector)
    {
        if (!OperatingSystem.IsWindows())
            return;
        if (!ShellIdentityResolver.Host.TryResolve(selector, out var shell, out _))
        {
            Assert.Equal("pwsh", selector);
            return;
        }

        var runner = new HookRunner(new HooksFileConfig
        {
            Hooks =
            {
                [nameof(HookEvent.PreToolUse)] =
                [
                    new HookMatcherGroup
                    {
                        Hooks =
                        [
                            new HookEntry
                            {
                                Shell = selector,
                                Command = "$data = [Console]::In.ReadToEnd() | ConvertFrom-Json; " +
                                    "Write-Output $data.session_id; Write-Output (Get-Process -Id $PID).Path; " +
                                    "[Console]::Error.WriteLine('阻断原因'); exit 2"
                            }
                        ]
                    }
                ]
            }
        }, _root);
        var result = await runner.RunAsync(HookEvent.PreToolUse,
            new HookInput { SessionId = "shell-json-marker", ToolName = "Exec" }, CancellationToken.None);

        Assert.Equal(2, result.ExitCode);
        Assert.True(result.Blocked);
        Assert.Contains("shell-json-marker", result.Output!);
        Assert.Contains(shell.ExecutablePath, result.Output!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("阻断原因", result.BlockReason!);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
