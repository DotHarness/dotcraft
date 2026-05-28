using DotCraft.Tools;
using DotCraft.Tests.Lsp;

namespace DotCraft.Tests.Tools;

public class FileToolsLspSyncTests
{
    [Fact]
    public async Task WriteAndEditFile_TriggerLspChangeAndSaveNotifications()
    {
        var workspace = CreateWorkspace();
        var manager = new SpyLspServerManager(workspace);
        var tools = new FileTools(
            workspace,
            requireApprovalOutsideWorkspace: false,
            lspServerManager: manager);

        var writeResult = await tools.WriteFile("notes.txt", "hello");
        Assert.StartsWith("Successfully wrote", writeResult, StringComparison.Ordinal);
        Assert.Single(manager.ChangeCalls);
        Assert.Single(manager.SaveCalls);

        var editResult = await tools.EditFile("notes.txt", oldText: "hello", newText: "world");
        Assert.StartsWith("Successfully edited", editResult, StringComparison.Ordinal);
        Assert.Equal(2, manager.ChangeCalls.Count);
        Assert.Equal(2, manager.SaveCalls.Count);
        Assert.Equal("world", await File.ReadAllTextAsync(Path.Combine(workspace, "notes.txt")));
    }

    private static string CreateWorkspace()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "dotcraft-filetools-lsp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        return workspace;
    }
}
