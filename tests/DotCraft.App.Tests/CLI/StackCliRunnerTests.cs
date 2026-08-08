using System.Text.Json.Nodes;
using DotCraft.CLI;
using Xunit;

namespace DotCraft.Tests.CLI;

public sealed class StackCliRunnerTests
{
    [Fact]
    public async Task InitDryRunDoesNotWriteOrGenerateSecrets()
    {
        var path = NewPath();
        var output = new StringWriter();

        var exitCode = await StackCliRunner.RunAsync(
            ["init", "--dir", path, "--dry-run"], output, new StringWriter(), CancellationToken.None, new FakeRunner());

        Assert.Equal(0, exitCode);
        Assert.False(Directory.Exists(path));
        Assert.DoesNotContain("token (shown once)", output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InitWritesOfficialSharedWorkspaceStackAndIndependentTokens()
    {
        var path = NewPath();
        try
        {
            var exitCode = await StackCliRunner.RunAsync(
                ["init", "--dir", path, "--no-start"], new StringWriter(), new StringWriter(), CancellationToken.None, new FakeRunner());

            Assert.Equal(0, exitCode);
            var compose = await File.ReadAllTextAsync(Path.Combine(path, "docker-compose.yml"));
            Assert.Contains("  dotcraft:", compose);
            Assert.Contains("  oratorio:", compose);
            Assert.Equal(2, Count(compose, ":/workspace"));
            Assert.Contains("/workspace/.craft/oratorio/worktrees", compose);
            Assert.Contains("Oratorio__Settings__Writable: \"true\"", compose);

            var env = await File.ReadAllLinesAsync(Path.Combine(path, ".env"));
            var appServerToken = Value(env, "APPSERVER_TOKEN");
            var oratorioToken = Value(env, "ORATORIO_SERVICE_TOKEN");
            Assert.NotEmpty(appServerToken);
            Assert.NotEmpty(oratorioToken);
            Assert.NotEqual(appServerToken, oratorioToken);
            Assert.True(File.Exists(Path.Combine(path, "state", "oratorio", "config.json")));
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task AddProjectWritesGitHubAndGitLabRoutesWithoutFallback()
    {
        var path = NewPath();
        try
        {
            await StackCliRunner.RunAsync(
                ["init", "--dir", path, "--no-start"], new StringWriter(), new StringWriter(), CancellationToken.None, new FakeRunner());

            Assert.Equal(0, await StackCliRunner.RunAsync(
                ["add-project", "--dir", path, "--provider", "github", "--project", "Acme/One", "--workspace", "/workspace/one"],
                new StringWriter(), new StringWriter(), CancellationToken.None, new FakeRunner()));
            Assert.Equal(0, await StackCliRunner.RunAsync(
                ["add-project", "--dir", path, "--provider", "gitlab", "--project", "group/two", "--workspace", "/workspace/two"],
                new StringWriter(), new StringWriter(), CancellationToken.None, new FakeRunner()));

            var root = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(path, "state", "oratorio", "config.json")))!;
            var oratorio = root["Oratorio"]!;
            var routes = oratorio["DotCraft"]!["RepositoryWorkspaceRoutes"]!.AsArray();
            Assert.Collection(routes,
                route =>
                {
                    Assert.Equal("github:github.com/acme/one", route!["Project"]!.GetValue<string>());
                    Assert.Equal("/workspace/one", route["WorkspacePath"]!.GetValue<string>());
                },
                route =>
                {
                    Assert.Equal("gitlab:gitlab.com/group/two", route!["Project"]!.GetValue<string>());
                    Assert.Equal("/workspace/two", route["WorkspacePath"]!.GetValue<string>());
                });
            Assert.Equal("github.com/acme/one", oratorio["GitHub"]!["Repositories"]![0]!.GetValue<string>());
            Assert.Equal("gitlab.com/group/two", oratorio["GitLab"]!["Projects"]![0]!.GetValue<string>());
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task AddProjectRejectsIncompleteConfigurationInsteadOfRepairingIt()
    {
        var path = NewPath();
        try
        {
            Directory.CreateDirectory(Path.Combine(path, "state", "oratorio"));
            var configPath = Path.Combine(path, "state", "oratorio", "config.json");
            await File.WriteAllTextAsync(configPath, "{}");
            var error = new StringWriter();

            var exitCode = await StackCliRunner.RunAsync(
                ["add-project", "--dir", path, "--provider", "github", "--project", "Acme/One", "--workspace", "/workspace/one"],
                new StringWriter(), error, CancellationToken.None, new FakeRunner());

            Assert.Equal(1, exitCode);
            Assert.Contains("missing object 'Oratorio'", error.ToString());
            Assert.Equal("{}", await File.ReadAllTextAsync(configPath));
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task UpgradeUsesOnlyAllowListedComposeOperationsAndDryRunIsNonMutating()
    {
        var path = NewPath();
        try
        {
            await StackCliRunner.RunAsync(
                ["init", "--dir", path, "--no-start"], new StringWriter(), new StringWriter(), CancellationToken.None, new FakeRunner());
            var runner = new FakeRunner();

            Assert.Equal(0, await StackCliRunner.RunAsync(
                ["upgrade", "--dir", path, "--dry-run"], new StringWriter(), new StringWriter(), CancellationToken.None, runner));
            Assert.Empty(runner.Calls);

            Assert.Equal(0, await StackCliRunner.RunAsync(
                ["upgrade", "--dir", path], new StringWriter(), new StringWriter(), CancellationToken.None, runner));
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
    public async Task WebhookEnableAndDisableManageOnlyGatewayAssets()
    {
        var path = NewPath();
        try
        {
            await StackCliRunner.RunAsync(
                ["init", "--dir", path, "--no-start"], new StringWriter(), new StringWriter(), CancellationToken.None, new FakeRunner());
            var runner = new FakeRunner();

            Assert.Equal(0, await StackCliRunner.RunAsync(
                ["webhook", "enable", "--dir", path, "--public-host", "hooks.example.com"],
                new StringWriter(), new StringWriter(), CancellationToken.None, runner));
            Assert.True(File.Exists(Path.Combine(path, "docker-compose.webhook.yml")));
            var caddy = await File.ReadAllTextAsync(Path.Combine(path, "Caddyfile"));
            Assert.Contains("method POST", caddy);
            Assert.Contains("path /api/v1/sources/github/webhook", caddy);
            Assert.Contains("respond 404", caddy);

            Assert.Equal(0, await StackCliRunner.RunAsync(
                ["webhook", "disable", "--dir", path], new StringWriter(), new StringWriter(), CancellationToken.None, runner));
            Assert.False(File.Exists(Path.Combine(path, "docker-compose.webhook.yml")));
            Assert.False(File.Exists(Path.Combine(path, "Caddyfile")));
            Assert.True(Directory.Exists(Path.Combine(path, "state")));
        }
        finally
        {
            TryDelete(path);
        }
    }

    private static string NewPath() => Path.Combine(Path.GetTempPath(), $"dotcraft-stack-{Guid.NewGuid():N}");
    private static int Count(string value, string fragment) => value.Split(fragment).Length - 1;
    private static string Value(IEnumerable<string> lines, string name) =>
        lines.Single(line => line.StartsWith(name + "=", StringComparison.Ordinal)).Split('=', 2)[1];

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
