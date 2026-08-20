using System.Text;
using System.Text.Json;
using DotCraft.Mcp;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;
using Xunit;

namespace DotCraft.Tests.Mcp;

public sealed class McpToolRuntimeResultBudgetTests
{
    [Fact]
    public void BoundPersistedResult_PreservesResultWithinBudget()
    {
        var raw = JsonSerializer.SerializeToElement(new
        {
            content = new[] { new { type = "text", text = "ok" } },
            structuredContent = new { value = 1 },
            _meta = new { source = "test" },
            isError = false
        });
        var structured = JsonSerializer.SerializeToElement(new { value = 1 });
        var meta = JsonSerializer.SerializeToElement(new { source = "test" });

        var bounded = McpToolRuntime.BoundPersistedResult(raw, structured, meta, isError: false);

        Assert.Equal(raw.GetRawText(), bounded.Raw.GetRawText());
        Assert.Equal(structured.GetRawText(), bounded.Structured?.GetRawText());
        Assert.Equal(meta.GetRawText(), bounded.Meta?.GetRawText());
    }

    [Theory]
    [InlineData("[1,2,3]")]
    [InlineData("\"value\"")]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("null")]
    public void BoundPersistedResult_PreservesNonObjectStructuredContent(string structuredJson)
    {
        var raw = JsonSerializer.SerializeToElement(new { content = Array.Empty<object>(), isError = false });
        var structured = JsonDocument.Parse(structuredJson).RootElement.Clone();

        var bounded = McpToolRuntime.BoundPersistedResult(raw, structured, meta: null, isError: false);

        Assert.Equal(structured.GetRawText(), bounded.Structured?.GetRawText());
    }

    [Fact]
    public void BoundPersistedResult_CollapsesOversizedRawResultWithoutChangingErrorState()
    {
        var raw = JsonSerializer.SerializeToElement(new
        {
            content = new[] { new { type = "text", text = new string('x', 2_200_000) } },
            structuredContent = new { value = new string('y', 128) },
            _meta = new { secret = new string('z', 128) },
            isError = false
        });
        var structured = JsonSerializer.SerializeToElement(new { value = "discarded" });
        var meta = JsonSerializer.SerializeToElement(new { secret = "discarded" });

        var bounded = McpToolRuntime.BoundPersistedResult(raw, structured, meta, isError: false);

        Assert.True(Encoding.UTF8.GetByteCount(bounded.Raw.GetRawText()) <= 2 * 1024 * 1024);
        Assert.Null(bounded.Structured);
        Assert.Null(bounded.Meta);
        Assert.False(bounded.Raw.GetProperty("isError").GetBoolean());
        Assert.Contains(
            "MCP result truncated before persistence",
            bounded.Raw.GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public void NormalizeModelContentItems_PreservesMcpTextAndImage()
    {
        var imageBytes = "mcp-image"u8.ToArray();
        var result = new CallToolResult
        {
            Content =
            [
                new TextContentBlock { Text = "captured" },
                ImageContentBlock.FromBytes(imageBytes, "image/png")
            ]
        };

        var contentItems = Assert.IsAssignableFrom<IReadOnlyList<AIContent>>(
            McpToolRuntime.NormalizeModelContentItems(result));

        Assert.Equal("captured", Assert.IsType<TextContent>(contentItems[0]).Text);
        var image = Assert.IsType<DataContent>(contentItems[1]);
        Assert.Equal("image/png", image.MediaType);
        Assert.Equal(imageBytes, image.Data.ToArray());
    }
}
