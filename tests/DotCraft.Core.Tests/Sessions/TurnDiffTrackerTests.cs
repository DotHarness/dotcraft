using DotCraft.Sessions;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Core.Tests.Sessions;

public sealed class TurnDiffTrackerTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "turn-diff-tracker");

    private readonly TurnDiffTracker _tracker = new();

    [Fact]
    public void AddThenUpdate_AccumulatesAsSingleAdd()
    {
        _tracker.Track(Add("a.txt", "foo\n"));
        _tracker.Track(Update("a.txt", "foo\n", "foo\nbar\n"));

        Assert.Equal(Lines(
            "diff --git a/a.txt b/a.txt",
            "new file mode 100644",
            $"index {GitBlobOid.Zero}..{GitBlobOid.Compute("foo\nbar\n")}",
            "--- /dev/null",
            "+++ b/a.txt",
            "@@ -0,0 +1,2 @@",
            "+foo",
            "+bar"), TakeSnapshot());
    }

    [Fact]
    public void OverwriteOfExistingFile_BecomesUpdate()
    {
        _tracker.Track(Update("dup.txt", "before\n", "after\n"));

        Assert.Equal(Lines(
            "diff --git a/dup.txt b/dup.txt",
            $"index {GitBlobOid.Compute("before\n")}..{GitBlobOid.Compute("after\n")}",
            "--- a/dup.txt",
            "+++ b/dup.txt",
            "@@ -1 +1 @@",
            "-before",
            "+after"), TakeSnapshot());
    }

    [Fact]
    public void Invalidate_BeforeAnyEmittedDiff_SuppressesTheDiffAndEmitsNothing()
    {
        _tracker.Track(Add("a.txt", "foo\n"));

        _tracker.Invalidate();

        Assert.False(_tracker.TryTakeSnapshot(out _));
    }

    [Fact]
    public void NoOpWrite_EmitsNothing()
    {
        _tracker.Track(Update("same.txt", "same\n", "same\n"));

        Assert.False(_tracker.TryTakeSnapshot(out _));
    }

    [Fact]
    public void UnchangedPaths_ReuseRenderedDiffs()
    {
        _tracker.Track(Add("a.txt", "one\n"));
        Assert.Equal(1, _tracker.RenderedDiffCount);

        _tracker.Track(Add("b.txt", "two\n"));
        Assert.Equal(2, _tracker.RenderedDiffCount);

        TakeSnapshot();
        Assert.False(_tracker.TryTakeSnapshot(out _));
        Assert.Equal(2, _tracker.RenderedDiffCount);
    }

    [Fact]
    public void RepeatedUpdates_OnlyRerenderTheTouchedPath()
    {
        _tracker.Track(Add("stable.txt", "stable\n"));
        _tracker.Track(Add("hot.txt", "value 0\n"));

        for (var value = 1; value <= 40; value++)
            _tracker.Track(Update("hot.txt", $"value {value - 1}\n", $"value {value}\n"));

        Assert.Equal(42, _tracker.RenderedDiffCount);
    }

    [Fact]
    public void EditBackToBaseline_YieldsEmptySnapshot()
    {
        _tracker.Track(Update("a.txt", "x\n", "y\n"));
        Assert.NotEqual("", TakeSnapshot());

        _tracker.Track(Update("a.txt", "y\n", "x\n"));

        Assert.Equal("", TakeSnapshot());
    }

    [Fact]
    public void Snapshots_AreTakenOnceAndIdenticalOnesAreSuppressed()
    {
        _tracker.Track(Add("a.txt", "foo\n"));
        TakeSnapshot();
        Assert.False(_tracker.TryTakeSnapshot(out _));

        _tracker.Track(Update("a.txt", "foo\n", "foo\n"));

        Assert.False(_tracker.TryTakeSnapshot(out _));
    }

    [Fact]
    public void UntouchedTracker_NeverSnapshots()
    {
        Assert.False(_tracker.TryTakeSnapshot(out _));
    }

    [Fact]
    public void Invalidate_AfterAnEmittedDiff_SnapshotsEmptyOnceAndIgnoresLaterEdits()
    {
        _tracker.Track(Add("a.txt", "foo\n"));
        TakeSnapshot();

        _tracker.Invalidate();
        Assert.Equal("", TakeSnapshot());
        _tracker.Invalidate();
        _tracker.Track(Add("b.txt", "bar\n"));

        Assert.False(_tracker.TryTakeSnapshot(out _));
        Assert.Equal(1, _tracker.RenderedDiffCount);
    }

    [Fact]
    public void AggregateOverCap_Invalidates()
    {
        var half = string.Concat(Enumerable.Repeat(new string('x', 99) + "\n", 6_000));
        _tracker.Track(Add("a.txt", half));
        Assert.NotEqual("", TakeSnapshot());

        _tracker.Track(Add("b.txt", half));

        Assert.Equal("", TakeSnapshot());
        _tracker.Track(Update("a.txt", half, "small\n"));
        Assert.False(_tracker.TryTakeSnapshot(out _));
    }

    [Fact]
    public void FileOverCap_Invalidates()
    {
        _tracker.Track(Add("a.txt", "small\n"));
        Assert.NotEqual("", TakeSnapshot());

        _tracker.Track(Add("big.txt", new string('x', UnifiedDiffLimits.Aggregate.MaxDiffChars + 1)));

        Assert.Equal("", TakeSnapshot());
    }

    [Fact]
    public void Files_AreOrderedByDisplayPath()
    {
        _tracker.Track(Add("b.txt", "b\n"));
        _tracker.Track(Add("a.txt", "a\n"));

        var diff = TakeSnapshot();

        Assert.StartsWith("diff --git a/a.txt b/a.txt\n", diff, StringComparison.Ordinal);
        Assert.True(diff.IndexOf("diff --git a/b.txt", StringComparison.Ordinal) > 0);
    }

    [Fact]
    public void PathKeys_FollowPlatformCaseSensitivity()
    {
        _tracker.Track(Add("Case.txt", "one\n"));
        _tracker.Track(Update("case.txt", "one\n", "two\n"));

        var diff = TakeSnapshot();

        Assert.Equal(OperatingSystem.IsWindows() ? 1 : 2, CountFiles(diff));
        Assert.StartsWith("diff --git a/Case.txt b/Case.txt\nnew file mode 100644\n", diff, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConcurrentTracking_KeepsOneFile()
    {
        var writers = Enumerable.Range(0, 8).Select(writer => Task.Run(() =>
        {
            for (var edit = 0; edit < 50; edit++)
                _tracker.Track(Update("hot.txt", "base\n", $"writer {writer} edit {edit}\n"));
        }));

        await Task.WhenAll(writers);

        var diff = TakeSnapshot();
        Assert.Equal(1, CountFiles(diff));
        Assert.Contains("\n-base\n", diff, StringComparison.Ordinal);
    }

    private string TakeSnapshot()
    {
        Assert.True(_tracker.TryTakeSnapshot(out var diff));
        return diff;
    }

    private static FileChangeRecord Add(string path, string after) =>
        new(Path.Combine(Root, path), path, FileChangeKind.Add, null, after);

    private static FileChangeRecord Update(string path, string before, string after) =>
        new(Path.Combine(Root, path), path, FileChangeKind.Update, before, after);

    private static int CountFiles(string diff) =>
        diff.Split("diff --git ", StringSplitOptions.None).Length - 1;

    private static string Lines(params string[] lines) => string.Concat(lines.Select(line => line + "\n"));
}
