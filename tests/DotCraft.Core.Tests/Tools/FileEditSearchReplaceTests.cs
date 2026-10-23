using Xunit;

namespace DotCraft.Tests.Tools;

public class FileEditSearchReplaceTests
{
    [Fact]
    public void Apply_ExactMatch_ReplacesOnce()
    {
        var content = "a\nb\nc\n";
        var (ok, newContent, error, kind, line, oldLc, _) = DotCraft.Tools.FileEditSearchReplace.Apply(content, "b", "B");
        Assert.True(ok);
        Assert.Null(error);
        Assert.Null(kind);
        Assert.Equal(2, line);
        Assert.Equal(1, oldLc);
        Assert.Equal("a\nB\nc\n", newContent);
    }

    [Fact]
    public void Apply_MultipleExactMatches_ReturnsError()
    {
        var content = "hi\nhi\n";
        var (ok, _, error, _, _, _, _) = DotCraft.Tools.FileEditSearchReplace.Apply(content, "hi", "x");
        Assert.False(ok);
        Assert.Contains("replaceAll", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Apply_ReplaceAll_NoMatch_ReturnsErrorWithoutFuzzy()
    {
        var content = "a\nb\n";
        var (ok, _, error, _, _, _, _) =
            DotCraft.Tools.FileEditSearchReplace.Apply(content, "missing", "x", replaceAll: true);
        Assert.False(ok);
        Assert.Contains("not found", error, StringComparison.OrdinalIgnoreCase);
    }
}
