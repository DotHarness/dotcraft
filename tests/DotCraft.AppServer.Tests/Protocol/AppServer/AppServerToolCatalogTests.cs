using System.Text.Json;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerToolCatalogTests
{
    [Fact]
    public async Task ToolList_ReturnsSortedBuiltInToolsWithMetadataAndPlanModeAnnotations()
    {
        var descriptorNames = BuiltInToolCatalog.Enumerate().Select(descriptor => descriptor.Name).ToList();
        Assert.Equal(descriptorNames.Distinct(StringComparer.Ordinal).Count(), descriptorNames.Count);

        var tools = await ListToolsAsync(new { });
        var names = tools.EnumerateArray().Select(tool => tool.GetProperty("name").GetString()!).ToList();
        Assert.Equal(names.OrderBy(name => name, StringComparer.Ordinal).ToList(), names);
        Assert.Contains("GrepFiles", names);
        var readFile = FindTool(tools, "ReadFile");
        Assert.False(readFile.TryGetProperty("icon", out _));
        Assert.Equal("builtin", readFile.GetProperty("source").GetString());
        Assert.False(string.IsNullOrWhiteSpace(readFile.GetProperty("description").GetString()));
        Assert.True(readFile.GetProperty("planMode").GetBoolean());
        Assert.False(FindTool(tools, "WriteFile").GetProperty("planMode").GetBoolean());
        Assert.False(FindTool(tools, "EditFile").GetProperty("planMode").GetBoolean());
    }

    [Fact]
    public async Task ToolList_PlanModeFilter_ExcludesMutatingToolsButKeepsMessageBoardTools()
    {
        var tools = await ListToolsAsync(new { mode = "plan" });
        var names = tools.EnumerateArray().Select(tool => tool.GetProperty("name").GetString()).ToList();

        Assert.Contains("ReadFile", names);
        Assert.Contains("GrepFiles", names);
        Assert.DoesNotContain("WriteFile", names);
        Assert.DoesNotContain("EditFile", names);
        foreach (var name in new[] { "BoardCreateChannel", "BoardGetChannels", "BoardListThreads", "BoardSearchPosts",
                     "BoardReadThread", "BoardReadPost", "BoardSubscribe", "BoardUnsubscribe", "BoardPost" })
            Assert.True(FindTool(tools, name).GetProperty("planMode").GetBoolean());
    }

    private static async Task<JsonElement> ListToolsAsync(object parameters)
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync();
        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ToolList, parameters));
        using var response = harness.Transport.TryReadSent()!;
        return response.RootElement.GetProperty("result").GetProperty("tools").Clone();
    }

    private static JsonElement FindTool(JsonElement tools, string name) =>
        tools.EnumerateArray().Single(tool => tool.GetProperty("name").GetString() == name);
}
