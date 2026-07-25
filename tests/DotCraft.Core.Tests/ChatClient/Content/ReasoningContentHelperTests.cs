using DotCraft.Agents;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Agents;

public sealed class ReasoningContentHelperTests
{
    [Theory]
    [InlineData("hello ")]
    [InlineData("  leading space")]
    [InlineData("\nnext line")]
    [InlineData("   ")]
    public void TryGetText_PreservesProviderWhitespace(string text)
    {
        var extracted = ReasoningContentHelper.TryGetText(
            new TextReasoningContent(text),
            out var actual);

        Assert.True(extracted);
        Assert.Equal(text, actual);
    }

    [Fact]
    public void EnumerateTexts_PreservesChunkBoundariesWithoutTrimming()
    {
        AIContent[] contents =
        [
            new TextReasoningContent("hello "),
            new TextReasoningContent("world"),
            new TextContent("not reasoning"),
            new TextReasoningContent("\nnext")
        ];

        Assert.Equal(["hello ", "world", "\nnext"], ReasoningContentHelper.EnumerateTexts(contents));
    }

    [Fact]
    public void TryGetText_RejectsEmptyText()
    {
        var extracted = ReasoningContentHelper.TryGetText(
            new TextReasoningContent(string.Empty),
            out var actual);

        Assert.False(extracted);
        Assert.Equal(string.Empty, actual);
    }
}
