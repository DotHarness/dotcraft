using DotCraft.CLI;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace DotCraft.Tests.CLI;

public sealed class StackDeploymentTemplateTests
{
    [Fact]
    public async Task InitSharesImageAndWorkspaceButIsolatesServiceStateAndCredentials()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dotcraft-stack-template-{Guid.NewGuid():N}");
        try
        {
            var exitCode = await StackCliRunner.InitAsync(
                new StackCommandOptions { Directory = path, NoStart = true },
                TextWriter.Null, TextWriter.Null, CancellationToken.None, new NoProcessRunner());
            Assert.Equal(0, exitCode);
            using var reader = File.OpenText(Path.Combine(path, "docker-compose.yml"));
            var yaml = new YamlStream();
            yaml.Load(reader);
            var services = Mapping((YamlMappingNode)yaml.Documents.Single().RootNode, "services");
            var dotcraft = Mapping(services, "dotcraft");
            var oratorio = Mapping(services, "oratorio");

            Assert.Equal(Scalar(dotcraft, "image"), Scalar(oratorio, "image"));
            Assert.False(dotcraft.Children.ContainsKey(new YamlScalarNode("command")));
            Assert.Equal(["/opt/dotcraft/oratorio-entrypoint.sh"], Sequence(oratorio, "command"));

            var volumes = Sequence(oratorio, "volumes");
            const string sharedWorkspace = "${DOTCRAFT_WORKSPACE_DIR:-./workspace}:/workspace";
            Assert.Contains(sharedWorkspace, Sequence(dotcraft, "volumes"));
            Assert.Contains(sharedWorkspace, volumes);
            Assert.Contains("${DOTCRAFT_STACK_STATE_DIR:-./state}/oratorio:/data/oratorio", volumes);
            Assert.DoesNotContain(volumes, mount => mount.Contains(":/root/.craft", StringComparison.Ordinal));
            Assert.False(oratorio.Children.ContainsKey(new YamlScalarNode("env_file")));

            var environment = Mapping(oratorio, "environment");
            Assert.Equal("${ORATORIO_SERVICE_TOKEN:?set ORATORIO_SERVICE_TOKEN in .env}", Scalar(environment, "DOTCRAFT_MANAGED_SERVICE_TOKEN"));
            Assert.Equal("${APPSERVER_TOKEN:?set APPSERVER_TOKEN in .env}", Scalar(environment, "Oratorio__DotCraft__AppServerToken"));
            Assert.False(environment.Children.ContainsKey(new YamlScalarNode("DOTCRAFT_API_KEY")));
            Assert.False(environment.Children.ContainsKey(new YamlScalarNode("DOTCRAFT_MODEL_SERVICE_TOKEN")));
        }
        finally
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
    }

    private static YamlMappingNode Mapping(YamlMappingNode parent, string key) =>
        (YamlMappingNode)parent.Children[new YamlScalarNode(key)];

    private static string Scalar(YamlMappingNode parent, string key) =>
        ((YamlScalarNode)parent.Children[new YamlScalarNode(key)]).Value!;

    private static string[] Sequence(YamlMappingNode parent, string key) =>
        ((YamlSequenceNode)parent.Children[new YamlScalarNode(key)]).Children
            .Select(node => ((YamlScalarNode)node).Value!)
            .ToArray();

    private sealed class NoProcessRunner : IStackProcessRunner
    {
        public Task<StackProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken ct) =>
            throw new InvalidOperationException("Initializing with --no-start must not launch a process.");
    }
}
