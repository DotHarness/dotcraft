using System.Text;
using DotCraft.Tools.BackgroundTerminals;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class TerminalOutputBufferTests
{
    [Fact]
    public void CompleteOutputDoesNotDuplicateOverlappingExcerpts()
    {
        var buffer = new TerminalOutputBuffer();
        buffer.Append("first\n中文😀");
        buffer.Append("\nlast\r\n");

        var result = buffer.Snapshot(100);

        Assert.Equal("first\n中文😀\nlast", result.Output);
        Assert.Equal(result.Output.Length, result.OriginalChars);
        Assert.False(result.Truncated);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    public void TruncationKeepsBothEndsWithinBudgetOnCharacterBoundaries(int limit)
    {
        var content = "😀" + string.Concat(Enumerable.Repeat("中😀", 10_000)) + "😀";
        var buffer = new TerminalOutputBuffer();
        buffer.Append(content);

        var result = buffer.Snapshot(limit);

        var parts = result.Output.Split(Environment.NewLine);
        Assert.Equal(3, parts.Length);
        Assert.Equal("😀", parts[0][..2]);
        Assert.EndsWith("😀", parts[2]);
        Assert.InRange(parts[0].Length + parts[2].Length, 1, limit);
        Assert.Equal(content.Length, result.OriginalChars);
        Assert.True(result.Truncated);
        Assert.Equal($"... (truncated, {content.Length - parts[0].Length - parts[2].Length} middle chars)", parts[1]);
        new UTF8Encoding(false, true).GetByteCount(result.Output);
    }

    [Fact]
    public void PrefixRemainsContiguousWhenItsByteBudgetEndsBeforeAMultibyteCharacter()
    {
        var buffer = new TerminalOutputBuffer();
        var head = new string('a', TerminalOutputBuffer.CapacityBytes / 2 - 1);
        buffer.Append(head + "😀");
        buffer.Append(new string('b', TerminalOutputBuffer.CapacityBytes) + "end");

        var result = buffer.Snapshot(0);

        Assert.StartsWith(head + Environment.NewLine, result.Output);
        Assert.EndsWith("end", result.Output);
        Assert.InRange(Encoding.UTF8.GetByteCount(result.Output), 1, TerminalOutputBuffer.CapacityBytes + 100);
        Assert.True(result.Truncated);
    }
}
