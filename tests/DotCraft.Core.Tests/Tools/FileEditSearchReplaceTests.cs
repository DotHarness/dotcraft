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
    public void Apply_WhitespaceNormalized_CollapsesSpacesInLines()
    {
        var content = "x\na  b  c\ny\n";
        var (ok, newContent, error, kind, _, _, _) = DotCraft.Tools.FileEditSearchReplace.Apply(content, "a b c", "A B C");
        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal("whitespace-normalized fallback", kind);
        Assert.Equal("x\nA B C\ny\n", newContent);
    }

    [Fact]
    public void Apply_UnicodeNormalized_MatchesEnDashWithAsciiHyphen()
    {
        // File uses EN DASH (U+2013); model sends ASCII hyphen in oldText
        var fileLine = "import foo  # comment \u2013 note\n";
        var content = "header\n" + fileLine + "footer\n";
        var oldText = "import foo  # comment - note";
        var (ok, newContent, error, kind, _, _, _) = DotCraft.Tools.FileEditSearchReplace.Apply(content, oldText, "IMPORT");
        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal("unicode-normalized fallback", kind);
        Assert.Contains("IMPORT", newContent, StringComparison.Ordinal);
        Assert.DoesNotContain("\u2013", newContent, StringComparison.Ordinal);
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
    public void Apply_ReplaceAll_ReplacesEveryExactMatch()
    {
        var content = "hi\nhi\n";
        var (ok, newContent, error, kind, _, _, replaceCount) =
            DotCraft.Tools.FileEditSearchReplace.Apply(content, "hi", "x", replaceAll: true);
        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal("replace all", kind);
        Assert.Equal(2, replaceCount);
        Assert.Equal("x\nx\n", newContent);
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
