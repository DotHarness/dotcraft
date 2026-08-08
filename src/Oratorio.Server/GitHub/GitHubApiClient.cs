using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Oratorio.Server.GitHub;

public interface IGitHubApiClient
{
    Task<IReadOnlyList<GitHubIssue>> ListIssuesAsync(GitHubRepositoryRef repository, GitHubListState state, DateTimeOffset? since, CancellationToken ct);
    Task<IReadOnlyList<GitHubPullRequest>> ListPullRequestsAsync(GitHubRepositoryRef repository, GitHubListState state, CancellationToken ct);
    Task<GitHubPullRequest> GetPullRequestAsync(GitHubRepositoryRef repository, int number, CancellationToken ct);
    Task<IReadOnlyList<GitHubComment>> ListIssueCommentsAsync(GitHubRepositoryRef repository, int number, CancellationToken ct);
    Task<IReadOnlyList<GitHubReview>> ListPullRequestReviewsAsync(GitHubRepositoryRef repository, int number, CancellationToken ct);
    Task<IReadOnlyList<GitHubReviewComment>> ListPullRequestReviewCommentsAsync(GitHubRepositoryRef repository, int number, CancellationToken ct);
    Task<IReadOnlyList<GitHubChangedFile>> ListPullRequestFilesAsync(GitHubRepositoryRef repository, int number, CancellationToken ct);
    Task<string> GetPullRequestDiffAsync(GitHubRepositoryRef repository, int number, CancellationToken ct);
    Task<GitHubWriteResponse> CreateIssueCommentAsync(GitHubRepositoryRef repository, int number, string body, CancellationToken ct);
    Task<GitHubWriteResponse> CreatePullRequestReviewAsync(GitHubRepositoryRef repository, int number, string @event, string body, string? commitId, CancellationToken ct);
    Task<GitHubWriteResponse> CreatePullRequestReviewAsync(GitHubRepositoryRef repository, int number, string @event, string body, string? commitId, IReadOnlyList<GitHubPullRequestReviewCommentWrite> comments, CancellationToken ct);
    Task<GitHubWriteResponse> CreatePullRequestReviewThreadReplyAsync(GitHubRepositoryRef repository, string threadId, string body, CancellationToken ct);
    Task<GitHubWriteResponse> CreateCheckRunAsync(
        GitHubRepositoryRef repository,
        string name,
        string headSha,
        string status,
        string? conclusion,
        string title,
        string summary,
        CancellationToken ct);
    Task<GitHubWriteResponse> UpdateCheckRunAsync(
        GitHubRepositoryRef repository,
        string checkRunId,
        string status,
        string? conclusion,
        string title,
        string summary,
        CancellationToken ct);
    Task<GitHubPullRequestCreateResponse> CreatePullRequestAsync(GitHubRepositoryRef repository, string title, string head, string @base, string body, bool draft, CancellationToken ct);
    Task<IReadOnlyList<GitHubReviewThread>> ListPullRequestReviewThreadsAsync(GitHubRepositoryRef repository, int number, CancellationToken ct);
    Task<GitHubWriteResponse> ResolveReviewThreadAsync(GitHubRepositoryRef repository, string threadId, bool resolved, CancellationToken ct);
}

public sealed class GitHubApiClient(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<GitHubOptions> options,
    IGitHubTokenProvider tokenProvider)
    : IGitHubApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public Task<IReadOnlyList<GitHubIssue>> ListIssuesAsync(GitHubRepositoryRef repository, GitHubListState state, DateTimeOffset? since, CancellationToken ct)
    {
        var path = $"/repos/{repository.Owner}/{repository.Name}/issues?state={StateQueryValue(state)}&per_page=100";
        if (since is not null)
        {
            path += $"&since={Uri.EscapeDataString(since.Value.UtcDateTime.ToString("O"))}";
        }

        return GetPagesAsync<GitHubIssue>(repository, path, ct);
    }

    public Task<IReadOnlyList<GitHubPullRequest>> ListPullRequestsAsync(GitHubRepositoryRef repository, GitHubListState state, CancellationToken ct) =>
        GetPagesAsync<GitHubPullRequest>(repository, $"/repos/{repository.Owner}/{repository.Name}/pulls?state={StateQueryValue(state)}&sort=updated&direction=desc&per_page=100", ct);

    public async Task<GitHubPullRequest> GetPullRequestAsync(GitHubRepositoryRef repository, int number, CancellationToken ct)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get, $"/repos/{repository.Owner}/{repository.Name}/pulls/{number}", repository, ct);
        using var response = await httpClientFactory.CreateClient("GitHub").SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GitHubPullRequest>(JsonOptions, ct)
            ?? throw new InvalidOperationException("GitHub pull request response was empty.");
    }

    public Task<IReadOnlyList<GitHubComment>> ListIssueCommentsAsync(GitHubRepositoryRef repository, int number, CancellationToken ct) =>
        GetPagesAsync<GitHubComment>(repository, $"/repos/{repository.Owner}/{repository.Name}/issues/{number}/comments?per_page=100", ct);

    public Task<IReadOnlyList<GitHubReview>> ListPullRequestReviewsAsync(GitHubRepositoryRef repository, int number, CancellationToken ct) =>
        GetPagesAsync<GitHubReview>(repository, $"/repos/{repository.Owner}/{repository.Name}/pulls/{number}/reviews?per_page=100", ct);

    public Task<IReadOnlyList<GitHubReviewComment>> ListPullRequestReviewCommentsAsync(GitHubRepositoryRef repository, int number, CancellationToken ct) =>
        GetPagesAsync<GitHubReviewComment>(repository, $"/repos/{repository.Owner}/{repository.Name}/pulls/{number}/comments?per_page=100", ct);

    public Task<IReadOnlyList<GitHubChangedFile>> ListPullRequestFilesAsync(GitHubRepositoryRef repository, int number, CancellationToken ct) =>
        GetPagesAsync<GitHubChangedFile>(repository, $"/repos/{repository.Owner}/{repository.Name}/pulls/{number}/files?per_page=100", ct, maxPages: 30);

    public async Task<string> GetPullRequestDiffAsync(GitHubRepositoryRef repository, int number, CancellationToken ct)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get, $"/repos/{repository.Owner}/{repository.Name}/pulls/{number}", repository, ct);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.diff"));
        using var response = await httpClientFactory.CreateClient("GitHub").SendAsync(request, ct);
        var responseText = await response.Content.ReadAsStringAsync(ct);
        response.EnsureSuccessStatusCode();
        return responseText;
    }

    public Task<GitHubWriteResponse> CreateIssueCommentAsync(GitHubRepositoryRef repository, int number, string body, CancellationToken ct) =>
        SendJsonAsync(
            HttpMethod.Post,
            $"/repos/{repository.Owner}/{repository.Name}/issues/{number}/comments",
            new Dictionary<string, object?> { ["body"] = body },
            repository,
            ct);

    public Task<GitHubWriteResponse> CreatePullRequestReviewAsync(GitHubRepositoryRef repository, int number, string @event, string body, string? commitId, CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>
        {
            ["body"] = body,
            ["event"] = @event
        };
        if (!string.IsNullOrWhiteSpace(commitId))
        {
            payload["commit_id"] = commitId;
        }

        return SendJsonAsync(HttpMethod.Post, $"/repos/{repository.Owner}/{repository.Name}/pulls/{number}/reviews", payload, repository, ct);
    }

    public Task<GitHubWriteResponse> CreatePullRequestReviewAsync(GitHubRepositoryRef repository, int number, string @event, string body, string? commitId, IReadOnlyList<GitHubPullRequestReviewCommentWrite> comments, CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>
        {
            ["body"] = body,
            ["event"] = @event
        };
        if (!string.IsNullOrWhiteSpace(commitId))
        {
            payload["commit_id"] = commitId;
        }

        if (comments.Count > 0)
        {
            payload["comments"] = comments.Select(comment =>
            {
                var commentPayload = new Dictionary<string, object?>
                {
                    ["path"] = comment.Path,
                    ["body"] = comment.Body,
                    ["line"] = comment.Line,
                    ["side"] = comment.Side
                };
                if (comment.StartLine.HasValue)
                {
                    commentPayload["start_line"] = comment.StartLine.Value;
                }

                if (!string.IsNullOrWhiteSpace(comment.StartSide))
                {
                    commentPayload["start_side"] = comment.StartSide;
                }

                return commentPayload;
            }).ToArray();
        }

        return SendJsonAsync(HttpMethod.Post, $"/repos/{repository.Owner}/{repository.Name}/pulls/{number}/reviews", payload, repository, ct);
    }

    public async Task<GitHubWriteResponse> CreatePullRequestReviewThreadReplyAsync(GitHubRepositoryRef repository, string threadId, string body, CancellationToken ct)
    {
        const string mutation = """
            mutation($threadId:ID!,$body:String!){
              addPullRequestReviewThreadReply(input:{pullRequestReviewThreadId:$threadId,body:$body}){
                comment{ id }
              }
            }
            """;
        var variables = new Dictionary<string, object?>
        {
            ["threadId"] = threadId,
            ["body"] = body
        };
        using var document = await SendGraphQlAsync(repository, mutation, variables, ct);
        var comment = document.RootElement
            .GetProperty("data").GetProperty("addPullRequestReviewThreadReply").GetProperty("comment");
        var id = comment.GetProperty("id").GetString() ?? Guid.NewGuid().ToString("n");
        return new GitHubWriteResponse(id, null, document.RootElement.GetRawText());
    }

    public Task<GitHubWriteResponse> CreateCheckRunAsync(
        GitHubRepositoryRef repository,
        string name,
        string headSha,
        string status,
        string? conclusion,
        string title,
        string summary,
        CancellationToken ct) =>
        SendJsonAsync(
            HttpMethod.Post,
            $"/repos/{repository.Owner}/{repository.Name}/check-runs",
            BuildCheckRunPayload(name, headSha, status, conclusion, title, summary),
            repository,
            ct);

    public Task<GitHubWriteResponse> UpdateCheckRunAsync(
        GitHubRepositoryRef repository,
        string checkRunId,
        string status,
        string? conclusion,
        string title,
        string summary,
        CancellationToken ct) =>
        SendJsonAsync(
            HttpMethod.Patch,
            $"/repos/{repository.Owner}/{repository.Name}/check-runs/{Uri.EscapeDataString(checkRunId)}",
            BuildCheckRunPayload(null, null, status, conclusion, title, summary),
            repository,
            ct);

    public async Task<GitHubPullRequestCreateResponse> CreatePullRequestAsync(GitHubRepositoryRef repository, string title, string head, string @base, string body, bool draft, CancellationToken ct)
    {
        using var request = await CreateRequestAsync(HttpMethod.Post, $"/repos/{repository.Owner}/{repository.Name}/pulls", repository, ct);
        request.Content = JsonContent.Create(
            new Dictionary<string, object?>
            {
                ["title"] = title,
                ["head"] = head,
                ["base"] = @base,
                ["body"] = body,
                ["draft"] = draft
            },
            options: JsonOptions);
        using var response = await httpClientFactory.CreateClient("GitHub").SendAsync(request, ct);
        var responseJson = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"GitHub API request failed with {(int)response.StatusCode} ({response.ReasonPhrase}): {responseJson}",
                null,
                response.StatusCode);
        }

        var created = JsonSerializer.Deserialize<GitHubPullRequestCreateResponse>(responseJson, JsonOptions)
            ?? throw new InvalidOperationException("GitHub pull request create response was empty.");
        return created;
    }

    public async Task<IReadOnlyList<GitHubReviewThread>> ListPullRequestReviewThreadsAsync(GitHubRepositoryRef repository, int number, CancellationToken ct)
    {
        const string query = """
            query($owner:String!,$name:String!,$number:Int!,$cursor:String){
              repository(owner:$owner,name:$name){
                pullRequest(number:$number){
                  reviewThreads(first:100,after:$cursor){
                    pageInfo{ hasNextPage endCursor }
                    nodes{ id isResolved comments(first:20){ nodes{ body } } }
                  }
                }
              }
            }
            """;

        var threads = new List<GitHubReviewThread>();
        string? cursor = null;
        for (var page = 0; page < 20; page++)
        {
            var variables = new Dictionary<string, object?>
            {
                ["owner"] = repository.Owner,
                ["name"] = repository.Name,
                ["number"] = number,
                ["cursor"] = cursor
            };
            using var document = await SendGraphQlAsync(repository, query, variables, ct);
            var reviewThreads = document.RootElement
                .GetProperty("data").GetProperty("repository").GetProperty("pullRequest").GetProperty("reviewThreads");
            foreach (var node in reviewThreads.GetProperty("nodes").EnumerateArray())
            {
                var id = node.GetProperty("id").GetString() ?? "";
                var isResolved = node.TryGetProperty("isResolved", out var resolvedElement) && resolvedElement.GetBoolean();
                var bodies = new List<string>();
                if (node.TryGetProperty("comments", out var comments) && comments.TryGetProperty("nodes", out var commentNodes))
                {
                    foreach (var comment in commentNodes.EnumerateArray())
                    {
                        if (comment.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String)
                        {
                            bodies.Add(body.GetString() ?? "");
                        }
                    }
                }

                threads.Add(new GitHubReviewThread(id, isResolved, bodies));
            }

            var pageInfo = reviewThreads.GetProperty("pageInfo");
            if (!pageInfo.GetProperty("hasNextPage").GetBoolean())
            {
                break;
            }

            cursor = pageInfo.GetProperty("endCursor").GetString();
        }

        return threads;
    }

    public async Task<GitHubWriteResponse> ResolveReviewThreadAsync(GitHubRepositoryRef repository, string threadId, bool resolved, CancellationToken ct)
    {
        var mutation = resolved
            ? "mutation($threadId:ID!){ resolveReviewThread(input:{threadId:$threadId}){ thread{ id isResolved } } }"
            : "mutation($threadId:ID!){ unresolveReviewThread(input:{threadId:$threadId}){ thread{ id isResolved } } }";
        var variables = new Dictionary<string, object?> { ["threadId"] = threadId };
        using var document = await SendGraphQlAsync(repository, mutation, variables, ct);
        return new GitHubWriteResponse(threadId, null, document.RootElement.GetRawText());
    }

    private async Task<JsonDocument> SendGraphQlAsync(GitHubRepositoryRef repository, string query, IReadOnlyDictionary<string, object?> variables, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildGraphQlUri());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("Oratorio-GitHubSource");
        var token = await tokenProvider.GetBearerTokenAsync(repository, ct);
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        request.Content = JsonContent.Create(new Dictionary<string, object?> { ["query"] = query, ["variables"] = variables }, options: JsonOptions);
        using var response = await httpClientFactory.CreateClient("GitHub").SendAsync(request, ct);
        var responseJson = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"GitHub GraphQL request failed with {(int)response.StatusCode} ({response.ReasonPhrase}): {responseJson}",
                null,
                response.StatusCode);
        }

        var document = JsonDocument.Parse(responseJson);
        if (document.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
        {
            var message = errors[0].TryGetProperty("message", out var m) ? m.GetString() : "GitHub GraphQL returned errors.";
            document.Dispose();
            throw new InvalidOperationException(message ?? "GitHub GraphQL returned errors.");
        }

        return document;
    }

    private Uri BuildGraphQlUri()
    {
        var endpoint = options.CurrentValue.Endpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return new Uri("https://api.github.com/graphql");
        }

        var trimmed = endpoint.TrimEnd('/');
        if (trimmed.EndsWith("/api/v3", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(trimmed[..^"/api/v3".Length] + "/api/graphql");
        }

        return new Uri(trimmed + "/graphql");
    }

    private async Task<IReadOnlyList<T>> GetPagesAsync<T>(GitHubRepositoryRef repository, string pathAndQuery, CancellationToken ct, int maxPages = 10)
    {
        var results = new List<T>();
        for (var page = 1; page <= maxPages; page++)
        {
            var separator = pathAndQuery.Contains('?') ? '&' : '?';
            using var request = await CreateRequestAsync(HttpMethod.Get, $"{pathAndQuery}{separator}page={page}", repository, ct);
            using var response = await httpClientFactory.CreateClient("GitHub").SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var pageItems = await response.Content.ReadFromJsonAsync<IReadOnlyList<T>>(JsonOptions, ct) ?? [];
            results.AddRange(pageItems);
            if (pageItems.Count < 100)
            {
                break;
            }
        }

        return results;
    }

    private static string StateQueryValue(GitHubListState state) =>
        state switch
        {
            GitHubListState.Open => "open",
            GitHubListState.Closed => "closed",
            _ => "all"
        };

    private async Task<GitHubWriteResponse> SendJsonAsync(HttpMethod method, string pathAndQuery, object payload, GitHubRepositoryRef repository, CancellationToken ct)
    {
        using var request = await CreateRequestAsync(method, pathAndQuery, repository, ct);
        request.Content = JsonContent.Create(payload, options: JsonOptions);
        using var response = await httpClientFactory.CreateClient("GitHub").SendAsync(request, ct);
        var responseJson = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"GitHub API request failed with {(int)response.StatusCode} ({response.ReasonPhrase}): {responseJson}",
                null,
                response.StatusCode);
        }

        return ParseWriteResponse(responseJson);
    }

    private static Dictionary<string, object?> BuildCheckRunPayload(
        string? name,
        string? headSha,
        string status,
        string? conclusion,
        string title,
        string summary)
    {
        var payload = new Dictionary<string, object?>
        {
            ["status"] = status,
            ["output"] = new Dictionary<string, object?>
            {
                ["title"] = title,
                ["summary"] = summary
            }
        };
        if (!string.IsNullOrWhiteSpace(name))
        {
            payload["name"] = name;
        }

        if (!string.IsNullOrWhiteSpace(headSha))
        {
            payload["head_sha"] = headSha;
        }

        if (string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            payload["conclusion"] = conclusion ?? "neutral";
            payload["completed_at"] = DateTimeOffset.UtcNow;
        }
        else
        {
            payload["started_at"] = DateTimeOffset.UtcNow;
        }

        return payload;
    }

    private static GitHubWriteResponse ParseWriteResponse(string responseJson)
    {
        try
        {
            using var document = JsonDocument.Parse(responseJson);
            var root = document.RootElement;
            var id = root.TryGetProperty("id", out var idElement) ? idElement.ToString() : Guid.NewGuid().ToString("n");
            var htmlUrl = root.TryGetProperty("html_url", out var urlElement) ? urlElement.GetString() : null;
            return new GitHubWriteResponse(id, htmlUrl, responseJson);
        }
        catch (JsonException)
        {
            return new GitHubWriteResponse(Guid.NewGuid().ToString("n"), null, responseJson);
        }
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(HttpMethod method, string pathAndQuery, GitHubRepositoryRef repository, CancellationToken ct)
    {
        var request = new HttpRequestMessage(method, BuildUri(pathAndQuery));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("Oratorio-GitHubSource");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        var token = await tokenProvider.GetBearerTokenAsync(repository, ct);
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    private Uri BuildUri(string pathAndQuery)
    {
        var endpoint = options.CurrentValue.Endpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            endpoint = "https://api.github.com";
        }

        return new Uri(new Uri(endpoint.TrimEnd('/') + "/"), pathAndQuery.TrimStart('/'));
    }
}
