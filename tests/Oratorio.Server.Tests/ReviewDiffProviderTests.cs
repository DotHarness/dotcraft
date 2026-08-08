using Microsoft.Extensions.Logging.Abstractions;
using Oratorio.Server.Data;
using Oratorio.Server.GitHub;
using Oratorio.Server.Services;

namespace Oratorio.Server.Tests;

public sealed class ReviewDiffProviderTests
{
    [Fact]
    public void BuildAnchorsFromPatch_TracksAddedDeletedAndContextLines()
    {
        var anchors = ReviewDiffProvider.BuildAnchorsFromPatch("""
@@ -10,3 +10,4 @@ public sealed class Example
 context before
-deleted value
+added value
+second added value
 context after
""");

        Assert.True(anchors.Has("LEFT", 10));
        Assert.True(anchors.Has("RIGHT", 10));
        Assert.True(anchors.Has("LEFT", 11));
        Assert.True(anchors.Has("RIGHT", 11));
        Assert.True(anchors.Has("RIGHT", 12));
        Assert.True(anchors.Has("LEFT", 12));
        Assert.True(anchors.Has("RIGHT", 13));
        Assert.False(anchors.Has("LEFT", 13));
    }

    [Fact]
    public void FindRightTextMatches_ResolvesWholeMultiLinePythonCall()
    {
        var anchors = ReviewDiffProvider.BuildAnchorsFromPatch("""
@@ -81,7 +81,7 @@ jobs:
         if package_version and package_version != version:
-            print(
-                f"::warning title=Version mismatch::package.json is {package_version}, "
-            )
+            print(
+                f"::warning title=Version mismatch::package.json is {package_version}, "
+            )
         emit_release_notes()
""");

        var matches = anchors.FindRightTextMatches("""
            print(
                f"::warning title=Version mismatch::package.json is {package_version}, "
            )
""");

        var match = Assert.Single(matches);
        Assert.Equal(82, match.StartLine);
        Assert.Equal(84, match.EndLine);
    }

    [Fact]
    public async Task BuildAnchorMapAsync_PreservesRenamePreviousPathAsChanged()
    {
        var gitHub = new FakeGitHubApiClient();
        gitHub.PullRequestFiles.Clear();
        gitHub.PullRequestFiles.Add(new GitHubChangedFile(
            "src/NewName.cs",
            "renamed",
            1,
            1,
            2,
            """
@@ -4,2 +4,2 @@ public sealed class NewName
-old value
+new value
 context
""",
            "src/OldName.cs"));
        var provider = new ReviewDiffProvider(gitHub, new FakeReviewLocalDiffProvider(), NullLogger<ReviewDiffProvider>.Instance);

        var map = await provider.BuildAnchorMapAsync(
            new OratorioRun(),
            new GitHubRepositoryRef("dotcraft", "oratorio"),
            184,
            ["src/NewName.cs", "src/OldName.cs"],
            CancellationToken.None);

        Assert.True(map.ContainsChangedPath("src/NewName.cs"));
        Assert.True(map.ContainsChangedPath("src/OldName.cs"));
        Assert.True(map.TryGetAnchors("src/NewName.cs", out var anchors));
        Assert.True(anchors.Has("LEFT", 4));
        Assert.True(anchors.Has("RIGHT", 4));
    }

    [Fact]
    public async Task BuildAnchorMapAsync_UsesLocalPatchWhenGitHubPatchIsMissing()
    {
        var gitHub = new FakeGitHubApiClient();
        gitHub.PullRequestFiles.Clear();
        gitHub.PullRequestFiles.Add(new GitHubChangedFile("src/MissingPatch.cs", "modified", 1, 0));
        var localDiff = new FakeReviewLocalDiffProvider(new Dictionary<string, string>
        {
            ["src/MissingPatch.cs"] = """
@@ -8,2 +8,3 @@ public sealed class MissingPatch
 context
+added locally
 tail
"""
        });
        var provider = new ReviewDiffProvider(gitHub, localDiff, NullLogger<ReviewDiffProvider>.Instance);

        var map = await provider.BuildAnchorMapAsync(
            new OratorioRun(),
            new GitHubRepositoryRef("dotcraft", "oratorio"),
            184,
            ["src/MissingPatch.cs"],
            CancellationToken.None);

        Assert.Equal(["src/MissingPatch.cs"], localDiff.RequestedPaths);
        Assert.True(map.TryGetAnchors("src/MissingPatch.cs", out var anchors));
        Assert.True(anchors.Has("RIGHT", 9));
    }

    [Fact]
    public async Task BuildAnchorMapAsync_KeepsChangedFileButNoAnchorsWhenFallbackIsUnavailable()
    {
        var gitHub = new FakeGitHubApiClient();
        gitHub.PullRequestFiles.Clear();
        gitHub.PullRequestFiles.Add(new GitHubChangedFile("src/MissingPatch.cs", "modified", 1, 0));
        var localDiff = new FakeReviewLocalDiffProvider();
        var provider = new ReviewDiffProvider(gitHub, localDiff, NullLogger<ReviewDiffProvider>.Instance);

        var map = await provider.BuildAnchorMapAsync(
            new OratorioRun(),
            new GitHubRepositoryRef("dotcraft", "oratorio"),
            184,
            ["src/MissingPatch.cs"],
            CancellationToken.None);

        Assert.True(map.ContainsChangedPath("src/MissingPatch.cs"));
        Assert.True(map.IsPatchUnavailable("src/MissingPatch.cs"));
        Assert.False(map.TryGetAnchors("src/MissingPatch.cs", out _));
    }
}
