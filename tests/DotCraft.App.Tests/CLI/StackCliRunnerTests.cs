using DotCraft.CLI;
using Xunit;

namespace DotCraft.Tests.CLI;

public sealed class StackCliRunnerTests
{
    [Fact]
    public async Task InitDryRunDoesNotWrite()
    {
        var path = NewPath();

        var exitCode = await StackCliRunner.InitAsync(
            new StackCommandOptions { Directory = path, DryRun = true },
            new StringWriter(), new StringWriter(), CancellationToken.None, new FakeRunner());

        Assert.Equal(0, exitCode);
        Assert.False(Directory.Exists(path));
    }

    [Fact]
    public async Task UpgradeUsesOnlyAllowListedComposeOperationsAndDryRunIsNonMutating()
    {
        var path = NewPath();
        try
        {
            await StackCliRunner.InitAsync(
                new StackCommandOptions { Directory = path, NoStart = true },
                new StringWriter(), new StringWriter(), CancellationToken.None, new FakeRunner());
            var runner = new FakeRunner();

            Assert.Equal(0, await StackCliRunner.UpgradeAsync(
                new StackCommandOptions { Directory = path, DryRun = true },
                new StringWriter(), new StringWriter(), CancellationToken.None, runner));
            Assert.Empty(runner.Calls);

            Assert.Equal(0, await StackCliRunner.UpgradeAsync(
                new StackCommandOptions { Directory = path },
                new StringWriter(), new StringWriter(), CancellationToken.None, runner));
            Assert.Collection(runner.Calls,
                call => Assert.EndsWith("pull", string.Join(' ', call.Arguments), StringComparison.Ordinal),
                call => Assert.EndsWith("up -d --remove-orphans", string.Join(' ', call.Arguments), StringComparison.Ordinal));
        }
        finally
        {
            TryDelete(path);
        }
    }

    private static string NewPath() => Path.Combine(Path.GetTempPath(), $"dotcraft-stack-{Guid.NewGuid():N}");

    private static void TryDelete(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    private sealed class FakeRunner : IStackProcessRunner
    {
        public List<(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory)> Calls { get; } = [];

        public Task<StackProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken ct)
        {
            Calls.Add((fileName, arguments.ToArray(), workingDirectory));
            return Task.FromResult(new StackProcessResult(0, "ok", string.Empty));
        }
    }
}
