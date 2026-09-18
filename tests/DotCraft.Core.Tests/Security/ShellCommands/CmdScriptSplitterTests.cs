using DotCraft.Security.ShellCommands;
using Xunit;

namespace DotCraft.Tests.Security.ShellCommands;

public sealed class CmdScriptSplitterTests
{
    [Fact]
    public void Lower_AnyScript_IsOpaque()
    {
        var lowered = new CmdScriptSplitter().Lower("dir");

        Assert.Null(lowered.PlainCommands);
        Assert.Equal("cmd scripts are not lowered.", lowered.PlainRejectReason);
        Assert.Equal(ShellFamily.Cmd, lowered.Family);
    }

    [Theory]
    [InlineData("echo hi&del /f file.txt")]
    [InlineData("echo hi&&del /f file.txt")]
    [InlineData("echo hi||del /f file.txt")]
    [InlineData("echo hi | del /f file.txt")]
    public void Lower_SeparatorGluedToWords_SplitsTheSegments(string script)
    {
        var lowered = new CmdScriptSplitter().Lower(script);

        AssertCommands([["echo", "hi"], ["del", "/f", "file.txt"]], lowered.LiteralCommands);
    }

    [Theory]
    [InlineData("start \"\" https://example.com", "start", "", "https://example.com")]
    [InlineData("cmd /c \"echo hi&del /f file.txt\"", "cmd", "/c", "echo hi&del /f file.txt")]
    [InlineData("echo hi^&del /f file.txt", "echo", "hi&del", "/f", "file.txt")]
    public void Lower_QuotedOrEscapedSeparator_StaysInOneSegment(string script, params string[] expected)
    {
        var lowered = new CmdScriptSplitter().Lower(script);

        Assert.Equal(expected, Assert.Single(lowered.LiteralCommands));
    }

    [Fact]
    public void Lower_EmptyScript_HasNoLiteralCommands()
    {
        var lowered = new CmdScriptSplitter().Lower("   ");

        Assert.Empty(lowered.LiteralCommands);
    }

    private static void AssertCommands(string[][] expected, IReadOnlyList<IReadOnlyList<string>> actual)
    {
        Assert.Equal(expected.Length, actual.Count);
        for (var index = 0; index < expected.Length; index++)
            Assert.Equal(expected[index], actual[index]);
    }
}
