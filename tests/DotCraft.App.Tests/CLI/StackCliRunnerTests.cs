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

    [Fact]
    public async Task UpgradeRejectsDeploymentWithoutPersistedDotCraftUserData()
    {
        var path = NewPath();
        try
        {
            await StackCliRunner.InitAsync(
                new StackCommandOptions { Directory = path, NoStart = true },
                new StringWriter(), new StringWriter(), CancellationToken.None, new FakeRunner());
            var compose = Path.Combine(path, "docker-compose.yml");
            var content = await File.ReadAllTextAsync(compose);
            var mount = "      - ${DOTCRAFT_STACK_STATE_DIR:-./state}/dotcraft:/root/.craft";
            var mountAt = content.IndexOf(mount, StringComparison.Ordinal);
            Assert.True(mountAt >= 0);
            await File.WriteAllTextAsync(compose, content.Remove(mountAt, mount.Length));

            var runner = new FakeRunner();
            var errors = new StringWriter();
            var doctorOutput = new StringWriter();
            Assert.Equal(1, await StackCliRunner.DoctorAsync(
                new StackCommandOptions { Directory = path },
                doctorOutput, new StringWriter(), CancellationToken.None, runner));
            Assert.Contains("[fail] DotCraft user data mount", doctorOutput.ToString());
            runner.Calls.Clear();
            var result = await StackCliRunner.UpgradeAsync(
                new StackCommandOptions { Directory = path },
                new StringWriter(), errors, CancellationToken.None, runner);

            Assert.Equal(1, result);
            Assert.Contains("/root/.craft", errors.ToString());
            Assert.Empty(runner.Calls);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Theory]
    [InlineData("      - ./saved-dotcraft:/root/.craft")]
    [InlineData("      - '/srv/dotcraft/config:/root/.craft:rw'")]
    [InlineData("      - type: bind\n        source: ./saved-dotcraft\n        target: /root/.craft")]
    [InlineData("      - type: volume\n        source: dotcraft-data\n        target: /root/.craft")]
    public async Task CustomPersistedUserDataMountPassesDoctorAndUpgrade(string mount)
    {
        var path = NewPath();
        try
        {
            await StackCliRunner.InitAsync(
                new StackCommandOptions { Directory = path, NoStart = true },
                new StringWriter(), new StringWriter(), CancellationToken.None, new FakeRunner());
            var compose = Path.Combine(path, "docker-compose.yml");
            var content = await File.ReadAllTextAsync(compose);
            content = content.Replace(
                "      - ${DOTCRAFT_STACK_STATE_DIR:-./state}/dotcraft:/root/.craft",
                mount,
                StringComparison.Ordinal);
            await File.WriteAllTextAsync(compose, content);

            var runner = new FakeRunner();
            Assert.Equal(0, await StackCliRunner.DoctorAsync(
                new StackCommandOptions { Directory = path },
                new StringWriter(), new StringWriter(), CancellationToken.None, runner));
            runner.Calls.Clear();
            Assert.Equal(0, await StackCliRunner.UpgradeAsync(
                new StackCommandOptions { Directory = path },
                new StringWriter(), new StringWriter(), CancellationToken.None, runner));
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Theory]
    [InlineData("      - ./saved-dotcraft:/root/.craft:ro")]
    [InlineData("      - type: bind\n        source: ./saved-dotcraft\n        target: /root/.craft\n        read_only: true")]
    public async Task ReadOnlyUserDataMountBlocksUpgrade(string mount)
    {
        var path = NewPath();
        try
        {
            await StackCliRunner.InitAsync(
                new StackCommandOptions { Directory = path, NoStart = true },
                new StringWriter(), new StringWriter(), CancellationToken.None, new FakeRunner());
            var compose = Path.Combine(path, "docker-compose.yml");
            var content = await File.ReadAllTextAsync(compose);
            await File.WriteAllTextAsync(compose, content.Replace(
                "      - ${DOTCRAFT_STACK_STATE_DIR:-./state}/dotcraft:/root/.craft",
                mount,
                StringComparison.Ordinal));

            var runner = new FakeRunner();
            Assert.Equal(1, await StackCliRunner.UpgradeAsync(
                new StackCommandOptions { Directory = path },
                new StringWriter(), new StringWriter(), CancellationToken.None, runner));
            Assert.Empty(runner.Calls);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task SubscriptionUpgradeUsesBaseStack()
    {
        var path = NewPath();
        try
        {
            await StackCliRunner.InitAsync(
                new StackCommandOptions { Directory = path, NoStart = true },
                new StringWriter(), new StringWriter(), CancellationToken.None, new FakeRunner());
            var envPath = Path.Combine(path, ".env");
            var env = await File.ReadAllTextAsync(envPath);
            await File.WriteAllTextAsync(envPath, env.Replace("DOTCRAFT_AUTH_METHOD=apiKey", "DOTCRAFT_AUTH_METHOD=chatgptOAuth"));

            var runner = new FakeRunner();
            Assert.Equal(0, await StackCliRunner.UpgradeAsync(
                new StackCommandOptions { Directory = path },
                new StringWriter(), new StringWriter(), CancellationToken.None, runner));
            Assert.Collection(runner.Calls,
                call => Assert.EndsWith("-f docker-compose.yml pull", string.Join(' ', call.Arguments), StringComparison.Ordinal),
                call => Assert.EndsWith("-f docker-compose.yml up -d --remove-orphans", string.Join(' ', call.Arguments), StringComparison.Ordinal));
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task UpgradeRejectsMissingAuthenticationMethodBeforePull()
    {
        var path = NewPath();
        try
        {
            await StackCliRunner.InitAsync(
                new StackCommandOptions { Directory = path, NoStart = true },
                new StringWriter(), new StringWriter(), CancellationToken.None, new FakeRunner());
            var envPath = Path.Combine(path, ".env");
            var env = await File.ReadAllTextAsync(envPath);
            await File.WriteAllTextAsync(envPath, env.Replace("DOTCRAFT_AUTH_METHOD=apiKey\n", ""));

            var runner = new FakeRunner();
            var errors = new StringWriter();
            Assert.Equal(1, await StackCliRunner.UpgradeAsync(
                new StackCommandOptions { Directory = path },
                new StringWriter(), errors, CancellationToken.None, runner));
            Assert.Contains("DOTCRAFT_AUTH_METHOD", errors.ToString());
            Assert.Empty(runner.Calls);
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
