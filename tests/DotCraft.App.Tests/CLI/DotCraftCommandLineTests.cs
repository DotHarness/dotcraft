using System.Diagnostics;
using DotCraft.CLI;
using Xunit;

namespace DotCraft.Tests.CLI;

public sealed class DotCraftCommandLineTests
{
    [Fact]
    public async Task RootWithoutArguments_ShowsHelpAndSucceeds()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await DotCraftCommandLine.RunAsync([], output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("tool-host", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task RootWithoutArguments_DoesNotInitializeAWorkspace()
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
            await process.WaitForExitAsync();

            Assert.Equal(0, process.ExitCode);
            Assert.False(Directory.Exists(Path.Combine(workingDirectory, ".craft")));
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task CommandGroupWithoutSubcommand_ShowsGroupHelpAndFails()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await DotCraftCommandLine.RunAsync(["tool-host"], output, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("workspace", output.ToString());
    }

    [Fact]
    public async Task LeafHelp_SucceedsWithoutExecutingTheCommand()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await DotCraftCommandLine.RunAsync(
            ["tool-host", "setup", "--help"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("https-endpoint", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Theory]
    [InlineData("setup", "--unknown")]
    [InlineData("app-server", "--listen", "http://localhost:9100")]
    [InlineData("tool-host", "register")]
    [InlineData("tool-host", "policy", "set", "Exec", "sometimes")]
    public async Task InvalidInput_IsRejected(string first, params string[] remaining)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var args = new[] { first }.Concat(remaining).ToArray();

        var exitCode = await DotCraftCommandLine.RunAsync(args, output, error);

        Assert.Equal(1, exitCode);
        Assert.NotEqual(string.Empty, error.ToString());
    }
}
