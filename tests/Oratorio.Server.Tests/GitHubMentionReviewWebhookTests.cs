using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Oratorio.Server.Data;
using Oratorio.Server.Domain;
using Oratorio.Server.GitHub;

namespace Oratorio.Server.Tests;

public sealed class GitHubMentionReviewWebhookTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public async Task ValidReviewCommand_DoesNotRequireCommandAccountParticipation_AndRedeliveryIsIdempotent()
    {
        await using var app = new TestOratorioApp(DisableHostedServices);
        var client = app.CreateClient();
        var payload = BuildPayload("@dotcraft-ai review for security regressions");

        var firstResponse = await SendWebhookAsync(client, payload, "delivery-1");
        Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);
        var first = await firstResponse.Content.ReadFromJsonAsync<GitHubCommentCommandWebhookResponse>(JsonOptions);
        Assert.NotNull(first);
        Assert.Equal(GitHubCommentCommandStatus.Queued, first.Status);
        Assert.False(first.Duplicate);

        var secondResponse = await SendWebhookAsync(client, payload, "delivery-1-redelivery");
        Assert.Equal(HttpStatusCode.Accepted, secondResponse.StatusCode);
        var second = await secondResponse.Content.ReadFromJsonAsync<GitHubCommentCommandWebhookResponse>(JsonOptions);
        Assert.NotNull(second);
        Assert.Equal(first.CommandId, second.CommandId);
        Assert.True(second.Duplicate);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var command = await db.GitHubCommentCommands.AsNoTracking().SingleAsync();
        Assert.Equal("9001", command.SourceCommentId);
        Assert.Equal("delivery-1", command.DeliveryId);
        Assert.Equal("example-owner/oratorio", command.Repository);
        Assert.Equal(184, command.PullRequestNumber);
        Assert.Equal("example-reviewer", command.ActorLogin);
        Assert.Equal("MEMBER", command.AuthorAssociation);
        Assert.Equal(GitHubCommentCommandKind.Review, command.CommandKind);
        Assert.Equal("security regressions", command.Focus);
        Assert.Equal(GitHubCommentCommandStatus.Queued, command.Status);
        Assert.Empty(await db.Runs.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData("User", "CONTRIBUTOR", "githubCommentCommandActorUnauthorized")]
    [InlineData("Bot", "OWNER", "githubCommentCommandBotRejected")]
    public async Task IneligibleActor_IsPersistedAsRejected(
        string senderType,
        string association,
        string expectedError)
    {
        await using var app = new TestOratorioApp(DisableHostedServices);
        var client = app.CreateClient();
        var payload = BuildPayload("@dotcraft-ai review", senderType: senderType, association: association);

        var response = await SendWebhookAsync(client, payload, "delivery-rejected");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<GitHubCommentCommandWebhookResponse>(JsonOptions);
        Assert.NotNull(result);
        Assert.Equal(GitHubCommentCommandStatus.Rejected, result.Status);
        Assert.Equal(expectedError, result.ErrorCode);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var command = await db.GitHubCommentCommands.AsNoTracking().SingleAsync();
        Assert.Equal(GitHubCommentCommandStatus.Rejected, command.Status);
        Assert.Equal(expectedError, command.ErrorCode);
        Assert.NotNull(command.CompletedAt);
        Assert.Empty(await db.Runs.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task UnconfiguredRepository_IsPersistedAsRejected()
    {
        await using var app = new TestOratorioApp(DisableHostedServices);
        var client = app.CreateClient();
        var payload = BuildPayload("@dotcraft-ai review", repository: "other-owner/other-repo");

        var response = await SendWebhookAsync(client, payload, "delivery-unconfigured");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var command = await db.GitHubCommentCommands.AsNoTracking().SingleAsync();
        Assert.Equal(GitHubCommentCommandStatus.Rejected, command.Status);
        Assert.Equal("githubCommentCommandRepositoryNotConfigured", command.ErrorCode);
        Assert.Empty(await db.Runs.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task RepositoryWithoutWorkspace_IsPersistedAsRejected()
    {
        await using var app = new TestOratorioApp(DisableHostedServices, settings: new Dictionary<string, string?>
        {
            ["Oratorio:GitHub:Repositories:0"] = "example-owner/no-workspace"
        });
        var client = app.CreateClient();
        var payload = BuildPayload("@dotcraft-ai review", repository: "example-owner/no-workspace");

        var response = await SendWebhookAsync(client, payload, "delivery-no-workspace");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var command = await db.GitHubCommentCommands.AsNoTracking().SingleAsync();
        Assert.Equal(GitHubCommentCommandStatus.Rejected, command.Status);
        Assert.Equal("githubCommentCommandWorkspaceNotConfigured", command.ErrorCode);
    }

    [Fact]
    public async Task IssueCommentWithoutWebhookSecret_FailsClosed()
    {
        await using var app = new TestOratorioApp(DisableHostedServices, settings: new Dictionary<string, string?>
        {
            ["Oratorio:GitHub:WebhookSecret"] = ""
        });
        var client = app.CreateClient();
        var payload = BuildPayload("@dotcraft-ai review");

        var response = await SendWebhookAsync(client, payload, "delivery-no-secret");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("githubWebhookSecretRequired", document.RootElement.GetProperty("error").GetString());

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        Assert.Empty(await db.GitHubCommentCommands.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task IssueCommentWithInvalidSignature_IsRejected()
    {
        await using var app = new TestOratorioApp(DisableHostedServices);
        var client = app.CreateClient();
        var payload = BuildPayload("@dotcraft-ai review");

        var response = await SendWebhookAsync(client, payload, "delivery-bad-signature", signature: "sha256=bad");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        Assert.Empty(await db.GitHubCommentCommands.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData("@dotcraft-ai implement this", true)]
    [InlineData("@dotcraft-ai review\nthanks", true)]
    [InlineData("@oratorio review", true)]
    [InlineData("@oratorio-integration review", true)]
    [InlineData("@dotcraft-ai review", false)]
    public async Task NonCommandOrNonPullRequest_FallsBackToExistingSync_WithoutPersistingCommand(
        string body,
        bool isPullRequest)
    {
        var fakeGitHub = new FakeGitHubApiClient();
        await using var app = new TestOratorioApp(services =>
        {
            DisableHostedServices(services);
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
        });
        var client = app.CreateClient();
        var payload = BuildPayload(body, isPullRequest: isPullRequest);

        var response = await SendWebhookAsync(client, payload, "delivery-no-command");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        Assert.Empty(await db.GitHubCommentCommands.AsNoTracking().ToListAsync());
        Assert.Empty(await db.Runs.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData("issue_comment", "edited")]
    [InlineData("pull_request", "created")]
    public async Task OtherEventOrAction_DoesNotPersistCommand(
        string eventName,
        string action)
    {
        var fakeGitHub = new FakeGitHubApiClient();
        await using var app = new TestOratorioApp(services =>
        {
            DisableHostedServices(services);
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
        });
        var client = app.CreateClient();
        var payload = BuildPayload("@dotcraft-ai review", action: action);

        var response = await SendWebhookAsync(
            client,
            payload,
            "delivery-other-event",
            eventName: eventName);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        Assert.Empty(await db.GitHubCommentCommands.AsNoTracking().ToListAsync());
        Assert.Empty(await db.Runs.AsNoTracking().ToListAsync());
    }

    private static async Task<HttpResponseMessage> SendWebhookAsync(
        HttpClient client,
        string payload,
        string deliveryId,
        string? signature = null,
        string eventName = "issue_comment")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/sources/github/webhook")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("X-GitHub-Event", eventName);
        request.Headers.TryAddWithoutValidation("X-GitHub-Delivery", deliveryId);
        request.Headers.TryAddWithoutValidation(
            "X-Hub-Signature-256",
            signature ?? Sign(payload, "test-secret"));
        return await client.SendAsync(request);
    }

    private static string BuildPayload(
        string body,
        string repository = "example-owner/oratorio",
        string senderType = "User",
        string association = "MEMBER",
        bool isPullRequest = true,
        string action = "created")
    {
        var issue = new Dictionary<string, object?>
        {
            ["number"] = 184
        };
        if (isPullRequest)
        {
            issue["pull_request"] = new
            {
                url = $"https://api.github.com/repos/{repository}/pulls/184"
            };
        }

        return JsonSerializer.Serialize(new
        {
            action,
            repository = new { full_name = repository },
            issue,
            comment = new
            {
                id = 9001,
                body,
                author_association = association,
                user = new { login = "example-reviewer", type = senderType }
            },
            sender = new { login = "example-reviewer", type = senderType }
        });
    }

    private static string Sign(string body, string secret) =>
        "sha256=" + Convert.ToHexString(
            HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(secret),
                Encoding.UTF8.GetBytes(body)))
            .ToLowerInvariant();

    private static void DisableHostedServices(IServiceCollection services) =>
        services.RemoveAll<IHostedService>();
}
