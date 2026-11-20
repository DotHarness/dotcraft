using System.Diagnostics;
using DotCraft.CLI;
using Xunit;

namespace DotCraft.Tests.CLI;

public sealed class DotCraftCommandLineTests
{
    [Fact]
    public async Task RootWithoutArguments_ShowsHelpWithoutInitializingAWorkspace()
    {
        var workingDirectory = Path.Combine(Path.GetTempPath(), $"dotcraft-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workingDirectory);
        try
        {
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            start.ArgumentList.Add(typeof(DotCraftCommandLine).Assembly.Location);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            Assert.Equal(0, process.ExitCode);
            Assert.Contains("tool-host", await output);
            Assert.Equal(string.Empty, await error);
            Assert.False(Directory.Exists(Path.Combine(workingDirectory, ".craft")));
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData(2, "tool-host")]
    [InlineData(0, "tool-host", "setup", "--help")]
    [InlineData(0, "codex-remote-control", "status", "--help")]
    public async Task HelpForGroupsAndLeaves_DoesNotExecuteTheCommand(int expectedExitCode, string first, params string[] remaining)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await DotCraftCommandLine.RunAsync([first, .. remaining], output, error);

        Assert.Equal(expectedExitCode, exitCode);
        Assert.Contains(remaining.Length == 0 ? "invite" : "--help", output.ToString());
    }

    [Theory]
    [InlineData("auth", "login", "openai", "--stdin")]
    [InlineData("setup", "--unknown")]
    [InlineData("app-server", "--listen", "http://localhost:9100")]
    [InlineData("tool-host", "policy", "set", "Exec", "sometimes")]
    [InlineData("exec", "resume", "--model", " ", "--last", "hello")]
    [InlineData("exec", "resume", "--ephemeral", "thread_9", "hello")]
    [InlineData("exec", "--unknown-option", "hello")]
    public async Task InvalidInput_IsRejectedAsUsageError(string first, params string[] remaining)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await DotCraftCommandLine.RunAsync([first, .. remaining], output, error);

        Assert.Equal(2, exitCode);
        Assert.NotEqual(string.Empty, error.ToString());
    }

    [Fact]
    public void AuthRouting_UsesStandaloneFromUserHome_AndKeepsWorkspaceGuardForExplicitWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotcraft-auth-route-{Guid.NewGuid():N}");
        var userCraftHome = Path.Combine(root, "home", ".craft");
        var workspaceCraft = Path.Combine(root, "project", ".craft");
        Directory.CreateDirectory(userCraftHome);
        Directory.CreateDirectory(workspaceCraft);
        try
        {
            var implicitAuth = new CommandLineArgs { Mode = CommandLineArgs.RunMode.Auth, AuthAction = "status", SetupProviderId = "openai" };
            var explicitHomeAuth = implicitAuth with { AuthWorkspacePath = Path.Combine(root, "home") };

            Assert.True(DotCraftApplication.ShouldRunStandaloneAuth(implicitAuth, userCraftHome, userCraftHome));
            Assert.False(DotCraftApplication.ShouldRunStandaloneAuth(implicitAuth, workspaceCraft, userCraftHome));
            Assert.False(DotCraftApplication.ShouldRunStandaloneAuth(explicitHomeAuth, userCraftHome, userCraftHome));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
