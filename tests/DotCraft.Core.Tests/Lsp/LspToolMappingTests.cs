using System.Text.Json;
using DotCraft.Tools;

namespace DotCraft.Tests.Lsp;

public class LspToolMappingTests
{
    [Fact]
    public async Task Lsp_GoToDefinition_MapsToDefinitionRequest_WithZeroBasedPosition()
    {
        var workspace = CreateWorkspace();
        var filePath = Path.Combine(workspace, "sample.cs");
        await File.WriteAllTextAsync(filePath, "class A { void M(){} }");

        var manager = new SpyLspServerManager(workspace);
        manager.RequestResults.Enqueue(JsonSerializer.SerializeToElement(new[]
        {
            new
            {
                uri = new Uri(filePath).AbsoluteUri,
                range = new
                {
                    start = new { line = 9, character = 4 },
                    end = new { line = 9, character = 8 }
                }
            }
        }));

        var tool = new LspTool(workspace, manager, requireApprovalOutsideWorkspace: false);
        var result = await tool.LSP("goToDefinition", "sample.cs", 3, 7);

        Assert.Single(manager.RequestMethods);
        Assert.Equal("textDocument/definition", manager.RequestMethods[0]);

        var request = manager.RequestParams[0];
        var position = request.GetProperty("position");
        Assert.Equal(2, position.GetProperty("line").GetInt32());
        Assert.Equal(6, position.GetProperty("character").GetInt32());
        Assert.True(manager.OpenCalls.Count >= 1);
        Assert.Contains("Definition:", result);
        Assert.Contains(":10:5", result);
    }

    [Fact]
    public async Task Lsp_IncomingCalls_UsesPrepareThenIncomingRequests()
    {
        var workspace = CreateWorkspace();
        var filePath = Path.Combine(workspace, "sample.cs");
        await File.WriteAllTextAsync(filePath, "class A { void M(){} }");

        var uri = new Uri(filePath).AbsoluteUri;
        var manager = new SpyLspServerManager(workspace);
        manager.RequestResults.Enqueue(JsonSerializer.SerializeToElement(new[]
        {
            new
            {
                name = "M",
                kind = 6,
                uri,
                range = new
                {
                    start = new { line = 1, character = 1 },
                    end = new { line = 1, character = 2 }
                }
            }
        }));
        manager.RequestResults.Enqueue(JsonSerializer.SerializeToElement(new[]
        {
            new
            {
                from = new
                {
                    name = "Caller",
                    uri,
                    range = new
                    {
                        start = new { line = 4, character = 2 },
                        end = new { line = 4, character = 8 }
                    }
                }
            }
        }));

        var tool = new LspTool(workspace, manager, requireApprovalOutsideWorkspace: false);
        var result = await tool.LSP("incomingCalls", "sample.cs", 2, 2);

        Assert.Equal(
            ["textDocument/prepareCallHierarchy", "callHierarchy/incomingCalls"],
            manager.RequestMethods);
        Assert.Contains("incoming calls", result, StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateWorkspace()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "dotcraft-lsp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        return workspace;
    }
}
