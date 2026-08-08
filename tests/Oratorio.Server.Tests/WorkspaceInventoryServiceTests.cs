using Oratorio.Server.DotCraft;

namespace Oratorio.Server.Tests;

public sealed class WorkspaceInventoryServiceTests
{
    [Fact]
    public async Task GetWorkspacesAsync_DeduplicatesMappedWorkspacePathsAndMergesRepositories()
    {
        var root = Directory.CreateTempSubdirectory("oratorio-workspaces-");
        var mappedWorkspace = Directory.CreateDirectory(Path.Combine(root.FullName, "mapped")).FullName;
        var processManager = new FakeDotCraftProcessManager(
            connected: true,
            endpoint: new DotCraftAppServerEndpoint("ws://127.0.0.1:9200/ws", "hub"));
        var service = new WorkspaceInventoryService(
            new StaticOptionsMonitor<DotCraftOptions>(new DotCraftOptions
            {
                RepositoryWorkspaceRoutes =
                [
                    new() { Project = "github:github.com/example-owner/oratorio", WorkspacePath = mappedWorkspace },
                    new() { Project = "github:github.com/example-owner/companion-repo", WorkspacePath = mappedWorkspace }
                ]
            }),
            processManager,
            new FixedClock(DateTimeOffset.Parse("2026-05-08T10:00:00Z")));

        try
        {
            var response = await service.GetWorkspacesAsync(CancellationToken.None);

            Assert.Equal("single", service.GetWorkspaceMode());
            Assert.Equal(1, response.Summary.Total);
            Assert.Equal(1, response.Summary.Connected);
            Assert.Single(processManager.ProbeWorkspacePaths);
            var mapped = Assert.Single(response.Workspaces);
            Assert.False(mapped.IsDefault);
            Assert.Equal(
                ["github:github.com/example-owner/companion-repo", "github:github.com/example-owner/oratorio"],
                mapped.Repositories);
            Assert.Equal("hub", mapped.EndpointSource);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
