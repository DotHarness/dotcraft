using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotCraft.Oratorio.Api;

namespace DotCraft.Oratorio.Tests;

public sealed class ServerConfigurationRecoveryTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public async Task ConfigurationWrite_AllowsUnrelatedChangesAndRemovalWithOfflineWorkspaceRoutes()
    {
        var root = Directory.CreateTempSubdirectory("oratorio-offline-routes-");
        var overlayPath = Path.Combine(root.FullName, "config.json");
        var offlineOne = Path.Combine(root.FullName, "offline-one");
        var offlineTwo = Path.Combine(root.FullName, "offline-two");
        Assert.False(Directory.Exists(offlineOne));
        Assert.False(Directory.Exists(offlineTwo));

        try
        {
            await using var app = new TestOratorioApp(settings: new Dictionary<string, string?>
            {
                ["Oratorio:Settings:ConfigPath"] = overlayPath
            });
            var client = app.CreateClient();
            var current = await GetConfigurationAsync(client);

            var seeded = await PutConfigurationAsync(client, current.Revision, current.Configuration with
            {
                DotCraft = current.Configuration.DotCraft with
                {
                    RepositoryWorkspaceRoutes =
                    [
                        new("github:github.com/example-owner/offline-one", offlineOne),
                        new("github:github.com/example-owner/offline-two", offlineTwo)
                    ]
                }
            });
            Assert.Equal(2, seeded.Configuration.Configuration.DotCraft.RepositoryWorkspaceRoutes.Count);

            var unrelated = await PutConfigurationAsync(
                client,
                seeded.Configuration.Revision,
                seeded.Configuration.Configuration with
                {
                    DotCraft = seeded.Configuration.Configuration.DotCraft with
                    {
                        RunTimeoutSeconds = seeded.Configuration.Configuration.DotCraft.RunTimeoutSeconds + 30
                    }
                });
            Assert.Equal(2, unrelated.Configuration.Configuration.DotCraft.RepositoryWorkspaceRoutes.Count);

            var retainedRoute = unrelated.Configuration.Configuration.DotCraft.RepositoryWorkspaceRoutes[1];
            var removed = await PutConfigurationAsync(
                client,
                unrelated.Configuration.Revision,
                unrelated.Configuration.Configuration with
                {
                    DotCraft = unrelated.Configuration.Configuration.DotCraft with
                    {
                        RepositoryWorkspaceRoutes = [retainedRoute]
                    }
                });

            Assert.Equal([retainedRoute], removed.Configuration.Configuration.DotCraft.RepositoryWorkspaceRoutes);
            Assert.Contains("dotCraft.repositoryWorkspaceRoutes", removed.AppliedFields);
            var changes = await client.GetFromJsonAsync<IReadOnlyList<ConfigurationChangeDto>>(
                "/api/v1/settings/server-configuration/changes",
                JsonOptions);
            var removalAudit = Assert.Single(changes!, change => change.ChangeId == removed.ChangeId);
            Assert.Contains("dotCraft.repositoryWorkspaceRoutes", removalAudit.ChangedFields);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative/workspace")]
    public async Task ConfigurationWrite_RejectsNonAbsoluteWorkspaceRoutes(string workspacePath)
    {
        var root = Directory.CreateTempSubdirectory("oratorio-invalid-route-");
        try
        {
            await using var app = new TestOratorioApp(settings: new Dictionary<string, string?>
            {
                ["Oratorio:Settings:ConfigPath"] = Path.Combine(root.FullName, "config.json")
            });
            var client = app.CreateClient();
            var current = await GetConfigurationAsync(client);
            var next = current.Configuration with
            {
                DotCraft = current.Configuration.DotCraft with
                {
                    RepositoryWorkspaceRoutes =
                    [
                        new("github:github.com/example-owner/invalid", workspacePath)
                    ]
                }
            };

            var response = await client.PutAsJsonAsync(
                "/api/v1/settings/server-configuration",
                new ServerConfigurationUpdateRequest(current.Revision, true, next),
                JsonOptions);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
            Assert.Equal("configurationValidationFailed", error?.Error.Code);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ConfigurationWrite_IsVisibleToImmediateReadsAndSyncScheduleUpdates()
    {
        var root = Directory.CreateTempSubdirectory("oratorio-config-visibility-");
        try
        {
            await using var app = new TestOratorioApp(settings: new Dictionary<string, string?>
            {
                ["Oratorio:Settings:ConfigPath"] = Path.Combine(root.FullName, "config.json")
            });
            var client = app.CreateClient();
            var current = await GetConfigurationAsync(client);

            var saved = await PutConfigurationAsync(
                client,
                current.Revision,
                WithGitLabProjects(current.Configuration, root.FullName, "group/project"));
            var reread = await GetConfigurationAsync(client);

            Assert.Equal(saved.Configuration.Revision, reread.Revision);
            Assert.Equal(["group/project"], reread.Configuration.GitLab.Projects);
            var profile = Assert.Single(reread.Configuration.GitLab.ProjectProfiles);
            Assert.Equal("group/project", profile.ProjectPath);
            Assert.True(profile.Secrets!.Token.Configured);
            Assert.Contains(
                reread.Configuration.DotCraft.RepositoryWorkspaceRoutes,
                route => route.Project == "gitlab:gitlab.example.test/group/project");

            var scheduleResponse = await client.PutAsJsonAsync(
                "/api/v1/sources/gitlab/sync-schedule",
                new SourceSyncScheduleUpdateRequest(true, 300),
                JsonOptions);
            Assert.True(scheduleResponse.IsSuccessStatusCode, await scheduleResponse.Content.ReadAsStringAsync());
            var schedule = await scheduleResponse.Content.ReadFromJsonAsync<SourceSyncScheduleDto>(JsonOptions);
            Assert.True(schedule!.Enabled);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ConfigurationWrite_KeepsRoutedGitLabProjectProfileWhileProjectIsDisabled()
    {
        var root = Directory.CreateTempSubdirectory("oratorio-disabled-gitlab-project-");
        try
        {
            await using var app = new TestOratorioApp(settings: new Dictionary<string, string?>
            {
                ["Oratorio:Settings:ConfigPath"] = Path.Combine(root.FullName, "config.json")
            });
            var client = app.CreateClient();
            var current = await GetConfigurationAsync(client);
            var enabled = await PutConfigurationAsync(
                client,
                current.Revision,
                WithGitLabProjects(current.Configuration, root.FullName, "group/active", "group/paused"));

            await PutConfigurationAsync(client, enabled.Configuration.Revision, enabled.Configuration.Configuration with
            {
                GitLab = enabled.Configuration.Configuration.GitLab with { Projects = ["group/active"] }
            });
            var disabled = await GetConfigurationAsync(client);
            var disabledProfile = Assert.Single(
                disabled.Configuration.GitLab.ProjectProfiles,
                profile => profile.ProjectPath == "group/paused");
            Assert.True(disabledProfile.Secrets!.Token.Configured);

            await PutConfigurationAsync(client, disabled.Revision, disabled.Configuration with
            {
                GitLab = disabled.Configuration.GitLab with { Projects = ["group/active", "group/paused"] }
            });
            var reenabled = await GetConfigurationAsync(client);
            var reenabledProfile = Assert.Single(
                reenabled.Configuration.GitLab.ProjectProfiles,
                profile => profile.ProjectPath == "group/paused");
            Assert.True(reenabledProfile.Secrets!.Token.Configured);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static ServerConfigurationDto WithGitLabProjects(
        ServerConfigurationDto configuration,
        string workspacePath,
        params string[] projects) =>
        configuration with
        {
            GitLab = configuration.GitLab with
            {
                Enabled = true,
                Endpoint = "https://gitlab.example.test",
                ApiBaseUrl = "https://gitlab.example.test/api/v4",
                Projects = projects,
                ProjectProfiles = projects
                    .Select(project => new GitLabProjectProfileDto(
                        "gitlab.example.test",
                        project,
                        "projectAccessToken",
                        new GitLabSecretConfigurationDto(
                            new SecretConfigurationFieldDto(false, "replace", $"{project}-token"),
                            new SecretConfigurationFieldDto(false),
                            new SecretConfigurationFieldDto(false))))
                    .ToArray()
            },
            DotCraft = configuration.DotCraft with
            {
                RepositoryWorkspaceRoutes =
                [
                    .. configuration.DotCraft.RepositoryWorkspaceRoutes,
                    .. projects.Select(project => new DotCraftRepositoryWorkspaceRouteDto(
                        $"gitlab:gitlab.example.test/{project}",
                        workspacePath))
                ]
            }
        };

    private static async Task<ServerConfigurationResponse> GetConfigurationAsync(HttpClient client) =>
        await client.GetFromJsonAsync<ServerConfigurationResponse>(
            "/api/v1/settings/server-configuration",
            JsonOptions)
        ?? throw new InvalidOperationException("Expected server configuration response.");

    private static async Task<ServerConfigurationUpdateResponse> PutConfigurationAsync(
        HttpClient client,
        string revision,
        ServerConfigurationDto configuration)
    {
        var response = await client.PutAsJsonAsync(
            "/api/v1/settings/server-configuration",
            new ServerConfigurationUpdateRequest(revision, true, configuration),
            JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ServerConfigurationUpdateResponse>(JsonOptions)
            ?? throw new InvalidOperationException("Expected server configuration update response.");
    }
}
