using System.Text.Json.Nodes;
using DotCraft.Tools;

namespace DotCraft.DynamicWorkflows.Tests;

public sealed class StructuredWorkflowResultRegistryTests
{
    [Fact]
    public async Task ToolSourceUsesClosedGeneratedArbitraryJsonSchema()
    {
        var registry = new StructuredWorkflowResultRegistry();
        registry.Bind("thread_child", JsonNode.Parse("{}")!);
        var source = new StructuredWorkflowResultToolSource(registry);
        var registration = Assert.Single(await source.GetRegistrationsAsync(new ToolPlanningContext(
            "thread_child",
            "turn_child",
            Path.GetTempPath(),
            Path.Combine(Path.GetTempPath(), ".craft"),
            "agent",
            null,
            [],
            1)));

        var schema = registration.Definition.InputSchema;
        Assert.Equal(["result"], schema.GetProperty("required").EnumerateArray().Select(static value => value.GetString()));
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.False(schema.GetProperty("properties").GetProperty("result").TryGetProperty("type", out _));
        Assert.Null(registration.Definition.OutputSchema);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("1")]
    [InlineData("[1,2]")]
    [InlineData("{\"summary\":\"ok\"}")]
    public async Task ToolRuntimeAcceptsEveryJsonValueShape(string json)
    {
        var registry = new StructuredWorkflowResultRegistry();
        registry.Bind("thread_child", JsonNode.Parse("{}")!);
        var source = new StructuredWorkflowResultToolSource(registry);
        var registration = Assert.Single(await source.GetRegistrationsAsync(new ToolPlanningContext(
            "thread_child",
            "turn_child",
            Path.GetTempPath(),
            Path.Combine(Path.GetTempPath(), ".craft"),
            "agent",
            null,
            [],
            1)));
        var value = JsonNode.Parse(json);
        var arguments = new JsonObject { ["result"] = value?.DeepClone() };
        var result = await registration.Binding.Runtime.InvokeAsync(
            new ToolInvocationContext(
                "thread_child",
                "turn_child",
                "call_result",
                ToolInvocationAudience.Model,
                registration.Definition.Name,
                registration.Definition.Id,
                registration.Binding.Id,
                1,
                DateTimeOffset.UtcNow),
            arguments);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(ToolExecutionDirective.TerminateTurn, result.Directive);
        Assert.True(registry.TryGetResult("thread_child", out var stored));
        Assert.True(JsonNode.DeepEquals(value, stored));
    }

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
