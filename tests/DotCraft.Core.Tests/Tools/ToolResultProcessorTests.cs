using DotCraft.Tools;
using Xunit;

namespace DotCraft.Core.Tests.Tools;

public class ToolResultProcessorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("   \n\t  ")]
    [InlineData("(no output)")]
    public void Process_EmptyOutput_ReturnsEmptyMessage(string? output)
    {
        var r = ToolResultProcessor.Process("Exec", output, 1000, "C:\\w", "C:\\w\\.craft", "s1", 40);
        Assert.Equal("(Exec completed with no output)", r);
    }

    [Fact]
    public void Process_UnderLimitOrUnlimited_ReturnsOriginalString()
    {
        var small = new string('a', 100);
        var large = new string('x', 500_000);
        Assert.Same(small, ToolResultProcessor.Process("Exec", small, 200, "C:\\w", "C:\\w\\.craft", "s1", 40));
        Assert.Same(large, ToolResultProcessor.Process("Exec", large, 0, "C:\\w", "C:\\w\\.craft", "s1", 40));
    }

    [Fact]
    public void Process_OverLimit_WritesSpillAndReturnsPreview()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "dotcraft-trp-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(workspace);
            var text = string.Join("\n", Enumerable.Range(0, 200).Select(_ => new string('z', 24)));
            var dataPath = Path.Combine(workspace, ".craft");
            var r = ToolResultProcessor.Process("GrepFiles", text, 1000, workspace, dataPath, "thread-1", 2) as string;
            Assert.NotNull(r);
            Assert.Contains(ToolResultProcessor.SpillPreviewMarker, r, StringComparison.Ordinal);

            var spillDir = Path.Combine(workspace, ".craft", "tool-results", "thread-1");
            Assert.True(Directory.Exists(spillDir));
            var files = Directory.GetFiles(spillDir, "GrepFiles_call-*.txt");
            Assert.Single(files);
            Assert.Equal(text, File.ReadAllText(files[0]));
        }
        finally
        {
            try { Directory.Delete(workspace, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Process_OverLimit_FewLines_CapsPreviewBelowTheSpillLimit()
    {
        const int limit = 30_000;
        var workspace = Path.Combine(Path.GetTempPath(), "dotcraft-trp-short-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(workspace);
            var text = new string('a', 50_000) + "\n" + new string('b', 10);
            var dataPath = Path.Combine(workspace, ".craft");
            var r = ToolResultProcessor.Process("Exec", text, limit, workspace, dataPath, "s1", 40) as string;
            Assert.NotNull(r);
            Assert.Contains("full output at:", r, StringComparison.OrdinalIgnoreCase);
            Assert.True(r.Length <= ToolResultProcessor.SpillPreviewMaxChars + 200, $"Preview length {r.Length} should not far exceed the preview cap + footer.");

            var spillDir = Path.Combine(workspace, ".craft", "tool-results", "s1");
            var files = Directory.GetFiles(spillDir, "Exec_call-*.txt");
            Assert.Single(files);
            Assert.Equal(text, File.ReadAllText(files[0]));
        }
        finally
        {
            try { Directory.Delete(workspace, recursive: true); } catch { }
        }
    }

    [Fact]
    public void BuildPreview_ManyLongLines_CapsHeadAndTailExcerpts()
    {
        const int maxPreviewChars = 1000;
        const string spillPath = ".craft/tool-results/s/file.txt";
        var longLine = new string('x', 800);
        var text = string.Join("\n", Enumerable.Range(0, 50).Select(_ => longLine));
        var p = ToolResultProcessor.BuildPreview(text, previewLines: 2, spillRelativePath: spillPath, maxPreviewChars: maxPreviewChars);

        Assert.Contains(ToolResultProcessor.SpillPreviewMarker, p, StringComparison.Ordinal);
        Assert.Contains(spillPath, p, StringComparison.Ordinal);

        const string headSep = "\n\n... (";
        var headEnd = p.IndexOf(headSep, StringComparison.Ordinal);
        Assert.True(headEnd >= 0);
        var headPart = p[..headEnd];

        const string tailSep = ") ...\n\n";
        var tailStart = p.IndexOf(tailSep, StringComparison.Ordinal);
        Assert.True(tailStart >= 0);
        var tailPart = p[(tailStart + tailSep.Length)..];

        Assert.True(headPart.Length + tailPart.Length <= maxPreviewChars,
            $"head ({headPart.Length}) + tail ({tailPart.Length}) should be <= {maxPreviewChars}.");
    }

    [Fact]
    public void BuildPreview_MaxPreviewCharsUnlimited_KeepsFullHeadAndTailExcerpts()
    {
        const string spillPath = ".craft/tool-results/s/file.txt";
        var longLine = new string('y', 400);
        var text = string.Join("\n", Enumerable.Range(0, 30).Select(_ => longLine));
        var p = ToolResultProcessor.BuildPreview(text, previewLines: 2, spillRelativePath: spillPath, maxPreviewChars: int.MaxValue);

        const string headSep = "\n\n... (";
        var headEnd = p.IndexOf(headSep, StringComparison.Ordinal);
        Assert.True(headEnd >= 0);
        var headPart = p[..headEnd];

        const string tailSep = ") ...\n\n";
        var tailStart = p.IndexOf(tailSep, StringComparison.Ordinal);
        Assert.True(tailStart >= 0);
        var tailPart = p[(tailStart + tailSep.Length)..];

        var expectedHead = string.Join("\n", new[] { longLine, longLine });
        var expectedTail = string.Join("\n", new[] { longLine, longLine });
        Assert.Equal(expectedHead, headPart);
        Assert.Equal(expectedTail, tailPart);
    }

    [Fact]
    public void SpillToDisk_WithCallId_IsDeterministicAndIdempotent()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "dotcraft-trp-idempotent-" + Guid.NewGuid().ToString("N"));
        try
        {
            var text = string.Join("\\n", Enumerable.Range(0, 100).Select(i => $"line-{i}"));
            var dataPath = Path.Combine(workspace, ".craft");
            var first = ToolResultProcessor.SpillToDisk(text, workspace, dataPath, "thread/one", "GrepFiles", "call/one");
            var second = ToolResultProcessor.SpillToDisk(text, workspace, dataPath, "thread/one", "GrepFiles", "call/one");

            Assert.Equal(first, second);
            var directory = Path.Combine(workspace, ".craft", "tool-results", ThreadArtifactPathResolver.GetCanonicalThreadSegment("thread/one"));
            var files = Directory.GetFiles(directory);
            Assert.Single(files);
            Assert.Equal(text, File.ReadAllText(files[0]));
        }
        finally
        {
            try { Directory.Delete(workspace, recursive: true); } catch { }
        }
    }

    [Fact]
    public void CanonicalThreadSegment_DoesNotCollideForSanitizedIds()
    {
        var first = ThreadArtifactPathResolver.GetCanonicalThreadSegment("a/b");
        var second = ThreadArtifactPathResolver.GetCanonicalThreadSegment("a\\b");

        Assert.StartsWith("thread-", first, StringComparison.Ordinal);
        Assert.StartsWith("thread-", second, StringComparison.Ordinal);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void CleanupThreadArtifacts_IsIdempotent()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "dotcraft-trp-cleanup-" + Guid.NewGuid().ToString("N"));
        try
        {
            var dataPath = Path.Combine(workspace, ".craft");
            _ = ToolResultProcessor.SpillToDisk("large output", workspace, dataPath, "thread-cleanup", "Exec", "call-cleanup");
            var first = ToolResultProcessor.CleanupThreadArtifacts(workspace, dataPath, "thread-cleanup");
            var second = ToolResultProcessor.CleanupThreadArtifacts(workspace, dataPath, "thread-cleanup");

            Assert.Equal(1, first.DirectoriesDeleted);
            Assert.Equal(0, first.Errors);
            Assert.Equal(ArtifactCleanupResult.Empty, second);
        }
        finally
        {
            try { Directory.Delete(workspace, recursive: true); } catch { }
        }
    }
}
