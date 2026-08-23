using System.Diagnostics;
using System.Text;
using Oratorio.Server.GitHub;
using Oratorio.Server.GitLab;
using Oratorio.Server.Sources;

namespace Oratorio.Server.Tests;

public sealed class GitTransportCredentialTests
{
    [Fact]
    public void ApplyTo_AppendsAfterInheritedGitConfigEntries()
    {
        var startInfo = new ProcessStartInfo("git");
        startInfo.Environment["GIT_CONFIG_COUNT"] = "2";
        startInfo.Environment["GIT_CONFIG_KEY_0"] = "core.pager";
        startInfo.Environment["GIT_CONFIG_VALUE_0"] = "cat";
        startInfo.Environment["GIT_CONFIG_KEY_1"] = "core.autocrlf";
        startInfo.Environment["GIT_CONFIG_VALUE_1"] = "false";

        new GitTransportCredential("github.com", "x-access-token", "ghs-token").ApplyTo(startInfo);

        Assert.Equal("3", startInfo.Environment["GIT_CONFIG_COUNT"]);
        Assert.Equal("http.https://github.com/.extraheader", startInfo.Environment["GIT_CONFIG_KEY_2"]);

        Assert.Equal("core.pager", startInfo.Environment["GIT_CONFIG_KEY_0"]);
        Assert.Equal("cat", startInfo.Environment["GIT_CONFIG_VALUE_0"]);
        Assert.Equal("core.autocrlf", startInfo.Environment["GIT_CONFIG_KEY_1"]);
        Assert.Equal("false", startInfo.Environment["GIT_CONFIG_VALUE_1"]);
    }

    [Fact]
    public void ApplyTo_WithoutInheritedEntries_StartsAtIndexZeroAndEncodesBasicHeader()
    {
        var startInfo = new ProcessStartInfo("git");
        startInfo.Environment.Remove("GIT_CONFIG_COUNT");

        new GitTransportCredential("gitlab.example.test", "oauth2", "glpat-token").ApplyTo(startInfo);

        Assert.Equal("1", startInfo.Environment["GIT_CONFIG_COUNT"]);
        Assert.Equal("http.https://gitlab.example.test/.extraheader", startInfo.Environment["GIT_CONFIG_KEY_0"]);

        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("oauth2:glpat-token"));
        Assert.Equal($"Authorization: Basic {expected}", startInfo.Environment["GIT_CONFIG_VALUE_0"]);
    }

    [Fact]
    public void ApplyTo_IgnoresUnparsableInheritedCount()
    {
        var startInfo = new ProcessStartInfo("git");
        startInfo.Environment["GIT_CONFIG_COUNT"] = "not-a-number";

        new GitTransportCredential("github.com", "x-access-token", "ghs-token").ApplyTo(startInfo);

        Assert.Equal("1", startInfo.Environment["GIT_CONFIG_COUNT"]);
        Assert.Equal("http.https://github.com/.extraheader", startInfo.Environment["GIT_CONFIG_KEY_0"]);
    }

    [Fact]
    public void ToRemoteUrl_EscapesTheSecret()
    {
        var credential = new GitTransportCredential("github.com", "x-access-token", "tok/en+with spaces");

        Assert.Equal(
            "https://x-access-token:tok%2Fen%2Bwith%20spaces@github.com/example-owner/oratorio.git",
            credential.ToRemoteUrl("example-owner/oratorio"));
    }

    [Theory]
    [InlineData("github", "example-owner/oratorio", "github", "github.com", "example-owner/oratorio")]
    [InlineData("github", "github:github.com/example-owner/oratorio", "github", "github.com", "example-owner/oratorio")]
    [InlineData("gitlab", "gitlab:gitlab.example.test/group/project", "gitlab", "gitlab.example.test", "group/project")]
    public void TryResolveProject_NormalizesBothProviderForms(
        string provider,
        string repository,
        string expectedProvider,
        string expectedInstance,
        string expectedPath)
    {
        Assert.True(CreateProvider().TryResolveProject(provider, repository, out var project));
        Assert.Equal(new SourceProjectKey(expectedProvider, expectedInstance, expectedPath), project);
    }

    [Theory]
    [InlineData(null, "example-owner/oratorio")]
    [InlineData("github", null)]
    [InlineData("local", "some-task")]
    public void TryResolveProject_RejectsUnroutableRequests(string? provider, string? repository)
    {
        Assert.False(CreateProvider().TryResolveProject(provider, repository, out _));
    }

    [Fact]
    public async Task ResolveAsync_UsesTheGitHubInstallationToken()
    {
        var credential = await CreateProvider().ResolveAsync(
            new SourceProjectKey("github", "github.com", "example-owner/oratorio"),
            CancellationToken.None);

        Assert.NotNull(credential);
        Assert.Equal("github.com", credential.Host);
        Assert.Equal("x-access-token", credential.Username);
        Assert.Equal("test-token", credential.Secret);
    }

    [Fact]
    public async Task ResolveAsync_UsesTheEnterpriseHostWhenTheEndpointIsNotGitHubCom()
    {
        var provider = CreateProvider(gitHubEndpoint: "https://github.example.test/api/v3");
        var credential = await provider.ResolveAsync(
            new SourceProjectKey("github", "github.example.test", "example-owner/oratorio"),
            CancellationToken.None);

        Assert.NotNull(credential);
        Assert.Equal("github.example.test", credential.Host);
    }

    [Fact]
    public async Task ResolveAsync_UsesTheGitLabProjectToken()
    {
        var provider = CreateProvider(gitLabToken: "glpat-project-token");
        var credential = await provider.ResolveAsync(
            new SourceProjectKey("gitlab", "gitlab.example.test", "group/project"),
            CancellationToken.None);

        Assert.NotNull(credential);
        Assert.Equal("gitlab.example.test", credential.Host);
        Assert.Equal("oauth2", credential.Username);
        Assert.Equal("glpat-project-token", credential.Secret);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsNullWhenTheProviderHasNoCredential()
    {
        // GitLab without a project profile: fetches must degrade to anonymous
        // transport rather than failing preparation outright.
        var provider = CreateProvider(gitLabToken: null);

        Assert.Null(await provider.ResolveAsync(
            new SourceProjectKey("gitlab", "gitlab.example.test", "group/project"),
            CancellationToken.None));
    }

    [Fact]
    public async Task ResolveAsync_PropagatesMissingGitHubAppAuthentication()
    {
        // The push path relies on this type: a plain InvalidOperationException is
        // classified as transient and would be retried indefinitely.
        var provider = CreateProvider(gitHubTokens: new UnauthenticatedGitHubTokenProvider());

        await Assert.ThrowsAsync<GitHubAppAuthenticationRequiredException>(() => provider.ResolveAsync(
            new SourceProjectKey("github", "github.com", "example-owner/oratorio"),
            CancellationToken.None));
    }

    [Fact]
    public async Task ResolveAsync_ReturnsNullForUnsupportedProviders()
    {
        Assert.Null(await CreateProvider().ResolveAsync(
            new SourceProjectKey("local", "local", "task"),
            CancellationToken.None));
    }

    private static GitTransportCredentialProvider CreateProvider(
        string gitHubEndpoint = "https://api.github.com",
        string? gitLabToken = "glpat-project-token",
        IGitHubTokenProvider? gitHubTokens = null)
    {
        var gitLabOptions = new GitLabOptions { Endpoint = "https://gitlab.example.test" };
        if (gitLabToken is not null)
        {
            gitLabOptions.ProjectProfiles =
            [
                new GitLabProjectProfileOptions
                {
                    Instance = "gitlab.example.test",
                    ProjectPath = "group/project",
                    Token = gitLabToken
                }
            ];
        }

        return new GitTransportCredentialProvider(
            gitHubTokens ?? new StaticGitHubTokenProvider(),
            new StaticOptionsMonitor<GitHubOptions>(new GitHubOptions { Endpoint = gitHubEndpoint }),
            new GitLabCredentialResolver(new PassthroughConfigurationSecretProtector()),
            new StaticOptionsMonitor<GitLabOptions>(gitLabOptions));
    }

    private sealed class UnauthenticatedGitHubTokenProvider : IGitHubTokenProvider
    {
        public Task<string?> GetBearerTokenAsync(GitHubRepositoryRef repository, CancellationToken ct) =>
            throw new GitHubAppAuthenticationRequiredException();
    }
}
