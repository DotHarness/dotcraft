using DotCraft.CLI;
using Xunit;

namespace DotCraft.Tests.CLI;

public sealed class RemoteStackTests
{
    [Fact]
    public async Task RemoteDeploymentPersistsOnlyClientCredentialAndChecksServiceBeforeDocker()
    {
        var root = Directory.CreateTempSubdirectory("remote-stack-").FullName;
        try
        {
            var tokenFile = Path.Combine(root, "client-token");
            await File.WriteAllTextAsync(tokenFile, "client-test");
            var path = Path.Combine(root, "stack");
            var output = new StringWriter();
            var runner = new Runner();
            Assert.Equal(0, await StackCliRunner.InitAsync(new StackCommandOptions
            {
                Directory = path, NoStart = true, Provider = "primary", Model = "model",
                ModelServiceUrl = "http://127.0.0.1:1/model-service", ModelServiceTokenFile = tokenFile
            }, output, new StringWriter(), CancellationToken.None, runner));
            Assert.DoesNotContain("client-test", output.ToString());
            var env = await File.ReadAllTextAsync(Path.Combine(path, ".env"));
            Assert.Contains("DOTCRAFT_MODEL_MODE=remote", env);
            Assert.Contains("DOTCRAFT_MODEL_SERVICE_TOKEN=client-test", env);
            Assert.Equal(0, await StackCliRunner.UpgradeAsync(new StackCommandOptions { Directory = path, DryRun = true },
                output, new StringWriter(), CancellationToken.None, runner));
            Assert.Equal(1, await StackCliRunner.UpgradeAsync(new StackCommandOptions { Directory = path },
                output, new StringWriter(), CancellationToken.None, runner));
            Assert.Equal(0, runner.Calls);
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class Runner : IStackProcessRunner
    {
        public int Calls { get; private set; }
        public Task<StackProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new StackProcessResult(0, "", ""));
        }
    }
}
