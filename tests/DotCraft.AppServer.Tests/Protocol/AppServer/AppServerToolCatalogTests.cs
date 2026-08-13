using System.Text.Json;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerToolCatalogTests
{
    [Fact]
    public async Task ToolList_ReturnsBuiltInToolsWithMetadata()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ToolList, new { }));
        using var response = harness.Transport.TryReadSent()!;
        var tools = response.RootElement.GetProperty("result").GetProperty("tools");

        var names = tools.EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString())
            .ToList();
        Assert.Contains("ReadFile", names);
        Assert.Contains("WriteFile", names);
        Assert.Contains("EditFile", names);
        Assert.Contains("GrepFiles", names);

        var readFile = FindTool(tools, "ReadFile");
        Assert.Equal("📄", readFile.GetProperty("icon").GetString());
        Assert.Equal("builtin", readFile.GetProperty("source").GetString());
        Assert.False(string.IsNullOrWhiteSpace(readFile.GetProperty("description").GetString()));
    }

    [Fact]
    public async Task ToolList_IsSortedByNameOrdinal()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ToolList, new { }));
        using var response = harness.Transport.TryReadSent()!;
        var names = response.RootElement.GetProperty("result").GetProperty("tools")
            .EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString()!)
            .ToList();

        var sorted = names.OrderBy(name => name, StringComparer.Ordinal).ToList();
        Assert.Equal(sorted, names);
    }

    [Fact]
    public async Task ToolList_AnnotatesPlanModeAvailability()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ToolList, new { }));
        using var response = harness.Transport.TryReadSent()!;
        var tools = response.RootElement.GetProperty("result").GetProperty("tools");

        Assert.True(FindTool(tools, "ReadFile").GetProperty("planMode").GetBoolean());
        Assert.False(FindTool(tools, "WriteFile").GetProperty("planMode").GetBoolean());
        Assert.False(FindTool(tools, "EditFile").GetProperty("planMode").GetBoolean());
    }

    [Fact]
    public async Task ToolList_PlanModeFilter_ExcludesMutatingTools()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ToolList, new { mode = "plan" }));
        using var response = harness.Transport.TryReadSent()!;
        var names = response.RootElement.GetProperty("result").GetProperty("tools")
            .EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString())
            .ToList();

        Assert.Contains("ReadFile", names);
        Assert.Contains("GrepFiles", names);
        Assert.DoesNotContain("WriteFile", names);
        Assert.DoesNotContain("EditFile", names);
        Assert.DoesNotContain("imagegen", names);
    }

    [Fact]
    public void BuiltInToolCatalog_DeduplicatesAndSorts()
    {
        var descriptors = BuiltInToolCatalog.Enumerate();

        var names = descriptors.Select(descriptor => descriptor.Name).ToList();
        Assert.Equal(names.Distinct(StringComparer.Ordinal).Count(), names.Count);
        Assert.Equal(names.OrderBy(name => name, StringComparer.Ordinal).ToList(), names);
        Assert.All(descriptors, descriptor => Assert.False(string.IsNullOrEmpty(descriptor.Icon)));
        Assert.DoesNotContain(descriptors, descriptor => descriptor.Name == "imagegen");
    }

    [Fact]
    public void BuiltInToolCatalog_UsesCanonicalHostDescriptorForSandboxAlternates()
    {
        var exec = Assert.Single(BuiltInToolCatalog.Enumerate(), descriptor => descriptor.Name == "Exec");

        Assert.Contains("On Windows PowerShell", exec.Description, StringComparison.Ordinal);
    }

    private static JsonElement FindTool(JsonElement tools, string name) =>
        tools.EnumerateArray().Single(tool => tool.GetProperty("name").GetString() == name);
}
