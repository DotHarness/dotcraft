using System.Net;
using System.Text.Json;
using DotCraft.Oratorio.GitHub;
using DotCraft.Oratorio.Domain;

namespace DotCraft.Oratorio.Tests;

internal sealed class FakeGitHubApiClient : IGitHubApiClient
{
    public string DefaultBranch { get; set; } = "main";
    public int FailNextGetIssueCount { get; set; }
    public bool FailRepositoryRead { get; set; }
    public int GetRepositoryCallCount { get; private set; }

    public Task<GitHubRepository> GetRepositoryAsync(GitHubRepositoryRef repository, CancellationToken ct)
    {
        GetRepositoryCallCount++;
        if (FailRepositoryRead)
        {
            throw new HttpRequestException("Synthetic repository read failure.");
        }

        return Task.FromResult(new GitHubRepository(DefaultBranch));
    }

    public Task<GitHubIssue> GetIssueAsync(GitHubRepositoryRef repository, int number, CancellationToken ct)
    {
        if (FailNextGetIssueCount > 0)
        {
            FailNextGetIssueCount--;
            throw new HttpRequestException("Synthetic issue read failure.");
        }

        var issues = IssuesByRepository.TryGetValue(repository.FullName, out var source) ? source : Issues;
        return Task.FromResult(issues.Single(x => x.Number == number));
    }

    public List<GitHubIssue> Issues { get; } =
    [
        new(
            1001,
            42,
            "Design comment sync cursor",
            "Bring imported source comments into Oratorio.",
            "open",
            "https://github.example.test/example-owner/oratorio/issues/42",
            new GitHubUser("example-author"),
            [new GitHubLabel("source-sync")],
            [new GitHubUser("example-assignee")],
            DateTimeOffset.Parse("2026-05-01T10:00:00Z"),
            DateTimeOffset.Parse("2026-05-02T10:00:00Z"),
            null,
            null),
        new(
            1002,
            184,
            "This is actually a PR-shaped issue",
            null,
            "open",
            "https://github.example.test/example-owner/oratorio/pull/184",
            new GitHubUser("example-author"),
            [],
            [],
            DateTimeOffset.Parse("2026-05-01T10:00:00Z"),
            DateTimeOffset.Parse("2026-05-02T10:00:00Z"),
            null,
            new GitHubPullMarker("https://github.example.test/example-owner/oratorio/pull/184"))
    ];

    public List<GitHubPullRequest> PullRequests { get; } =
    [
        new(
            2001,
            184,
            "Add JWT middleware and refresh-token flow",
            "Implements auth refresh flow.",
            "open",
            "https://github.example.test/example-owner/oratorio/pull/184",
            new GitHubUser("example-author"),
            [new GitHubLabel("security"), new GitHubLabel("backend")],
            [new GitHubUser("example-assignee")],
            DateTimeOffset.Parse("2026-05-01T11:00:00Z"),
            DateTimeOffset.Parse("2026-05-02T12:00:00Z"),
            null,
            null,
            false,
            new GitHubBranchRef("feature/auth-refresh", "abc123"),
            new GitHubBranchRef("main", "base123"))
    ];

    public List<GitHubIssueCommentWrite> IssueComments { get; } = [];
    public List<GitHubPullRequestReviewWrite> PullRequestReviews { get; } = [];
    public List<GitHubCheckRunWrite> CheckRuns { get; } = [];
    public List<GitHubPullRequestCreateWrite> PullRequestCreates { get; } = [];
    public List<string> WriteOperations { get; } = [];
    public Dictionary<string, List<GitHubIssue>> IssuesByRepository { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<GitHubPullRequest>> PullRequestsByRepository { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, TimeSpan> ListIssueDelays { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> CancelingIssueRepositories { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<DateTimeOffset?>> IssueSinceArguments { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<GitHubListState>> IssueStateArguments { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<GitHubListState>> PullRequestStateArguments { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<GitHubComment> PullRequestIssueComments { get; } =
    [
        new(
            9001,
            "Please use a constant-time comparison helper.",
            "https://github.example.test/example-owner/oratorio/pull/184#issuecomment-9001",
            new GitHubUser("reviewer"),
            DateTimeOffset.Parse("2026-05-02T13:00:00Z"),
            DateTimeOffset.Parse("2026-05-02T13:00:00Z"))
    ];
    public List<GitHubComment> IssueSourceComments { get; } =
    [
        new(
            9002,
            "Issue comment imported from GitHub.",
            "https://github.example.test/example-owner/oratorio/issues/42#issuecomment-9002",
            new GitHubUser("operator"),
            DateTimeOffset.Parse("2026-05-02T14:00:00Z"),
            DateTimeOffset.Parse("2026-05-02T14:00:00Z"))
    ];
    public List<GitHubReview> SourceReviews { get; } =
    [
        new(
            9101,
            "Two suggestions remain.",
            "https://github.example.test/example-owner/oratorio/pull/184#pullrequestreview-9101",
            "CHANGES_REQUESTED",
            new GitHubUser("reviewer"),
            DateTimeOffset.Parse("2026-05-02T15:00:00Z"))
    ];
    public List<GitHubReviewComment> SourceReviewComments { get; } =
    [
        new(
            9201,
            "Consider extracting the token validation branch.",
            "https://github.example.test/example-owner/oratorio/pull/184#discussion_r9201",
            "src/Auth/RefreshTokenStore.cs",
            88,
            88,
            new GitHubUser("reviewer"),
            DateTimeOffset.Parse("2026-05-02T15:05:00Z"),
            DateTimeOffset.Parse("2026-05-02T15:05:00Z"))
    ];
    public List<GitHubChangedFile> PullRequestFiles { get; } =
    [
        new("src/Auth/RefreshTokenStore.cs", "modified", 4, 1, 5, RefreshTokenStorePatch),
        new("src/Auth/JwtMiddleware.cs", "modified", 2, 0, 2, JwtMiddlewarePatch)
    ];
    public Dictionary<string, List<GitHubComment>> RepositoryIssueComments { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<GitHubReviewComment>> RepositoryReviewComments { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> FailingCommentScanRepositories { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<GitHubCommentScanRequest> CommentScanRequests { get; } = [];
    public List<GitHubReactionWrite> Reactions { get; } = [];
    public int FailNextReactionCount { get; set; }
    public int FailNextWriteCount { get; set; }
    public int FailNextPullRequestReviewCount { get; set; }
    public int IssueCommentCallCount { get; private set; }
    public int PullRequestReviewCallCount { get; private set; }
    public int PullRequestReviewCommentCallCount { get; private set; }
    public int FullDiffCallCount { get; private set; }
    public int GetPullRequestCallCount { get; private set; }
    public int FailNextGetPullRequestCount { get; set; }
    public bool FailSourceDetailReads { get; set; }
    public bool FailPullRequestFileReads { get; set; }

    public static readonly string RefreshTokenStorePatch = """
@@ -84,8 +84,9 @@ public sealed class RefreshTokenStore
     public string Refresh(string token)
     {
-        return token;
+        return token;
+        return refreshed;
     }
 }
""";

    public static readonly string JwtMiddlewarePatch = """
@@ -20,4 +20,5 @@ public sealed class JwtMiddleware
     public void Invoke()
     {
+        Validate();
     }
 }
""";

    public static readonly string SyntheticFileSearcherPatch = """
@@ -120,23 +255,23 @@ public sealed class SyntheticFileSearcher
     context line 01
     context line 02
     context line 03
     context line 04
     context line 05
     context line 06
     context line 07
     context line 08
     context line 09
     context line 10
     context line 11
     context line 12
     context line 13
     context line 14
     context line 15
     context line 16
     context line 17
     context line 18
     context line 19
     context line 20
     context line 21
     context line 22
     context line 23
""";

    public async Task<IReadOnlyList<GitHubIssue>> ListIssuesAsync(GitHubRepositoryRef repository, GitHubListState state, DateTimeOffset? since, CancellationToken ct)
    {
        if (ListIssueDelays.TryGetValue(repository.FullName, out var delay))
        {
            await Task.Delay(delay, ct);
        }

        if (CancelingIssueRepositories.Contains(repository.FullName))
        {
            throw new TaskCanceledException($"Synthetic GitHub list timeout for {repository.FullName}.");
        }

        if (!IssueSinceArguments.TryGetValue(repository.FullName, out var sinceArguments))
        {
            sinceArguments = [];
            IssueSinceArguments[repository.FullName] = sinceArguments;
        }

        sinceArguments.Add(since);
        if (!IssueStateArguments.TryGetValue(repository.FullName, out var stateArguments))
        {
            stateArguments = [];
            IssueStateArguments[repository.FullName] = stateArguments;
        }

        stateArguments.Add(state);
        var source = IssuesByRepository.TryGetValue(repository.FullName, out var issues) ? issues : Issues;
        source = FilterIssuesByState(source, state).ToList();
        if (since is not null)
        {
            source = source.Where(x => x.UpdatedAt >= since.Value).ToList();
        }

        return source;
    }

    public Task<IReadOnlyList<GitHubPullRequest>> ListPullRequestsAsync(GitHubRepositoryRef repository, GitHubListState state, CancellationToken ct)
    {
        if (!PullRequestStateArguments.TryGetValue(repository.FullName, out var stateArguments))
        {
            stateArguments = [];
            PullRequestStateArguments[repository.FullName] = stateArguments;
        }

        stateArguments.Add(state);
        var source = PullRequestsByRepository.TryGetValue(repository.FullName, out var pullRequests) ? pullRequests : PullRequests;
        return Task.FromResult<IReadOnlyList<GitHubPullRequest>>(FilterPullRequestsByState(source, state).ToArray());
    }

    public Task<GitHubPullRequest> GetPullRequestAsync(GitHubRepositoryRef repository, int number, CancellationToken ct)
    {
        GetPullRequestCallCount++;
        if (FailNextGetPullRequestCount > 0)
        {
            FailNextGetPullRequestCount--;
            throw new HttpRequestException("Synthetic GitHub pull request read failure.");
        }

        var source = PullRequestsByRepository.TryGetValue(repository.FullName, out var pullRequests) ? pullRequests : PullRequests;
        return Task.FromResult(source.First(x => x.Number == number));
    }

    public Task<IReadOnlyList<GitHubComment>> ListIssueCommentsAsync(GitHubRepositoryRef repository, int number, CancellationToken ct)
    {
        IssueCommentCallCount++;
        if (FailSourceDetailReads)
        {
            throw new HttpRequestException("Synthetic GitHub detail failure.");
        }

        var written = IssueComments
            .Select((comment, index) => (comment, index))
            .Where(x => x.comment.Number == number)
            .Select(x => new GitHubComment(
                100 + x.index,
                x.comment.Body,
                $"https://github.example.test/{repository.FullName}/pull/{number}#issuecomment-{100 + x.index}",
                new GitHubUser(OwnBotLogin, "Bot"),
                DateTimeOffset.Parse("2026-05-03T00:00:00Z"),
                DateTimeOffset.Parse("2026-05-03T00:00:00Z")));
        return Task.FromResult<IReadOnlyList<GitHubComment>>((number == 184 ? PullRequestIssueComments : IssueSourceComments).Concat(written).ToArray());
    }

    public Task<IReadOnlyList<GitHubReview>> ListPullRequestReviewsAsync(GitHubRepositoryRef repository, int number, CancellationToken ct)
    {
        PullRequestReviewCallCount++;
        if (FailSourceDetailReads)
        {
            throw new HttpRequestException("Synthetic GitHub detail failure.");
        }

        return Task.FromResult<IReadOnlyList<GitHubReview>>(SourceReviews);
    }

    public Task<IReadOnlyList<GitHubReviewComment>> ListPullRequestReviewCommentsAsync(GitHubRepositoryRef repository, int number, CancellationToken ct)
    {
        PullRequestReviewCommentCallCount++;
        if (FailSourceDetailReads)
        {
            throw new HttpRequestException("Synthetic GitHub detail failure.");
        }

        return Task.FromResult<IReadOnlyList<GitHubReviewComment>>(SourceReviewComments
            .Concat(PublishedReviewThreads(number).Select(thread => thread.Root))
            .Concat(ReviewCommentReplies.Where(reply => reply.Number == number).Select(ToReviewComment))
            .ToArray());
    }

    public const string OwnAppSlug = "oratorio-app";
    public const string OwnBotLogin = $"{OwnAppSlug}[bot]";

    public Task<string> GetAuthenticatedAppSlugAsync(CancellationToken ct) => Task.FromResult(OwnAppSlug);

    public List<GitHubReviewCommentReplyWrite> ReviewCommentReplies { get; } = [];
    public int LoseNextReviewCommentReplyResponseCount { get; set; }

    public Task<GitHubWriteResponse> CreatePullRequestReviewCommentReplyAsync(GitHubRepositoryRef repository, int number, long commentId, string body, CancellationToken ct)
    {
        MaybeFailWrite();
        var reply = new GitHubReviewCommentReplyWrite(repository, number, commentId, body, 7500 + ReviewCommentReplies.Count);
        ReviewCommentReplies.Add(reply);
        if (LoseNextReviewCommentReplyResponseCount > 0)
        {
            LoseNextReviewCommentReplyResponseCount--;
            throw new HttpRequestException("Synthetic lost GitHub review comment reply response.");
        }

        return Task.FromResult(new GitHubWriteResponse(
            reply.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            $"https://github.example.test/{repository.FullName}/pull/{number}#discussion_r{reply.Id}",
            $$"""{"id":{{reply.Id}}}"""));
    }

    private IReadOnlyList<(string ThreadId, GitHubReviewComment Root)> PublishedReviewThreads(int number)
    {
        var threads = new List<(string, GitHubReviewComment)>();
        var index = 0;
        foreach (var review in PullRequestReviews.Where(r => r.Number == number))
        {
            foreach (var comment in review.Comments)
            {
                threads.Add(($"thread-{number}-{index}", new GitHubReviewComment(
                    7000 + index,
                    comment.Body,
                    $"https://github.example.test/example-owner/oratorio/pull/{number}#discussion_r{7000 + index}",
                    comment.Path,
                    comment.Line,
                    comment.Line,
                    new GitHubUser(OwnBotLogin, "Bot"),
                    DateTimeOffset.Parse("2026-05-03T00:00:00Z"),
                    DateTimeOffset.Parse("2026-05-03T00:00:00Z"))));
                index++;
            }
        }

        return threads;
    }

    private static GitHubReviewComment ToReviewComment(GitHubReviewCommentReplyWrite reply) =>
        new(
            reply.Id,
            reply.Body,
            $"https://github.example.test/{reply.Repository.FullName}/pull/{reply.Number}#discussion_r{reply.Id}",
            "",
            null,
            null,
            new GitHubUser(OwnBotLogin, "Bot"),
            DateTimeOffset.Parse("2026-05-03T00:00:00Z"),
            DateTimeOffset.Parse("2026-05-03T00:00:00Z"),
            InReplyToId: reply.InReplyToId);

    public Task<GitHubConditionalList<GitHubComment>> ListRepositoryIssueCommentsAsync(GitHubRepositoryRef repository, DateTimeOffset since, string? etag, CancellationToken ct) =>
        Task.FromResult(ListRepositoryComments(
            repository,
            "issue-comments",
            since,
            etag,
            RepositoryIssueComments,
            x => x.Id,
            x => x.UpdatedAt));

    public Task<GitHubConditionalList<GitHubReviewComment>> ListRepositoryReviewCommentsAsync(GitHubRepositoryRef repository, DateTimeOffset since, string? etag, CancellationToken ct) =>
        Task.FromResult(ListRepositoryComments(
            repository,
            "review-comments",
            since,
            etag,
            RepositoryReviewComments,
            x => x.Id,
            x => x.UpdatedAt));

    public Task CreateIssueCommentReactionAsync(GitHubRepositoryRef repository, long commentId, string content, CancellationToken ct) =>
        AddReactionAsync(new GitHubReactionWrite(repository, GitHubCommentKind.Conversation, commentId, content));

    public Task CreateReviewCommentReactionAsync(GitHubRepositoryRef repository, long commentId, string content, CancellationToken ct) =>
        AddReactionAsync(new GitHubReactionWrite(repository, GitHubCommentKind.ReviewComment, commentId, content));

    private Task AddReactionAsync(GitHubReactionWrite reaction)
    {
        if (FailNextReactionCount > 0)
        {
            FailNextReactionCount--;
            throw new HttpRequestException("Synthetic GitHub reaction failure.", null, HttpStatusCode.InternalServerError);
        }

        Reactions.Add(reaction);
        return Task.CompletedTask;
    }

    private GitHubConditionalList<T> ListRepositoryComments<T>(
        GitHubRepositoryRef repository,
        string stream,
        DateTimeOffset since,
        string? etag,
        Dictionary<string, List<T>> source,
        Func<T, long> id,
        Func<T, DateTimeOffset> updatedAt)
    {
        if (FailingCommentScanRepositories.Contains(repository.FullName))
        {
            CommentScanRequests.Add(new GitHubCommentScanRequest(repository.FullName, stream, since, etag, false));
            throw new HttpRequestException("Synthetic GitHub comment scan failure.", null, HttpStatusCode.BadGateway);
        }

        var items = (source.TryGetValue(repository.FullName, out var comments) ? comments : [])
            .Where(x => updatedAt(x) >= since)
            .OrderByDescending(updatedAt)
            .ToArray();
        var currentETag = $"\"{string.Join(",", items.Select(x => $"{id(x)}:{updatedAt(x).ToUnixTimeMilliseconds()}"))}\"";
        var notModified = string.Equals(etag, currentETag, StringComparison.Ordinal);
        CommentScanRequests.Add(new GitHubCommentScanRequest(repository.FullName, stream, since, etag, notModified));
        return notModified
            ? new GitHubConditionalList<T>(true, etag, [])
            : new GitHubConditionalList<T>(false, currentETag, items);
    }

    public Task<IReadOnlyList<GitHubChangedFile>> ListPullRequestFilesAsync(GitHubRepositoryRef repository, int number, CancellationToken ct)
    {
        if (FailPullRequestFileReads)
        {
            throw new HttpRequestException("Synthetic GitHub pull request file read failure.", null, HttpStatusCode.InternalServerError);
        }

        return Task.FromResult<IReadOnlyList<GitHubChangedFile>>(PullRequestFiles);
    }

    private static IEnumerable<GitHubIssue> FilterIssuesByState(IEnumerable<GitHubIssue> issues, GitHubListState state) =>
        state switch
        {
            GitHubListState.Open => issues.Where(x => x.State.Equals("open", StringComparison.OrdinalIgnoreCase)),
            GitHubListState.Closed => issues.Where(x => x.State.Equals("closed", StringComparison.OrdinalIgnoreCase)),
            _ => issues
        };

    private static IEnumerable<GitHubPullRequest> FilterPullRequestsByState(IEnumerable<GitHubPullRequest> pullRequests, GitHubListState state) =>
        state switch
        {
            GitHubListState.Open => pullRequests.Where(x => x.State.Equals("open", StringComparison.OrdinalIgnoreCase)),
            GitHubListState.Closed => pullRequests.Where(x => x.State.Equals("closed", StringComparison.OrdinalIgnoreCase)),
            _ => pullRequests
        };

    public Task<string> GetPullRequestDiffAsync(GitHubRepositoryRef repository, int number, CancellationToken ct)
    {
        FullDiffCallCount++;
        return Task.FromResult("""
diff --git a/src/Auth/RefreshTokenStore.cs b/src/Auth/RefreshTokenStore.cs
index 1111111..2222222 100644
--- a/src/Auth/RefreshTokenStore.cs
+++ b/src/Auth/RefreshTokenStore.cs
@@ -84,8 +84,9 @@ public sealed class RefreshTokenStore
     public string Refresh(string token)
     {
-        return token;
+        return token;
+        return refreshed;
     }
 }
diff --git a/src/Auth/JwtMiddleware.cs b/src/Auth/JwtMiddleware.cs
index 3333333..4444444 100644
--- a/src/Auth/JwtMiddleware.cs
+++ b/src/Auth/JwtMiddleware.cs
@@ -20,4 +20,5 @@ public sealed class JwtMiddleware
     public void Invoke()
     {
+        Validate();
     }
 }
""");
    }

    public int LoseNextIssueCommentResponseCount { get; set; }

    public Task<GitHubWriteResponse> CreateIssueCommentAsync(GitHubRepositoryRef repository, int number, string body, CancellationToken ct)
    {
        MaybeFailWrite();
        IssueComments.Add(new GitHubIssueCommentWrite(repository, number, body));
        if (LoseNextIssueCommentResponseCount > 0)
        {
            LoseNextIssueCommentResponseCount--;
            throw new HttpRequestException("Synthetic lost GitHub comment response.");
        }

        return Task.FromResult(new GitHubWriteResponse("issue-comment:100", $"https://github.example.test/{repository.FullName}/issues/{number}#issuecomment-100", """{"id":100}"""));
    }

    public Task<GitHubWriteResponse> CreatePullRequestReviewAsync(GitHubRepositoryRef repository, int number, string @event, string body, string? commitId, CancellationToken ct)
    {
        MaybeFailWrite();
        PullRequestReviews.Add(new GitHubPullRequestReviewWrite(repository, number, @event, body, commitId, []));
        return Task.FromResult(new GitHubWriteResponse("review:200", $"https://github.example.test/{repository.FullName}/pull/{number}#pullrequestreview-200", """{"id":200}"""));
    }

    public Task<GitHubWriteResponse> CreatePullRequestReviewAsync(GitHubRepositoryRef repository, int number, string @event, string body, string? commitId, IReadOnlyList<GitHubPullRequestReviewCommentWrite> comments, CancellationToken ct)
    {
        MaybeFailWrite();
        if (FailNextPullRequestReviewCount > 0)
        {
            FailNextPullRequestReviewCount--;
            throw new HttpRequestException("Synthetic GitHub review failure.");
        }

        PullRequestReviews.Add(new GitHubPullRequestReviewWrite(repository, number, @event, body, commitId, comments));
        return Task.FromResult(new GitHubWriteResponse("review:200", $"https://github.example.test/{repository.FullName}/pull/{number}#pullrequestreview-200", """{"id":200}"""));
    }

    public Task<GitHubWriteResponse> CreateCheckRunAsync(
        GitHubRepositoryRef repository,
        string name,
        string headSha,
        string status,
        string? conclusion,
        string title,
        string summary,
        CancellationToken ct)
    {
        MaybeFailWrite();
        var id = $"check-run:{300 + CheckRuns.Count}";
        CheckRuns.Add(new GitHubCheckRunWrite(repository, name, headSha, status, conclusion, title, summary, id));
        return Task.FromResult(new GitHubWriteResponse(id, $"https://github.example.test/{repository.FullName}/runs/{300 + CheckRuns.Count - 1}", $$"""{"id":"{{id}}"}"""));
    }

    public Task<GitHubWriteResponse> UpdateCheckRunAsync(
        GitHubRepositoryRef repository,
        string checkRunId,
        string status,
        string? conclusion,
        string title,
        string summary,
        CancellationToken ct)
    {
        MaybeFailWrite();
        var check = CheckRuns.FirstOrDefault(x => x.ExternalId == checkRunId)
            ?? throw new InvalidOperationException($"Unknown check run {checkRunId}.");
        check.Status = status;
        check.Conclusion = conclusion;
        check.Title = title;
        check.Summary = summary;
        check.UpdateCount++;
        return Task.FromResult(new GitHubWriteResponse(checkRunId, $"https://github.example.test/{repository.FullName}/runs/{checkRunId}", $$"""{"id":"{{checkRunId}}"}"""));
    }

    public List<GitHubReviewThreadReplyWrite> ReviewThreadReplies { get; } = [];
    public List<(string ThreadId, bool Resolved)> ReviewThreadResolutions { get; } = [];

    public Task<IReadOnlyList<GitHubReviewThread>> ListPullRequestReviewThreadsAsync(GitHubRepositoryRef repository, int number, CancellationToken ct)
    {
        var threads = new List<GitHubReviewThread>();
        foreach (var (threadId, root) in PublishedReviewThreads(number))
        {
            var bodies = new List<string> { root.Body };
            bodies.AddRange(ReviewThreadReplies.Where(reply => reply.ThreadId == threadId).Select(reply => reply.Body));
            bodies.AddRange(ReviewCommentReplies.Where(reply => reply.Number == number && reply.InReplyToId == root.Id).Select(reply => reply.Body));
            threads.Add(new GitHubReviewThread(threadId, false, bodies));
        }

        return Task.FromResult<IReadOnlyList<GitHubReviewThread>>(threads);
    }

    public Task<GitHubWriteResponse> CreatePullRequestReviewThreadReplyAsync(GitHubRepositoryRef repository, string threadId, string body, CancellationToken ct)
    {
        MaybeFailWrite();
        WriteOperations.Add($"reply:{threadId}");
        ReviewThreadReplies.Add(new GitHubReviewThreadReplyWrite(repository, threadId, body));
        var id = $"review-thread-reply:{400 + ReviewThreadReplies.Count - 1}";
        return Task.FromResult(new GitHubWriteResponse(id, $"https://github.example.test/{repository.FullName}/pull/184#discussion_r{400 + ReviewThreadReplies.Count - 1}", $$"""{"id":"{{id}}"}"""));
    }

    public Task<GitHubWriteResponse> ResolveReviewThreadAsync(GitHubRepositoryRef repository, string threadId, bool resolved, CancellationToken ct)
    {
        MaybeFailWrite();
        WriteOperations.Add($"resolve:{threadId}:{resolved}");
        ReviewThreadResolutions.Add((threadId, resolved));
        return Task.FromResult(new GitHubWriteResponse(threadId, null, $$"""{"threadId":"{{threadId}}","resolved":{{(resolved ? "true" : "false")}}}"""));
    }

    public Task<GitHubPullRequestCreateResponse> CreatePullRequestAsync(GitHubRepositoryRef repository, string title, string head, string @base, string body, bool draft, CancellationToken ct)
    {
        MaybeFailWrite();
        PullRequestCreates.Add(new GitHubPullRequestCreateWrite(repository, title, head, @base, body, draft));
        return Task.FromResult(new GitHubPullRequestCreateResponse(
            5001,
            501,
            $"https://github.example.test/{repository.FullName}/pull/501",
            title,
            new GitHubBranchRef(head, "commit-sha-123"),
            new GitHubBranchRef(@base, "base-sha-123")));
    }

    private void MaybeFailWrite()
    {
        if (FailNextWriteCount <= 0)
        {
            return;
        }

        FailNextWriteCount--;
        throw new HttpRequestException("Synthetic GitHub write failure.");
    }
}

internal sealed record GitHubIssueCommentWrite(GitHubRepositoryRef Repository, int Number, string Body);

internal sealed record GitHubCommentScanRequest(string Repository, string Stream, DateTimeOffset Since, string? ETag, bool NotModified);

internal sealed record GitHubReactionWrite(GitHubRepositoryRef Repository, GitHubCommentKind Kind, long CommentId, string Content);

internal sealed record GitHubPullRequestReviewWrite(GitHubRepositoryRef Repository, int Number, string Event, string Body, string? CommitId, IReadOnlyList<GitHubPullRequestReviewCommentWrite> Comments);

internal sealed record GitHubReviewThreadReplyWrite(GitHubRepositoryRef Repository, string ThreadId, string Body);

internal sealed record GitHubReviewCommentReplyWrite(GitHubRepositoryRef Repository, int Number, long InReplyToId, string Body, long Id);

internal sealed class GitHubCheckRunWrite(
    GitHubRepositoryRef repository,
    string name,
    string headSha,
    string status,
    string? conclusion,
    string title,
    string summary,
    string externalId)
{
    public GitHubRepositoryRef Repository { get; } = repository;
    public string Name { get; } = name;
    public string HeadSha { get; } = headSha;
    public string Status { get; set; } = status;
    public string? Conclusion { get; set; } = conclusion;
    public string Title { get; set; } = title;
    public string Summary { get; set; } = summary;
    public string ExternalId { get; } = externalId;
    public int UpdateCount { get; set; }
}

internal sealed record GitHubPullRequestCreateWrite(GitHubRepositoryRef Repository, string Title, string Head, string Base, string Body, bool Draft);
