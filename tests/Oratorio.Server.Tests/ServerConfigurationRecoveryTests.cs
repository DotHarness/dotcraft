using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Oratorio.Server.Api;

namespace Oratorio.Server.Tests;

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
