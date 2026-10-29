using System.Net.Http.Json;
using System.Text.Json;
using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.GitHub;
using DotCraft.Oratorio.Integrations;
using DotCraft.Protocol.AppServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static DotCraft.Oratorio.Tests.OratorioApiTestSupport;

namespace DotCraft.Oratorio.Tests;

public sealed class SourceSetupToolTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("oratorio-source-setup-");

    public void Dispose() => _root.Delete(recursive: true);

    [Fact]
    public async Task ConnectProject_upserts_a_github_project_without_duplicates()
    {
        await using var app = CreateApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(new FakeGitHubApiClient());
        });
        var first = Workspace("first");
        var second = Workspace("second");

        var connected = await CallAsync(app, OratorioDynamicToolCatalog.ConnectProjectName, new
        {
            provider = "github",
            project = "example-owner/new-repo",
            workspacePath = first
        });
        Assert.True(connected.Success, Text(connected));
        var result = connected.StructuredContent!.Value;
        Assert.Equal("ready", result.GetProperty("credentials").GetString());
        Assert.Equal("succeeded", result.GetProperty("sync").GetProperty("status").GetString());
        Assert.Equal(first, result.GetProperty("project").GetProperty("workspacePath").GetString());
        Assert.True(result.GetProperty("project").GetProperty("autoReview").GetBoolean());

        var again = await CallAsync(app, OratorioDynamicToolCatalog.ConnectProjectName, new
        {
            provider = "github",
            project = "/example-owner/new-repo/",
            workspacePath = second,
            autoReview = false,
            syncIntervalMinutes = 0
        });
        Assert.True(again.Success, Text(again));

        var configuration = (await GetConfigurationAsync(app)).Configuration;
        Assert.Single(configuration.GitHub.Repositories, x => x == "example-owner/new-repo");
        var route = Assert.Single(configuration.DotCraft.RepositoryWorkspaceRoutes, x => x.Project == "github:github.com/example-owner/new-repo");
        Assert.Equal(second, route.WorkspacePath);
        Assert.DoesNotContain("github:github.com/example-owner/new-repo", configuration.Automation.AutoReviewRepositories);
        var schedules = await app.CreateClient().GetFromJsonAsync<SourceSyncSchedulesResponse>("/api/v1/sources/sync-schedules", JsonOptions);
        Assert.False(Assert.Single(schedules!.Schedules, x => x.Provider == "github").Enabled);

        var setup = (await CallAsync(app, OratorioDynamicToolCatalog.GetSourceSetupName, new { })).StructuredContent!.Value;
        var project = Assert.Single(
            setup.GetProperty("projects").EnumerateArray(),
            x => x.GetProperty("project").GetString() == "example-owner/new-repo");
        Assert.Equal("ready", project.GetProperty("credentials").GetString());
        Assert.Equal("succeeded", project.GetProperty("lastSync").GetProperty("status").GetString());
        Assert.Contains(setup.GetProperty("workspaces").EnumerateArray(), x => x.GetProperty("path").GetString() == second);
    }

    [Fact]
    public async Task ConnectProject_keeps_current_settings_for_omitted_arguments()
    {
        await using var app = CreateApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(new FakeGitHubApiClient());
        });
        var workspace = Workspace("keep");

        var first = await CallAsync(app, OratorioDynamicToolCatalog.ConnectProjectName, new
        {
            provider = "github",
            project = "example-owner/new-repo",
            workspacePath = workspace,
            allowWrites = true,
            autoReview = false,
            syncIntervalMinutes = 30
        });
        Assert.True(first.Success, Text(first));

        var again = await CallAsync(app, OratorioDynamicToolCatalog.ConnectProjectName, new
        {
            provider = "github",
            project = "example-owner/new-repo",
            workspacePath = workspace
        });
        Assert.True(again.Success, Text(again));

        var configuration = (await GetConfigurationAsync(app)).Configuration;
        Assert.True(configuration.GitHub.WritesEnabled);
        Assert.DoesNotContain("github:github.com/example-owner/new-repo", configuration.Automation.AutoReviewRepositories);
        var schedules = await app.CreateClient().GetFromJsonAsync<SourceSyncSchedulesResponse>("/api/v1/sources/sync-schedules", JsonOptions);
        var schedule = Assert.Single(schedules!.Schedules, x => x.Provider == "github");
        Assert.True(schedule.Enabled);
        Assert.Equal(1800, schedule.IntervalSeconds);
    }

    [Fact]
    public async Task GitLab_project_reports_missing_credentials_until_the_token_is_saved()
    {
        const string token = "glpat-source-setup-secret";
        await using var app = CreateApp();

        var connected = await CallAsync(app, OratorioDynamicToolCatalog.ConnectProjectName, new
        {
            provider = "gitlab",
            project = "group/sub/project",
            workspacePath = Workspace("gitlab"),
            endpoint = "https://gitlab.example.test"
        });
        Assert.True(connected.Success, Text(connected));
        var result = connected.StructuredContent!.Value;
        Assert.Equal("missing", result.GetProperty("credentials").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("sync").ValueKind);
        Assert.Contains(result.GetProperty("warnings").EnumerateArray(), x => x.GetProperty("code").GetString() == "gitlabTokenMissing");

        var configuration = (await GetConfigurationAsync(app)).Configuration;
        Assert.True(configuration.GitLab.Enabled);
        Assert.Equal("https://gitlab.example.test/api/v4", configuration.GitLab.ApiBaseUrl);
        Assert.Contains("group/sub/project", configuration.GitLab.Projects);
        Assert.Contains("gitlab:gitlab.example.test/group/sub/project", configuration.Automation.AutoReviewRepositories);
        Assert.False(Assert.Single(configuration.GitLab.ProjectProfiles).Secrets!.Token.Configured);

        var request = (await CallAsync(app, OratorioDynamicToolCatalog.RequestSourceCredentialName, new
        {
            provider = "gitlab",
            project = "group/sub/project"
        })).StructuredContent!.Value;
        Assert.Equal("gitlabToken", request.GetProperty("credential").GetString());
        Assert.Equal("gitlab.example.test", request.GetProperty("instance").GetString());
        Assert.False(request.GetProperty("configured").GetBoolean());
        Assert.Equal(JsonValueKind.Null, request.GetProperty("appId").ValueKind);

        var saved = await CallAsync(app, OratorioDynamicToolCatalog.SaveSourceCredentialName, new
        {
            provider = "gitlab",
            project = "group/sub/project",
            secret = token,
            tokenKind = "personalAccessToken"
        });
        Assert.True(saved.Success, Text(saved));
        Assert.True(saved.StructuredContent!.Value.GetProperty("configured").GetBoolean());
        Assert.DoesNotContain(token, saved.StructuredContent.Value.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(token, Text(saved), StringComparison.Ordinal);

        var client = app.CreateClient();
        var after = await GetConfigurationAsync(app);
        var profile = Assert.Single(after.Configuration.GitLab.ProjectProfiles);
        Assert.True(profile.Secrets!.Token.Configured);
        Assert.Equal("personalAccessToken", profile.TokenKind);
        Assert.DoesNotContain(token, await File.ReadAllTextAsync(OverlayPath), StringComparison.Ordinal);
        var changes = await client.GetStringAsync("/api/v1/settings/server-configuration/changes");
        Assert.DoesNotContain(token, changes, StringComparison.Ordinal);
        var audit = JsonSerializer.Deserialize<IReadOnlyList<ConfigurationChangeDto>>(changes, JsonOptions)!;
        Assert.All(audit, change => Assert.Equal("app-binding", change.Actor));
        Assert.Contains(audit[0].ChangedFields, field => field.EndsWith(".secrets.token", StringComparison.Ordinal));

        var setup = (await CallAsync(app, OratorioDynamicToolCatalog.GetSourceSetupName, new { })).StructuredContent!.Value;
        var project = Assert.Single(
            setup.GetProperty("projects").EnumerateArray(),
            x => x.GetProperty("project").GetString() == "group/sub/project");
        Assert.Equal("ready", project.GetProperty("credentials").GetString());
    }

    [Fact]
    public async Task Binding_writes_are_refused_when_the_deployment_disables_configuration_writes()
    {
        await using var app = CreateApp(settings: new Dictionary<string, string?> { ["Oratorio:Settings:Writable"] = "false" });

        var setup = (await CallAsync(app, OratorioDynamicToolCatalog.GetSourceSetupName, new { })).StructuredContent!.Value;
        Assert.False(setup.GetProperty("writable").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(setup.GetProperty("disabledReason").GetString()));

        var connected = await CallAsync(app, OratorioDynamicToolCatalog.ConnectProjectName, new
        {
            provider = "github",
            project = "example-owner/new-repo",
            workspacePath = Workspace("readonly")
        });
        Assert.Equal("configWritesForbidden", connected.ErrorCode);

        var saved = await CallAsync(app, OratorioDynamicToolCatalog.SaveSourceCredentialName, new
        {
            provider = "github",
            project = "example-owner/oratorio",
            secret = "-----BEGIN RSA PRIVATE KEY-----\nkey\n-----END RSA PRIVATE KEY-----"
        });
        Assert.Equal("configWritesForbidden", saved.ErrorCode);
        Assert.False(File.Exists(OverlayPath));
    }

    private string OverlayPath => Path.Combine(_root.FullName, "config.json");

    private TestOratorioApp CreateApp(
        Action<IServiceCollection>? services = null,
        IReadOnlyDictionary<string, string?>? settings = null)
    {
        var merged = new Dictionary<string, string?>(settings ?? new Dictionary<string, string?>())
        {
            ["Oratorio:Settings:ConfigPath"] = OverlayPath
        };
        return new TestOratorioApp(services, merged);
    }

    private string Workspace(string name) => Directory.CreateDirectory(Path.Combine(_root.FullName, name)).FullName;

    private static async Task<ServerConfigurationResponse> GetConfigurationAsync(TestOratorioApp app) =>
        (await app.CreateClient().GetFromJsonAsync<ServerConfigurationResponse>("/api/v1/settings/server-configuration", JsonOptions))!;

    private static async Task<DynamicToolCallResult> CallAsync(TestOratorioApp app, string tool, object arguments)
    {
        var catalog = app.Services.GetRequiredService<OratorioDynamicToolCatalog>();
        using var scope = app.Services.CreateScope();
        var call = new DynamicToolCallParams
        {
            ThreadId = string.Empty,
            TurnId = string.Empty,
            CallId = Guid.NewGuid().ToString("n"),
            Tool = tool,
            Arguments = JsonSerializer.SerializeToElement(arguments, JsonOptions)
        };
        return await catalog.InvokeAsync(
            call,
            new OratorioToolInvocationContext(
                scope.ServiceProvider,
                call,
                OratorioToolSurface.AppBinding,
                BindingGrant: new OratorioAppBindingGrantContext("binding-1", 1)),
            catalog.BindingDescriptors.Select(x => x.LocalName).ToHashSet(StringComparer.Ordinal),
            CancellationToken.None);
    }

    private static string Text(DynamicToolCallResult result) =>
        string.Join("\n", result.ContentItems?.Select(x => x.Text) ?? []);
}
