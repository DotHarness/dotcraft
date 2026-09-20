using DotCraft.Security.ShellCommands;
using Xunit;

namespace DotCraft.Tests.Security.ShellCommands;

public sealed class PosixScriptLowererTests
{
    [Theory]
    [InlineData("ls -1", "ls", "-1")]
    [InlineData("echo 123 456", "echo", "123", "456")]
    [InlineData("echo \"/usr\"'/'\"local\"/bin", "echo", "/usr/local/bin")]
    [InlineData("echo '/usr'\"/\"'local'/bin", "echo", "/usr/local/bin")]
    [InlineData("rg -n \"foo\" -g\"*.py\"", "rg", "-n", "foo", "-g*.py")]
    [InlineData("grep -n 'pattern' -g'*.txt'", "grep", "-n", "pattern", "-g*.txt")]
    [InlineData("git commit -m \"line1\nline2\"", "git", "commit", "-m", "line1\nline2")]
    [InlineData(
        "echo \"~HOME\" 'HEAD~1' \"HEAD^\" 'foo#bar' \"=sh\" 'file~'",
        "echo", "~HOME", "HEAD~1", "HEAD^", "foo#bar", "=sh", "file~")]
    [InlineData("echo -\"{a,b}\" '*?[]~^#=\\\\'", "echo", "-{a,b}", "*?[]~^#=\\\\")]
    [InlineData("echo \"\"", "echo", "")]
    public void Lower_PlainCommand_KeepsEveryWordLiteral(string script, params string[] expected)
    {
        var lowered = Lower(script);

        Assert.True(lowered.IsPlain, lowered.PlainRejectReason);
        Assert.Equal(expected, Assert.Single(lowered.PlainCommands!));
    }

    [Fact]
    public void Lower_SimpleCommandsJoinedBySeparators_YieldsOneCommandEach()
    {
        var lowered = Lower("ls && pwd; echo 'hi there' | wc -l");

        AssertCommands(
            [["ls"], ["pwd"], ["echo", "hi there"], ["wc", "-l"]],
            lowered.PlainCommands);
    }

    [Fact]
    public void Lower_CommandsSeparatedByNewline_YieldsOneCommandEach()
    {
        var lowered = Lower("git status\r\ngit diff --stat");

        AssertCommands([["git", "status"], ["git", "diff", "--stat"]], lowered.PlainCommands);
    }

    [Theory]
    [InlineData("ls\n")]
    [InlineData("ls;")]
    [InlineData("\nls\n\n")]
    public void Lower_TrailingTerminator_StaysPlain(string script)
    {
        var lowered = Lower(script);

        AssertCommands([["ls"]], lowered.PlainCommands);
    }

    [Theory]
    [InlineData("echo \"hi ${USER}\"", "expansion")]
    [InlineData("echo \"$HOME\"", "expansion")]
    [InlineData("echo $HOME", "expansion")]
    [InlineData("echo $(pwd)", "expansion")]
    [InlineData("echo `pwd`", "expansion")]
    [InlineData("rg -g\"$VAR\" pattern", "expansion")]
    [InlineData("rg -g\"$(pwd)\" pattern", "expansion")]
    [InlineData("find . $'\\x2ddelete'", "expansion")]
    [InlineData("(ls)", "denies '('")]
    [InlineData("ls || (pwd && echo hi)", "denies '('")]
    [InlineData("ls > out.txt", "denies '>'")]
    [InlineData("dotnet test > out.txt", "denies '>'")]
    [InlineData("echo hi & echo bye", "background execution")]
    [InlineData("find . -{delete,print}", "denies '{'")]
    [InlineData("rg --pre{=,=sh} pattern payload.sh", "denies '{'")]
    [InlineData("find . -del*", "denies '*'")]
    [InlineData("find . -delet?", "denies '?'")]
    [InlineData("find . -delet[e]", "denies '['")]
    [InlineData("find . -de\\lete", "denies '\\'")]
    [InlineData("echo ~", "denies '~'")]
    [InlineData("echo HEAD~1", "denies '~'")]
    [InlineData("echo HEAD^", "denies '^'")]
    [InlineData("echo =sh", "denies '='")]
    [InlineData("echo foo#bar", "denies '#'")]
    [InlineData("l* -l", "denies '*'")]
    [InlineData("echo \"\\$HOME\"", "backslash inside double quotes")]
    [InlineData("echo \"\\\\\"", "backslash inside double quotes")]
    [InlineData("grep \\\"x; rm -rf build; echo \\\" README.md", "escaped quotes")]
    [InlineData("echo 'unterminated", "unbalanced")]
    [InlineData("echo \"unterminated", "unbalanced")]
    [InlineData("FOO=bar ls", "variable assignment prefix")]
    [InlineData("FOO+=bar ls", "variable assignment prefix")]
    [InlineData("ls &&", "empty command position")]
    [InlineData("&& ls", "empty command position")]
    [InlineData("ls ;; pwd", "empty command position")]
    [InlineData("ls | | wc", "empty command position")]
    [InlineData("if test -d x; then rm --force x; fi", "keyword 'if'")]
    public void Lower_UnclassifiableScript_IsOpaqueWithReason(string script, string expectedReason)
    {
        var lowered = Lower(script);

        Assert.Null(lowered.PlainCommands);
        Assert.Contains(expectedReason, lowered.PlainRejectReason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n  ")]
    public void Lower_EmptyScript_IsOpaqueWithoutLiteralCommands(string script)
    {
        var lowered = Lower(script);

        Assert.Null(lowered.PlainCommands);
        Assert.Equal("requires a command that can be classified.", lowered.PlainRejectReason);
        Assert.Empty(lowered.LiteralCommands);
    }

    [Fact]
    public void Lower_ConditionalScript_ExtractsTheGuardedCommands()
    {
        var lowered = Lower("if test -d x; then rm --force x; fi");

        AssertCommands([["test", "-d", "x"], ["rm", "--force", "x"]], lowered.LiteralCommands);
    }

    [Fact]
    public void Lower_RedirectedCommand_DropsTheUnresolvedWordsAndTheTarget()
    {
        var lowered = Lower("rm -rf \"$TARGET\" >/dev/null");

        AssertCommands([["rm", "-rf"]], lowered.LiteralCommands);
    }

    [Fact]
    public void Lower_CommandSubstitution_ExtractsTheSubstitutedCommand()
    {
        var lowered = Lower("echo \"$(rm -rf /tmp/example)\"");

        AssertCommands([["echo"], ["rm", "-rf", "/tmp/example"]], lowered.LiteralCommands);
    }

    [Fact]
    public void Lower_CommandNameFromVariable_ExtractsNothing()
    {
        var lowered = Lower("cmd=rm; $cmd -rf /tmp/example");

        Assert.Empty(lowered.LiteralCommands);
    }

    [Fact]
    public void Lower_AppendAssignmentPrefix_ExtractsTheCommand()
    {
        var lowered = Lower("TARGET+=build rm -rf build");

        AssertCommands([["rm", "-rf", "build"]], lowered.LiteralCommands);
    }

    [Theory]
    [InlineData("echo 'rm -rf /tmp/example'", "echo", "rm -rf /tmp/example")]
    [InlineData("trap 'rm -rf /tmp/example' EXIT", "trap", "rm -rf /tmp/example", "EXIT")]
    [InlineData("bash -c 'rm -rf /tmp/example'", "bash", "-c", "rm -rf /tmp/example")]
    public void Lower_QuotedScriptArgument_StaysOneWord(string script, params string[] expected)
    {
        var lowered = Lower(script);

        Assert.Equal(expected, Assert.Single(lowered.LiteralCommands));
    }

    private static LoweredScript Lower(string script) => new PosixScriptLowerer().Lower(script);

    private static void AssertCommands(string[][] expected, IReadOnlyList<IReadOnlyList<string>>? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Length, actual.Count);
        for (var index = 0; index < expected.Length; index++)
            Assert.Equal(expected[index], actual[index]);
    }
}
