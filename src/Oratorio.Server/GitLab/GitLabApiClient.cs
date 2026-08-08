using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Oratorio.Server.GitLab;

public interface IGitLabApiClient
{
    Task<GitLabProject> GetProjectAsync(GitLabProjectRef project, CancellationToken ct);
    Task<IReadOnlyList<GitLabIssue>> ListIssuesAsync(GitLabProjectRef project, GitLabListState state, DateTimeOffset? updatedAfter, CancellationToken ct);
    Task<IReadOnlyList<GitLabMergeRequest>> ListMergeRequestsAsync(GitLabProjectRef project, GitLabListState state, DateTimeOffset? updatedAfter, CancellationToken ct);
    Task<GitLabMergeRequest> GetMergeRequestAsync(GitLabProjectRef project, int iid, CancellationToken ct);
    Task<IReadOnlyList<GitLabMergeRequestDiff>> ListMergeRequestDiffsAsync(GitLabProjectRef project, int iid, CancellationToken ct);
    Task<IReadOnlyList<GitLabNote>> ListIssueNotesAsync(GitLabProjectRef project, int iid, CancellationToken ct);
    Task<IReadOnlyList<GitLabNote>> ListMergeRequestNotesAsync(GitLabProjectRef project, int iid, CancellationToken ct);
    Task<IReadOnlyList<GitLabDiscussion>> ListMergeRequestDiscussionsAsync(GitLabProjectRef project, int iid, CancellationToken ct);
    Task<GitLabWriteResponse> CreateIssueNoteAsync(GitLabProjectRef project, int iid, string body, CancellationToken ct);
    Task<GitLabWriteResponse> CreateMergeRequestNoteAsync(GitLabProjectRef project, int iid, string body, CancellationToken ct);
    Task<GitLabWriteResponse> CreateMergeRequestDiscussionAsync(GitLabProjectRef project, int iid, string body, GitLabMergeRequestPosition position, CancellationToken ct);
    Task<GitLabWriteResponse> ResolveMergeRequestDiscussionAsync(GitLabProjectRef project, int iid, string discussionId, bool resolved, CancellationToken ct);
    Task<GitLabWriteResponse> SetCommitStatusAsync(GitLabProjectRef project, string sha, string state, string name, string description, string? targetUrl, CancellationToken ct);
    Task<GitLabMergeRequestCreateResponse> CreateMergeRequestAsync(GitLabProjectRef project, string title, string sourceBranch, string targetBranch, string description, bool draft, CancellationToken ct);
}

public sealed class GitLabApiClient(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<GitLabOptions> options,
    IGitLabCredentialResolver credentials)
    : IGitLabApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public async Task<GitLabProject> GetProjectAsync(GitLabProjectRef project, CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Get, $"/projects/{project.EncodedPath}", project);
        using var response = await httpClientFactory.CreateClient("GitLab").SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GitLabProject>(JsonOptions, ct)
            ?? throw new InvalidOperationException("GitLab project response was empty.");
    }

    public Task<IReadOnlyList<GitLabIssue>> ListIssuesAsync(
        GitLabProjectRef project,
        GitLabListState state,
        DateTimeOffset? updatedAfter,
        CancellationToken ct)
    {
        var path = $"/projects/{project.EncodedPath}/issues?scope=all&state={StateQueryValue(state)}&order_by=updated_at&sort=asc&per_page=100";
        if (updatedAfter is not null)
        {
            path += $"&updated_after={Uri.EscapeDataString(updatedAfter.Value.UtcDateTime.ToString("O"))}";
        }

        return GetPagesAsync<GitLabIssue>(project, path, ct);
    }

    public Task<IReadOnlyList<GitLabMergeRequest>> ListMergeRequestsAsync(
        GitLabProjectRef project,
        GitLabListState state,
        DateTimeOffset? updatedAfter,
        CancellationToken ct)
    {
        var path = $"/projects/{project.EncodedPath}/merge_requests?scope=all&state={StateQueryValue(state)}&order_by=updated_at&sort=asc&per_page=100";
        if (updatedAfter is not null)
        {
            path += $"&updated_after={Uri.EscapeDataString(updatedAfter.Value.UtcDateTime.ToString("O"))}";
        }

        return GetPagesAsync<GitLabMergeRequest>(project, path, ct);
    }

    public async Task<GitLabMergeRequest> GetMergeRequestAsync(GitLabProjectRef project, int iid, CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Get, $"/projects/{project.EncodedPath}/merge_requests/{iid}", project);
        using var response = await httpClientFactory.CreateClient("GitLab").SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GitLabMergeRequest>(JsonOptions, ct)
            ?? throw new InvalidOperationException("GitLab merge request response was empty.");
    }

    public Task<IReadOnlyList<GitLabMergeRequestDiff>> ListMergeRequestDiffsAsync(GitLabProjectRef project, int iid, CancellationToken ct) =>
        GetPagesAsync<GitLabMergeRequestDiff>(project, $"/projects/{project.EncodedPath}/merge_requests/{iid}/diffs?per_page=100", ct);

    public Task<IReadOnlyList<GitLabNote>> ListIssueNotesAsync(GitLabProjectRef project, int iid, CancellationToken ct) =>
        GetPagesAsync<GitLabNote>(project, $"/projects/{project.EncodedPath}/issues/{iid}/notes?sort=asc&order_by=updated_at&per_page=100", ct);

    public Task<IReadOnlyList<GitLabNote>> ListMergeRequestNotesAsync(GitLabProjectRef project, int iid, CancellationToken ct) =>
        GetPagesAsync<GitLabNote>(project, $"/projects/{project.EncodedPath}/merge_requests/{iid}/notes?sort=asc&order_by=updated_at&per_page=100", ct);

    public Task<IReadOnlyList<GitLabDiscussion>> ListMergeRequestDiscussionsAsync(GitLabProjectRef project, int iid, CancellationToken ct) =>
        GetPagesAsync<GitLabDiscussion>(project, $"/projects/{project.EncodedPath}/merge_requests/{iid}/discussions?per_page=100", ct);

    public Task<GitLabWriteResponse> CreateIssueNoteAsync(GitLabProjectRef project, int iid, string body, CancellationToken ct) =>
        SendJsonAsync(
            HttpMethod.Post,
            $"/projects/{project.EncodedPath}/issues/{iid}/notes",
            new Dictionary<string, object?> { ["body"] = body },
            project,
            ct);

    public Task<GitLabWriteResponse> CreateMergeRequestNoteAsync(GitLabProjectRef project, int iid, string body, CancellationToken ct) =>
        SendJsonAsync(
            HttpMethod.Post,
            $"/projects/{project.EncodedPath}/merge_requests/{iid}/notes",
            new Dictionary<string, object?> { ["body"] = body },
            project,
            ct);

    public Task<GitLabWriteResponse> CreateMergeRequestDiscussionAsync(GitLabProjectRef project, int iid, string body, GitLabMergeRequestPosition position, CancellationToken ct) =>
        SendJsonAsync(
            HttpMethod.Post,
            $"/projects/{project.EncodedPath}/merge_requests/{iid}/discussions",
            new Dictionary<string, object?>
            {
                ["body"] = body,
                ["position"] = BuildPositionPayload(position)
            },
            project,
            ct);

    public Task<GitLabWriteResponse> ResolveMergeRequestDiscussionAsync(GitLabProjectRef project, int iid, string discussionId, bool resolved, CancellationToken ct) =>
        SendJsonAsync(
            HttpMethod.Put,
            $"/projects/{project.EncodedPath}/merge_requests/{iid}/discussions/{Uri.EscapeDataString(discussionId)}?resolved={(resolved ? "true" : "false")}",
            new Dictionary<string, object?>(),
            project,
            ct);

    public Task<GitLabWriteResponse> SetCommitStatusAsync(
        GitLabProjectRef project,
        string sha,
        string state,
        string name,
        string description,
        string? targetUrl,
        CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>
        {
            ["state"] = state,
            ["name"] = name,
            ["description"] = description
        };
        if (!string.IsNullOrWhiteSpace(targetUrl))
        {
            payload["target_url"] = targetUrl;
        }

        return SendJsonAsync(HttpMethod.Post, $"/projects/{project.EncodedPath}/statuses/{Uri.EscapeDataString(sha)}", payload, project, ct);
    }

    public async Task<GitLabMergeRequestCreateResponse> CreateMergeRequestAsync(
        GitLabProjectRef project,
        string title,
        string sourceBranch,
        string targetBranch,
        string description,
        bool draft,
        CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>
        {
            ["source_branch"] = sourceBranch,
            ["target_branch"] = targetBranch,
            ["title"] = draft && !title.StartsWith("Draft:", StringComparison.OrdinalIgnoreCase)
                ? $"Draft: {title}"
                : title,
            ["description"] = description,
            ["remove_source_branch"] = false
        };

        using var request = CreateRequest(HttpMethod.Post, $"/projects/{project.EncodedPath}/merge_requests", project);
        request.Content = JsonContent.Create(payload, options: JsonOptions);
        using var response = await httpClientFactory.CreateClient("GitLab").SendAsync(request, ct);
        var responseJson = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"GitLab API request failed with {(int)response.StatusCode} ({response.ReasonPhrase}): {responseJson}",
                null,
                response.StatusCode);
        }

        return JsonSerializer.Deserialize<GitLabMergeRequestCreateResponse>(responseJson, JsonOptions)
            ?? throw new InvalidOperationException("GitLab merge request create response was empty.");
    }

    private async Task<IReadOnlyList<T>> GetPagesAsync<T>(GitLabProjectRef project, string pathAndQuery, CancellationToken ct, int maxPages = 50)
    {
        var results = new List<T>();
        for (var page = 1; page <= maxPages; page++)
        {
            var separator = pathAndQuery.Contains('?') ? '&' : '?';
            using var request = CreateRequest(HttpMethod.Get, $"{pathAndQuery}{separator}page={page}", project);
            using var response = await httpClientFactory.CreateClient("GitLab").SendAsync(request, ct);
            EnsureGitLabSuccess(response);

            var pageItems = await response.Content.ReadFromJsonAsync<IReadOnlyList<T>>(JsonOptions, ct) ?? [];
            results.AddRange(pageItems);
            var nextPage = response.Headers.TryGetValues("X-Next-Page", out var values)
                ? values.FirstOrDefault()
                : null;
            if (string.IsNullOrWhiteSpace(nextPage))
            {
                break;
            }
        }

        return results;
    }

    private static void EnsureGitLabSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw new HttpRequestException(
            $"GitLab API request failed with {(int)response.StatusCode} ({response.ReasonPhrase}).",
            null,
            response.StatusCode);
    }

    private async Task<GitLabWriteResponse> SendJsonAsync(HttpMethod method, string pathAndQuery, object payload, GitLabProjectRef project, CancellationToken ct)
    {
        using var request = CreateRequest(method, pathAndQuery, project);
        request.Content = JsonContent.Create(payload, options: JsonOptions);
        using var response = await httpClientFactory.CreateClient("GitLab").SendAsync(request, ct);
        var responseJson = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"GitLab API request failed with {(int)response.StatusCode} ({response.ReasonPhrase}): {responseJson}",
                null,
                response.StatusCode);
        }

        return ParseWriteResponse(responseJson);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string pathAndQuery, GitLabProjectRef project)
    {
        var request = new HttpRequestMessage(method, BuildUri(pathAndQuery));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("Oratorio-GitLabSource");
        var token = credentials.ResolveToken(options.CurrentValue, project);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new GitLabCredentialException(
                "gitlabProjectProfileTokenMissing",
                $"GitLab project profile token is missing for {project.ProjectPath}.");
        }

        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.TryAddWithoutValidation("PRIVATE-TOKEN", token);
        }

        return request;
    }

    private Uri BuildUri(string pathAndQuery) =>
        new(new Uri(options.CurrentValue.EffectiveApiBaseUrl.TrimEnd('/') + "/"), pathAndQuery.TrimStart('/'));

    private static Dictionary<string, object?> BuildPositionPayload(GitLabMergeRequestPosition position)
    {
        var payload = new Dictionary<string, object?>
        {
            ["base_sha"] = position.BaseSha,
            ["head_sha"] = position.HeadSha,
            ["start_sha"] = position.StartSha,
            ["position_type"] = "text",
            ["old_path"] = position.OldPath,
            ["new_path"] = position.NewPath
        };
        if (position.OldLine.HasValue)
        {
            payload["old_line"] = position.OldLine.Value;
        }

        if (position.NewLine.HasValue)
        {
            payload["new_line"] = position.NewLine.Value;
        }

        return payload;
    }

    private static GitLabWriteResponse ParseWriteResponse(string responseJson)
    {
        try
        {
            using var document = JsonDocument.Parse(responseJson);
            var root = document.RootElement;
            var id = root.TryGetProperty("id", out var idElement) ? idElement.ToString() : Guid.NewGuid().ToString("n");
            var url = root.TryGetProperty("web_url", out var webUrl)
                ? webUrl.GetString()
                : root.TryGetProperty("url", out var noteUrl)
                    ? noteUrl.GetString()
                    : null;
            return new GitLabWriteResponse(id, url, responseJson);
        }
        catch (JsonException)
        {
            return new GitLabWriteResponse(Guid.NewGuid().ToString("n"), null, responseJson);
        }
    }

    private static string StateQueryValue(GitLabListState state) =>
        state switch
        {
            GitLabListState.Opened => "opened",
            GitLabListState.Closed => "closed",
            GitLabListState.Merged => "merged",
            _ => "all"
        };
}
