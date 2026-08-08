using System.Diagnostics;
using Oratorio.Server.Data;
using Oratorio.Server.GitHub;

namespace Oratorio.Server.Services;

public interface IReviewDiffProvider
{
    Task<ReviewDiffAnchorMap> BuildAnchorMapAsync(
        OratorioRun run,
        GitHubRepositoryRef repository,
        int number,
        IReadOnlyList<string> requestedPaths,
        CancellationToken ct);
}

public interface IReviewLocalDiffProvider
{
    Task<string?> GetFilePatchAsync(OratorioRun run, string path, CancellationToken ct);
}

public sealed class ReviewDiffProvider(
    IGitHubApiClient gitHub,
    IReviewLocalDiffProvider localDiffs,
    ILogger<ReviewDiffProvider> logger) : IReviewDiffProvider
{
    public const int GitHubPullRequestFilesLimit = 3000;

    public async Task<ReviewDiffAnchorMap> BuildAnchorMapAsync(
        OratorioRun run,
        GitHubRepositoryRef repository,
        int number,
        IReadOnlyList<string> requestedPaths,
        CancellationToken ct)
    {
        var changedFiles = await gitHub.ListPullRequestFilesAsync(repository, number, ct);
        var builder = new ReviewDiffAnchorMapBuilder();
        var missingPatchPaths = new List<string>();

        foreach (var changedFile in changedFiles)
        {
            builder.MarkChanged(changedFile.Filename);
            if (!string.IsNullOrWhiteSpace(changedFile.PreviousFilename))
            {
                builder.MarkChanged(changedFile.PreviousFilename);
            }

            if (string.IsNullOrWhiteSpace(changedFile.Patch))
            {
                missingPatchPaths.Add(changedFile.Filename);
                continue;
            }

            builder.AddPatch(changedFile.Filename, changedFile.Patch);
        }

        if (changedFiles.Count >= GitHubPullRequestFilesLimit)
        {
            builder.AddDiagnostic("reviewDiffTooLarge: GitHub returned the maximum 3000 changed files; files outside API coverage are only commentable when local git diff fallback can resolve them.");
        }

        var fallbackPaths = missingPatchPaths
            .Concat(changedFiles.Count >= GitHubPullRequestFilesLimit
                ? requestedPaths.Where(path => !builder.ContainsChangedPath(path))
                : [])
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var path in fallbackPaths)
        {
            var patch = await localDiffs.GetFilePatchAsync(run, path, ct);
            if (string.IsNullOrWhiteSpace(patch))
            {
                logger.LogDebug("No local diff fallback was available for PR file {Path}.", path);
                builder.MarkPatchUnavailable(path);
                continue;
            }

            builder.MarkChanged(path);
            builder.AddPatch(path, patch);
        }

        return builder.Build();
    }

    public static ReviewDiffFileAnchors BuildAnchorsFromPatch(string patch)
    {
        var anchors = new ReviewDiffFileAnchors();
        ParsePatchIntoAnchors(patch, anchors);
        return anchors;
    }

    private static void ParsePatchIntoAnchors(string patch, ReviewDiffFileAnchors anchors)
    {
        var left = 0;
        var right = 0;
        var inHunk = false;
        foreach (var line in patch.Split('\n'))
        {
            var value = line.TrimEnd('\r');
            if (value.StartsWith("@@ ", StringComparison.Ordinal))
            {
                ParseHunk(value, out var oldStart, out var newStart);
                left = oldStart;
                right = newStart;
                inHunk = true;
                continue;
            }

            if (!inHunk || value.StartsWith("\\", StringComparison.Ordinal))
            {
                continue;
            }

            if (value.StartsWith("+", StringComparison.Ordinal))
            {
                anchors.AddRight(right, value[1..], null, right);
                right++;
            }
            else if (value.StartsWith("-", StringComparison.Ordinal))
            {
                anchors.AddLeft(left, value[1..], left, null);
                left++;
            }
            else
            {
                var text = value.Length == 0 ? "" : value[1..];
                anchors.AddLeft(left, text, left, right);
                anchors.AddRight(right, text, left, right);
                left++;
                right++;
            }
        }
    }

    private static void ParseHunk(string hunk, out int oldStart, out int newStart)
    {
        oldStart = 0;
        newStart = 0;
        var parts = hunk.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part.StartsWith("-", StringComparison.Ordinal))
            {
                oldStart = ParseStart(part);
            }
            else if (part.StartsWith("+", StringComparison.Ordinal))
            {
                newStart = ParseStart(part);
            }
        }
    }

    private static int ParseStart(string part)
    {
        var value = part[1..];
        var comma = value.IndexOf(',');
        if (comma >= 0)
        {
            value = value[..comma];
        }

        return int.TryParse(value, out var parsed) ? parsed : 0;
    }

    private sealed class ReviewDiffAnchorMapBuilder
    {
        private readonly HashSet<string> _changedPaths = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ReviewDiffFileAnchors> _files = new(StringComparer.Ordinal);
        private readonly List<string> _diagnostics = [];
        private readonly HashSet<string> _patchUnavailablePaths = new(StringComparer.Ordinal);

        public void MarkChanged(string path)
        {
            _changedPaths.Add(path);
        }

        public bool ContainsChangedPath(string path) => _changedPaths.Contains(path);

        public void AddPatch(string path, string patch)
        {
            var anchors = _files.TryGetValue(path, out var existing) ? existing : new ReviewDiffFileAnchors();
            ParsePatchIntoAnchors(patch, anchors);
            _files[path] = anchors;
        }

        public void AddDiagnostic(string warning)
        {
            _diagnostics.Add(warning);
        }

        public void MarkPatchUnavailable(string path)
        {
            _patchUnavailablePaths.Add(path);
        }

        public ReviewDiffAnchorMap Build() => new(_changedPaths, _files, _diagnostics, _patchUnavailablePaths);
    }
}

public sealed class GitReviewLocalDiffProvider(ILogger<GitReviewLocalDiffProvider> logger) : IReviewLocalDiffProvider
{
    private static readonly TimeSpan GitCommandTimeout = TimeSpan.FromSeconds(30);

    public async Task<string?> GetFilePatchAsync(OratorioRun run, string path, CancellationToken ct)
    {
        var workspace = FirstExistingDirectory(run.WorktreePath, run.BaseWorkspacePath);
        if (workspace is null)
        {
            return null;
        }

        var headRef = FirstNonEmpty(run.Item?.HeadSha, run.BaseSha, "HEAD");
        var baseRef = await ResolveBaseRefAsync(workspace, run, headRef, ct);
        if (string.IsNullOrWhiteSpace(baseRef))
        {
            return null;
        }

        var result = await RunGitAsync(workspace, ["diff", "--no-ext-diff", $"{baseRef}...{headRef}", "--", path], ct);
        if (result.ExitCode != 0)
        {
            logger.LogDebug("Local git diff fallback failed for {Path}: {Error}", path, result.Stderr.Trim());
            return null;
        }

        return string.IsNullOrWhiteSpace(result.Stdout) ? null : result.Stdout;
    }

    private static string? FirstExistingDirectory(params string?[] paths) =>
        paths.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path));

    private static string FirstNonEmpty(params string?[] values) =>
        values.First(value => !string.IsNullOrWhiteSpace(value))!;

    private async Task<string?> ResolveBaseRefAsync(string workspace, OratorioRun run, string headRef, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(run.BaseSha) &&
            !string.Equals(run.BaseSha, headRef, StringComparison.OrdinalIgnoreCase))
        {
            return run.BaseSha;
        }

        if (!string.IsNullOrWhiteSpace(run.BaseRef) &&
            !string.Equals(run.BaseRef, headRef, StringComparison.OrdinalIgnoreCase))
        {
            return run.BaseRef;
        }

        foreach (var candidate in new[] { "origin/HEAD", "origin/main", "origin/master" })
        {
            var result = await RunGitAsync(workspace, ["merge-base", candidate, headRef], ct);
            if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Stdout))
            {
                return result.Stdout.Trim();
            }
        }

        return null;
    }

    private async Task<GitCommandResult> RunGitAsync(string workspace, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(GitCommandTimeout);

        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workspace,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return new GitCommandResult(1, "", "Unable to start git process.");
        }

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            TryKill(process);
            logger.LogDebug("Local git command timed out in {Workspace}: git {Arguments}", workspace, string.Join(' ', arguments));
            return new GitCommandResult(1, "", "git command timed out.");
        }

        return new GitCommandResult(process.ExitCode, await stdout, await stderr);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private sealed record GitCommandResult(int ExitCode, string Stdout, string Stderr);
}

public sealed class ReviewDiffAnchorMap
{
    private readonly HashSet<string> _changedPaths;
    private readonly Dictionary<string, ReviewDiffFileAnchors> _files;
    private readonly HashSet<string> _patchUnavailablePaths;

    internal ReviewDiffAnchorMap(
        IEnumerable<string> changedPaths,
        IReadOnlyDictionary<string, ReviewDiffFileAnchors> files,
        IReadOnlyList<string> diagnostics,
        IEnumerable<string>? patchUnavailablePaths = null)
    {
        _changedPaths = new HashSet<string>(changedPaths, StringComparer.Ordinal);
        _files = new Dictionary<string, ReviewDiffFileAnchors>(files, StringComparer.Ordinal);
        _patchUnavailablePaths = new HashSet<string>(patchUnavailablePaths ?? [], StringComparer.Ordinal);
        Diagnostics = diagnostics.ToArray();
    }

    public IReadOnlyList<string> Diagnostics { get; }

    public bool ContainsChangedPath(string path) => _changedPaths.Contains(path);

    public bool TryGetAnchors(string path, out ReviewDiffFileAnchors anchors) => _files.TryGetValue(path, out anchors!);

    public bool IsPatchUnavailable(string path) => _patchUnavailablePaths.Contains(path);
}

public sealed class ReviewDiffFileAnchors
{
    private readonly HashSet<int> _left = [];
    private readonly HashSet<int> _right = [];
    private readonly Dictionary<int, ReviewDiffLinePosition> _leftPositions = [];
    private readonly Dictionary<int, ReviewDiffLinePosition> _rightPositions = [];
    private readonly Dictionary<int, string> _rightText = [];

    public IReadOnlySet<int> Left => _left;
    public IReadOnlySet<int> Right => _right;

    internal void AddLeft(int line, string text, int? oldLine = null, int? newLine = null)
    {
        if (line > 0)
        {
            _left.Add(line);
            _leftPositions[line] = new ReviewDiffLinePosition(oldLine ?? line, newLine);
        }
    }

    internal void AddRight(int line, string text, int? oldLine = null, int? newLine = null)
    {
        if (line > 0)
        {
            _right.Add(line);
            _rightPositions[line] = new ReviewDiffLinePosition(oldLine, newLine ?? line);
            _rightText[line] = text;
        }
    }

    public bool Has(string side, int line) => side == "LEFT" ? _left.Contains(line) : _right.Contains(line);

    public bool TryGetPosition(string side, int line, out ReviewDiffLinePosition position)
    {
        var positions = side == "LEFT" ? _leftPositions : _rightPositions;
        if (positions.TryGetValue(line, out var found))
        {
            position = found;
            return true;
        }

        position = new ReviewDiffLinePosition(null, null);
        return false;
    }

    public bool TryGetRightTextRange(int startLine, int endLine, out string text)
    {
        text = "";
        if (startLine <= 0 || endLine < startLine)
        {
            return false;
        }

        var lines = new List<string>();
        for (var line = startLine; line <= endLine; line++)
        {
            if (!_rightText.TryGetValue(line, out var value))
            {
                return false;
            }

            lines.Add(value);
        }

        text = string.Join("\n", lines);
        return true;
    }

    public IReadOnlyList<ReviewDiffTextMatch> FindRightTextMatches(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        if (lines.Length == 0)
        {
            return [];
        }

        var rightLines = _rightText
            .OrderBy(x => x.Key)
            .Select(x => (Line: x.Key, Text: x.Value))
            .ToArray();
        var matches = new List<ReviewDiffTextMatch>();
        for (var index = 0; index < rightLines.Length; index++)
        {
            if (rightLines[index].Text != lines[0])
            {
                continue;
            }

            var startLine = rightLines[index].Line;
            var matched = true;
            for (var offset = 1; offset < lines.Length; offset++)
            {
                var nextIndex = index + offset;
                if (nextIndex >= rightLines.Length ||
                    rightLines[nextIndex].Line != startLine + offset ||
                    rightLines[nextIndex].Text != lines[offset])
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                matches.Add(new ReviewDiffTextMatch(startLine, startLine + lines.Length - 1));
            }
        }

        return matches;
    }
}

public sealed record ReviewDiffLinePosition(int? OldLine, int? NewLine);

public sealed record ReviewDiffTextMatch(int StartLine, int EndLine);
