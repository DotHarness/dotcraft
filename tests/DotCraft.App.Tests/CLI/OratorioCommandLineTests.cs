using DotCraft.CLI;
using Xunit;

namespace DotCraft.Tests.CLI;

public sealed class OratorioCommandLineTests
{
    [Fact]
    public async Task Help_DoesNotInitializeServiceState()
    {
        var state = Path.Combine(Path.GetTempPath(), $"oratorio-help-{Guid.NewGuid():N}");

        var exitCode = await DotCraftCommandLine.RunAsync(
            ["oratorio", "serve", "--state", state, "--help"], TextWriter.Null, TextWriter.Null);

        Assert.Equal(0, exitCode);
        Assert.False(Directory.Exists(state));
    }

    [Theory]
    [InlineData("--listen", "ws://127.0.0.1:5087")]
    [InlineData("--listen", "http://127.0.0.1:5087/api")]
    [InlineData("--listen", "http://user:secret@127.0.0.1:5087")]
    [InlineData("--listen", "http://127.0.0.1:5087?token=secret")]
    [InlineData("--state", " ")]
    [InlineData("--token", "secret")]
    public async Task InvalidArguments_AreUsageErrorsWithoutInitializingState(string option, string value)
    {
        var state = Path.Combine(Path.GetTempPath(), $"oratorio-invalid-{Guid.NewGuid():N}");

        var args = option == "--state"
            ? new[] { "oratorio", "serve", option, value }
            : new[] { "oratorio", "serve", "--state", state, option, value };
        var exitCode = await DotCraftCommandLine.RunAsync(args, TextWriter.Null, TextWriter.Null);

        Assert.Equal(2, exitCode);
        Assert.False(Directory.Exists(state));
    }

    [Fact]
    public async Task StartupFailure_ReturnsNonZero()
    {
        var root = Path.Combine(Path.GetTempPath(), $"oratorio-failure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var state = Path.Combine(root, "not-a-directory");
        await File.WriteAllTextAsync(state, "occupied");
        try
        {
            var exitCode = await DotCraftCommandLine.RunAsync(
                ["oratorio", "serve", "--state", state, "--listen", "http://127.0.0.1:0"], TextWriter.Null, TextWriter.Null);

            Assert.Equal(1, exitCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
