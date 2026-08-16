using System.Text.Json.Nodes;

namespace DotCraft.DynamicWorkflows.Tests;

public sealed class StructuredWorkflowResultRegistryTests
{
    [Fact]
    public void Submit_RejectsInvalidThenAcceptsCorrectedValue()
    {
        var registry = new StructuredWorkflowResultRegistry();
        registry.Bind("thread_child", JsonNode.Parse("""
            {"type":"object","properties":{"summary":{"type":"string"}},"required":["summary"],"additionalProperties":false}
            """)!);

        Assert.False(registry.TrySubmit("thread_child", JsonNode.Parse("""{"wrong":1}"""), out var error));
        Assert.NotNull(error);
        Assert.Contains(" at: /", error, StringComparison.Ordinal);
        Assert.True(registry.TrySubmit("thread_child", JsonNode.Parse("""{"summary":"ok"}"""), out error));
        Assert.Null(error);
        Assert.True(registry.TryGetResult("thread_child", out var result));
        Assert.Equal("ok", result!["summary"]!.GetValue<string>());
    }

    [Fact]
    public void Submit_RejectsResultThatExceedsBoundLimit()
    {
        var registry = new StructuredWorkflowResultRegistry();
        registry.Bind("thread_child", JsonNode.Parse("""{"type":"string"}""")!, maxResultBytes: 8);

        Assert.False(registry.TrySubmit("thread_child", JsonValue.Create("too-large"), out var error));
        Assert.Contains("size limit", error, StringComparison.Ordinal);
    }
}
