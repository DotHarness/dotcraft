using System.Net;
using DotCraft.Oratorio.GitHub;

namespace DotCraft.Oratorio.Tests;

public sealed class GitHubIssueReadTests
{
    [Fact]
    public async Task ReadsIssueAndActualDefaultBranchUsingRepositoryAuthentication()
    {
        using var handler = new CapturingHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(request.RequestUri!.AbsolutePath.EndsWith("/issues/42", StringComparison.Ordinal)
                ? """{"id":1001,"number":42,"title":"Explain the issue","state":"open","html_url":"https://github.example.test/owner/repo/issues/42","body":"Issue requirements."}"""
                : """{"default_branch":"trunk"}""")
        });
        using var http = new HttpClient(handler);
        var client = CreateClient(http);
        var repository = new GitHubRepositoryRef("owner", "repo");
        var metadata = await client.GetRepositoryAsync(repository, CancellationToken.None);
        Assert.Equal("trunk", metadata.DefaultBranch);
        Assert.Equal("https://github.example.test/api/v3/repos/owner/repo", handler.LastRequestUri!.AbsoluteUri);
        var issue = await client.GetIssueAsync(repository, 42, CancellationToken.None);
        Assert.Equal(42, issue.Number);
        Assert.Equal("Issue requirements.", issue.Body);
        Assert.Null(issue.PullRequest);
        Assert.Equal("https://github.example.test/api/v3/repos/owner/repo/issues/42", handler.LastRequestUri!.AbsoluteUri);
        Assert.Equal("test-token", handler.LastAuthorization!.Parameter);
    }

    private static GitHubApiClient CreateClient(HttpClient http) => new(
        new StaticHttpClientFactory(http),
        new StaticOptionsMonitor<GitHubOptions>(new GitHubOptions { Endpoint = "https://github.example.test/api/v3" }),
        new StaticGitHubTokenProvider(),
        new GitHubCredentialResolver(new PassthroughConfigurationSecretProtector()),
        new FixedClock(DateTimeOffset.UtcNow));
}
