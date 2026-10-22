using DotCraft.Sessions;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class FileToolsLocationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("dotcraft-tool-location-").FullName;

    [Fact]
    public async Task WriteFile_ResolvesRelativePathsAgainstTheMovedLocation()
    {
        var workspace = Directory.CreateDirectory(Path.Combine(_root, "repo")).FullName;
        var worktree = Directory.CreateDirectory(Path.Combine(_root, "worktree")).FullName;
        var location = new ToolLocation(new ThreadWorkspaceContext(workspace, [workspace]));
        var tools = new FileTools(workspace, requireApprovalOutsideWorkspace: false, location: location);

        location.MoveTo(new ThreadWorkspaceContext(worktree, [worktree]));
        var result = await tools.WriteFile("notes.txt", "moved");

        Assert.True(result.Success, result.Content);
        Assert.Equal("moved", await File.ReadAllTextAsync(Path.Combine(worktree, "notes.txt")));
        Assert.False(File.Exists(Path.Combine(workspace, "notes.txt")));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
