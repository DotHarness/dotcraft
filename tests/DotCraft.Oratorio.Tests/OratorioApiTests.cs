using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.Integrations;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.GitHub;
using DotCraft.Oratorio.Services;
using static DotCraft.Oratorio.Tests.OratorioApiTestSupport;

namespace DotCraft.Oratorio.Tests;

public sealed class OratorioApiTests
{
    [Fact]
    public async Task RequestChanges_ClosesCurrentRound_AndNextDispatchCreatesNewRound()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();

        await CreateItemAsync(client, "task:test-request-changes");
        var firstDispatch = await PostAsync<ItemDetailResponse>(
            client,
            "/api/v1/items/local/task:test-request-changes/dispatch",
            new DispatchRequest("mock", "Initial run", MockOutcome.Success, 1));

        Assert.Equal(ItemState.Dispatching, firstDispatch.Item.State);
        Assert.Equal(1, firstDispatch.Item.CurrentRound);

        var firstReview = await WaitForItemAsync(client, "task:test-request-changes", x => x.Item.State == ItemState.AwaitingReview);
        Assert.Contains(firstReview.Runs, x => x.Attempt == 1 && x.Status == RunStatus.Succeeded);

        var changed = await PostAsync<ItemDetailResponse>(
            client,
            "/api/v1/items/local/task:test-request-changes/request-changes",
            new DecisionRequest("Please cover expired token behavior."));

        Assert.Equal(ItemState.Discovered, changed.Item.State);
        Assert.Equal(1, changed.Item.CurrentRound);
        Assert.Contains(changed.Rounds, x => x.RoundNumber == 1 && x.Status == RoundStatus.ChangesRequested);
        Assert.Contains(changed.Comments, x => x.Body == "Please cover expired token behavior.");

        var secondDispatch = await PostAsync<ItemDetailResponse>(
            client,
            "/api/v1/items/local/task:test-request-changes/dispatch",
            new DispatchRequest("mock", "Follow-up run", MockOutcome.Success, 1));

        Assert.Equal(ItemState.Dispatching, secondDispatch.Item.State);
        Assert.Equal(2, secondDispatch.Item.CurrentRound);

        var secondReview = await WaitForItemAsync(client, "task:test-request-changes", x => x.Item.State == ItemState.AwaitingReview);
        Assert.Contains(secondReview.Rounds, x => x.RoundNumber == 2 && x.Status == RoundStatus.AwaitingReview);
        Assert.Contains(secondReview.Runs, x => x.Attempt == 1 && x.Status == RunStatus.Succeeded);

        var round1 = Assert.Single(secondReview.Rounds, x => x.RoundNumber == 1);
        var round2 = Assert.Single(secondReview.Rounds, x => x.RoundNumber == 2);
        Assert.Contains(secondReview.Runs, x => x.RoundId == round1.RoundId && x.Attempt == 1);
        Assert.Contains(secondReview.Runs, x => x.RoundId == round2.RoundId && x.Attempt == 1);
    }

    [Fact]
    public async Task CancelRun_StopsLongMockRun_AndReturnsItemToDiscovered()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();

        var task = await CreateLocalTaskAsync(client, "Cancel mock run");
        var dispatched = await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{task.Item.ItemId}/dispatch",
            new DispatchRequest("mock", "Long enough to cancel.", MockOutcome.Success, 30));

        var runId = Assert.Single(dispatched.Runs).RunId;
        var cancelled = await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{task.Item.ItemId}/cancel-run",
            new CancelRunRequest("Operator moved it back to To do."));

        Assert.Equal(ItemState.Discovered, cancelled.Item.State);
        Assert.Null(cancelled.Item.CurrentRunId);
        Assert.Equal(CheckState.NotConfigured, cancelled.Item.CheckState);
        Assert.Contains(cancelled.Runs, x => x.RunId == runId && x.Status == RunStatus.Cancelled && x.ErrorCode == "operatorCancelled");
        Assert.Contains(cancelled.Rounds, x => x.RoundNumber == 1 && x.Status == RoundStatus.Cancelled);
        Assert.Contains(cancelled.Timeline, x => x.Kind == TimelineEventKind.RunCancelled);

        await Task.Delay(600);
        var afterWorkerTick = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{task.Item.ItemId}", JsonOptions);
        Assert.Equal(ItemState.Discovered, afterWorkerTick!.Item.State);
        Assert.Contains(afterWorkerTick.Runs, x => x.RunId == runId && x.Status == RunStatus.Cancelled);
        Assert.DoesNotContain(afterWorkerTick.Runs, x => x.RunId == runId && x.Status == RunStatus.Succeeded);
    }

    [Fact]
    public async Task CancelRun_IsRejected_WhenItemDoesNotHaveActiveRun()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();

        await CreateItemAsync(client, "task:test-cancel-discovered");
        await AssertCancelRejectedAsync(client, "/api/v1/items/local/task:test-cancel-discovered/cancel-run");

        await CreateItemAsync(client, "task:test-cancel-failed");
        await PostAsync<ItemDetailResponse>(
            client,
            "/api/v1/items/local/task:test-cancel-failed/dispatch",
            new DispatchRequest("mock", "Fail.", MockOutcome.Fail, 1));
        await WaitForItemAsync(client, "task:test-cancel-failed", x => x.Item.State == ItemState.Failed);
        await AssertCancelRejectedAsync(client, "/api/v1/items/local/task:test-cancel-failed/cancel-run");

        await CreateItemAsync(client, "task:test-cancel-approved");
        await PostAsync<ItemDetailResponse>(
            client,
            "/api/v1/items/local/task:test-cancel-approved/dispatch",
            new DispatchRequest("mock", "Complete.", MockOutcome.Success, 1));
        await WaitForItemAsync(client, "task:test-cancel-approved", x => x.Item.State == ItemState.AwaitingReview);
        await PostAsync<ItemDetailResponse>(
            client,
            "/api/v1/items/local/task:test-cancel-approved/approve",
            new DecisionRequest("Approved."));
        await AssertCancelRejectedAsync(client, "/api/v1/items/local/task:test-cancel-approved/cancel-run");

        var archived = await CreateLocalTaskAsync(client, "Archived cancel target");
        await PostAsync<ItemDetailResponse>(client, $"/api/v1/items/id/{archived.Item.ItemId}/archive", new { });
        await AssertCancelRejectedAsync(client, $"/api/v1/items/id/{archived.Item.ItemId}/cancel-run");
    }

    [Fact]
    public async Task FailedMockRun_CanBeRetried_InSameRoundWithNextAttempt()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();

        await CreateItemAsync(client, "task:test-fail-retry");
        await PostAsync<ItemDetailResponse>(
            client,
            "/api/v1/items/local/task:test-fail-retry/dispatch",
            new DispatchRequest("mock", "Fail this run.", MockOutcome.Fail, 1));

        var failed = await WaitForItemAsync(client, "task:test-fail-retry", x => x.Item.State == ItemState.Failed);
        Assert.Equal(CheckState.Failing, failed.Item.CheckState);
        Assert.Contains(failed.Runs, x => x.Attempt == 1 && x.Status == RunStatus.Failed && x.ErrorCode == "mockFailed");

        var retry = await PostAsync<ItemDetailResponse>(
            client,
            "/api/v1/items/local/task:test-fail-retry/dispatch",
            new DispatchRequest("mock", "Retry with success.", MockOutcome.Success, 1));

        Assert.Equal(1, retry.Item.CurrentRound);
        Assert.Contains(retry.Runs, x => x.Attempt == 2 && (x.Status is RunStatus.Queued or RunStatus.Dispatching or RunStatus.Running));

        var reviewed = await WaitForItemAsync(client, "task:test-fail-retry", x => x.Item.State == ItemState.AwaitingReview);
        Assert.Contains(reviewed.Runs, x => x.Attempt == 2 && x.Status == RunStatus.Succeeded);
    }

    [Fact]
    public async Task Approve_IsRejected_UnlessItemIsAwaitingReview()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();

        await CreateItemAsync(client, "task:test-active-approve");
        await AssertApproveRejectedAsync();
        await PostAsync<ItemDetailResponse>(
            client,
            "/api/v1/items/local/task:test-active-approve/dispatch",
            new DispatchRequest("mock", "Keep active.", MockOutcome.Success, 5));
        await AssertApproveRejectedAsync();

        async Task AssertApproveRejectedAsync()
        {
            var response = await client.PostAsJsonAsync(
                "/api/v1/items/local/task:test-active-approve/approve",
                new DecisionRequest("Looks fine."),
                JsonOptions);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
            Assert.Equal("invalidTransition", error?.Error.Code);
        }
    }

    [Fact]
    public async Task LocalTask_CreateEditArchiveAndReopen_RoundTripsThroughApi()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();

        var created = await CreateLocalTaskAsync(client);

        Assert.Equal("local", created.Item.Source);
        Assert.Equal(ItemKind.LocalTask, created.Item.Kind);
        Assert.StartsWith("task:", created.Item.ExternalId, StringComparison.Ordinal);
        Assert.Equal(ItemState.Discovered, created.Item.State);
        Assert.Equal(["m1", "local"], created.Item.Labels);
        Assert.Contains(created.Timeline, x => x.Kind == TimelineEventKind.SourceSynced && x.Title == "Local task created");

        var edited = await PatchAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{created.Item.ItemId}",
            new UpdateItemRequest(
                "Edited local task",
                "Updated local body.",
                "example-owner/local-repo",
                "example-assignee",
                "local/m1",
                ["m1", "edited"]));

        Assert.Equal("Edited local task", edited.Item.Title);
        Assert.Equal("Updated local body.", edited.Item.Description);
        Assert.Equal(["m1", "edited"], edited.Item.Labels);
        Assert.Contains(edited.Timeline, x => x.Kind == TimelineEventKind.ItemUpdated);

        var commented = await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{created.Item.ItemId}/comments",
            new CommentRequest("Keep the public API source-neutral.", null));
        Assert.Contains(commented.Comments, x => x.Body == "Keep the public API source-neutral.");
        Assert.Contains(commented.Timeline, x => x.Kind == TimelineEventKind.CommentAdded);

        var archived = await PostAsync<ItemDetailResponse>(client, $"/api/v1/items/id/{created.Item.ItemId}/archive", new { });
        Assert.Equal(ItemState.Archived, archived.Item.State);
        Assert.Contains(archived.Timeline, x => x.Kind == TimelineEventKind.ItemArchived);

        var defaultList = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items", JsonOptions);
        Assert.DoesNotContain(defaultList!.Items, x => x.ItemId == created.Item.ItemId);

        var archivedList = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?state=archived", JsonOptions);
        Assert.Contains(archivedList!.Items, x => x.ItemId == created.Item.ItemId);

        var includeArchivedList = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?includeArchived=true", JsonOptions);
        Assert.Contains(includeArchivedList!.Items, x => x.ItemId == created.Item.ItemId);

        var reopened = await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{created.Item.ItemId}/reopen",
            new DecisionRequest("Bring it back."));
        Assert.Equal(ItemState.Discovered, reopened.Item.State);
    }

    [Fact]
    public async Task LocalTask_EditAndArchiveRejectActiveRuns()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();

        var created = await CreateLocalTaskAsync(client);
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{created.Item.ItemId}/dispatch",
            new DispatchRequest("mock", "Keep active.", MockOutcome.Success, 5));

        var editResponse = await PatchRawAsync(
            client,
            $"/api/v1/items/id/{created.Item.ItemId}",
            new UpdateItemRequest("Cannot edit", "Active.", null, null, null, null));
        Assert.Equal(HttpStatusCode.Conflict, editResponse.StatusCode);
        var editError = await editResponse.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        Assert.Equal("invalidTransition", editError?.Error.Code);

        var archiveResponse = await client.PostAsJsonAsync($"/api/v1/items/id/{created.Item.ItemId}/archive", new { }, JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, archiveResponse.StatusCode);
        var archiveError = await archiveResponse.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        Assert.Equal("invalidTransition", archiveError?.Error.Code);
    }

    [Fact]
    public async Task ListTasks_RepositoryFilterMatchesCanonicalGitHubKeyForUnqualifiedRepository()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();

        var created = await CreateLocalTaskAsync(
            client,
            "Filter canonical project key",
            repository: "owner-alpha/repo-alpha");

        var list = await client.GetFromJsonAsync<TaskListResponse>(
            "/api/v1/tasks?repository=github:github.com/owner-alpha/repo-alpha",
            JsonOptions);

        Assert.NotNull(list);
        var item = Assert.Single(list.Tasks);
        Assert.Equal(created.Item.ItemId, item.ItemId);
        Assert.Equal("owner-alpha/repo-alpha", item.Repository);
    }

    [Fact]
    public async Task SettingsDiagnostics_ReturnsRedactedRuntimeStatus()
    {
        var fakeProcess = new FakeDotCraftProcessManager(
            true,
            new DotCraftAppServerEndpoint("ws://user:secret@127.0.0.1:9100/ws?token=hidden#fragment", "hub"),
            "DotCraft AppServer is reachable.");
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.AddSingleton<IDotCraftAppServerProcessManager>(fakeProcess);
            services.RemoveAll<IOptionsMonitor<GitHubOptions>>();
            services.AddSingleton<IOptionsMonitor<GitHubOptions>>(new StaticOptionsMonitor<GitHubOptions>(new GitHubOptions
            {
                Endpoint = "https://user:secret@api.github.test/v3?token=hidden#fragment",
                AppId = "12345",
                InstallationProfiles =
                [
                    new GitHubInstallationProfileOptions
                    {
                        Instance = "api.github.test",
                        Owner = "dotcraft",
                        InstallationId = "98765",
                        Source = "manual"
                    }
                ],
                PrivateKey = "test-private-key",
                WebhookSecret = "test-webhook-secret",
                Repositories = ["example-owner/oratorio"],
                WritesEnabled = true
            }));
            services.RemoveAll<IOptionsMonitor<DotCraftOptions>>();
            services.AddSingleton<IOptionsMonitor<DotCraftOptions>>(new StaticOptionsMonitor<DotCraftOptions>(new DotCraftOptions
            {
                RepositoryWorkspaceRoutes =
                [
                    new() { Project = "github:github.com/example-owner/oratorio", WorkspacePath = Path.Combine(Path.GetTempPath(), "oratorio-test-workspace") }
                ],
                AppServerUrl = "ws://user:secret@127.0.0.1:9100/ws?token=hidden#fragment",
                HubDiscoveryEnabled = true,
                ManagedWorktreesEnabled = true,
                WorktreeBranchPrefix = "oratorio/run",
                GlobalMaxActiveRuns = 4,
                MaxActiveRunsPerRepository = 2,
                MaxActiveRunsPerSource = 3,
                RetryBackoffSeconds = 11,
                MaxRetryBackoffSeconds = 301,
                WorktreeCleanupEnabled = true
            }));
        });
        var client = app.CreateClient();

        var diagnostics = await client.GetFromJsonAsync<SettingsDiagnosticsResponse>(
            "/api/v1/settings/diagnostics",
            JsonOptions);
        var serialized = JsonSerializer.Serialize(diagnostics, JsonOptions);

        Assert.NotNull(diagnostics);
        Assert.True(diagnostics!.Capabilities["settingsDiagnostics"]);
        Assert.True(diagnostics.Capabilities["serverConfigurationWrites"]);
        Assert.Equal("githubApp", diagnostics.GitHub.Authentication);
        Assert.Equal("https://api.github.test/v3", diagnostics.GitHub.Endpoint.TrimEnd('/'));
        Assert.Equal("ws://127.0.0.1:9100/ws", diagnostics.DotCraft.Endpoint.TrimEnd('/'));
        Assert.Equal(4, diagnostics.Runtime.GlobalMaxActiveRuns);
        Assert.True(diagnostics.Redaction.SecretsRedacted);
        Assert.DoesNotContain("test-private-key", serialized);
        Assert.DoesNotContain("test-webhook-secret", serialized);
        Assert.DoesNotContain("hidden", serialized);
        Assert.DoesNotContain("user:secret", serialized);
    }

    [Fact]
    public void StatePathDefaults_PreferStateRootOverContentRoot()
    {
        var root = Directory.CreateTempSubdirectory("oratorio-state-paths-");
        var contentRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "release", "server")).FullName;
        var stateRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "desktop-state")).FullName;
        var contentStateRoot = Path.GetFullPath(Path.Combine(contentRoot, "..", ".craft", "oratorio"));

        Assert.Equal(stateRoot, OratorioStatePaths.ResolveDefaultStateRoot(contentRoot, stateRoot));
        Assert.Equal(Path.Combine(stateRoot, "oratorio.db"), OratorioStatePaths.ResolveDefaultDatabasePath(contentRoot, stateRoot));
        Assert.Equal(Path.Combine(stateRoot, "config.json"), OratorioStatePaths.ResolveDefaultConfigurationOverlayPath(contentRoot, stateRoot));
        Assert.Equal(contentStateRoot, OratorioStatePaths.ResolveDefaultStateRoot(contentRoot));
        Assert.Equal(Path.Combine(contentStateRoot, "oratorio.db"), OratorioStatePaths.ResolveDefaultDatabasePath(contentRoot));
        Assert.Equal(Path.Combine(contentStateRoot, "config.json"), OratorioStatePaths.ResolveDefaultConfigurationOverlayPath(contentRoot));
    }

    [Fact]
    public async Task ServerConfiguration_AutomationLabels_DefaultOnlyBeforeOverlayExists()
    {
        var root = Directory.CreateTempSubdirectory("oratorio-config-label-defaults-");
        var overlayPath = Path.Combine(root.FullName, "config.json");
        await using var app = new TestOratorioApp(settings: new Dictionary<string, string?>
        {
            ["Oratorio:Settings:ConfigPath"] = overlayPath
        });
        var client = app.CreateClient();

        var current = await client.GetFromJsonAsync<ServerConfigurationResponse>(
            "/api/v1/settings/server-configuration",
            JsonOptions);

        Assert.NotNull(current);
        Assert.Equal(["oratorio:auto"], current!.Configuration.Automation.AutoDispatchAllowLabels);
        Assert.Equal(
            ["blocked", "on hold", "needs-design", "needs-human"],
            current.Configuration.Automation.AutoDispatchBlockLabels);

        var next = current.Configuration with
        {
            Automation = current.Configuration.Automation with
            {
                AutoDispatchAllowLabels = [],
                AutoDispatchBlockLabels = ["on hold", "needs-human"]
            }
        };
        var saved = await PutAsync<ServerConfigurationUpdateResponse>(
            client,
            "/api/v1/settings/server-configuration",
            new ServerConfigurationUpdateRequest(current.Revision, true, next));

        Assert.Empty(saved.Configuration.Configuration.Automation.AutoDispatchAllowLabels);
        Assert.Equal(
            ["on hold", "needs-human"],
            saved.Configuration.Configuration.Automation.AutoDispatchBlockLabels);

        ServerConfigurationResponse? reloaded = null;
        await WaitUntilAsync(async () =>
        {
            reloaded = await client.GetFromJsonAsync<ServerConfigurationResponse>(
                "/api/v1/settings/server-configuration",
                JsonOptions);
            return reloaded is not null
                && reloaded.Configuration.Automation.AutoDispatchAllowLabels.Count == 0
                && reloaded.Configuration.Automation.AutoDispatchBlockLabels.SequenceEqual(["on hold", "needs-human"]);
        });
        Assert.NotNull(reloaded);
        Assert.Empty(reloaded!.Configuration.Automation.AutoDispatchAllowLabels);
        Assert.Equal(
            ["on hold", "needs-human"],
            reloaded.Configuration.Automation.AutoDispatchBlockLabels);
    }

    [Fact]
    public async Task ServerConfigurationWrite_CanBeDisabledForServerDeployments()
    {
        var root = Directory.CreateTempSubdirectory("oratorio-disabled-write-");
        var overlayPath = Path.Combine(root.FullName, "config.json");
        await using var app = new TestOratorioApp(settings: new Dictionary<string, string?>
        {
            ["Oratorio:Settings:ConfigPath"] = overlayPath,
            ["Oratorio:Settings:Writable"] = "false"
        });
        var client = app.CreateClient();

        var configuration = await client.GetFromJsonAsync<ServerConfigurationResponse>(
            "/api/v1/settings/server-configuration",
            JsonOptions);
        Assert.NotNull(configuration);
        Assert.False(configuration!.Writable);
        Assert.Equal("Server configuration writes are disabled for this deployment.", configuration.DisabledReason);

        var response = await client.PutAsJsonAsync(
            "/api/v1/settings/server-configuration",
            new ServerConfigurationUpdateRequest(configuration.Revision, true, configuration.Configuration),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(File.Exists(overlayPath));
    }

    [Fact]
    public async Task ServerConfigurationWrite_PersistsAuditsAndReloadsFutureOperations()
    {
        var root = Directory.CreateTempSubdirectory("oratorio-config-write-");
        var workspace = Directory.CreateDirectory(Path.Combine(root.FullName, "workspace")).FullName;
        var overlayPath = Path.Combine(root.FullName, "config.json");
        await using var app = new TestOratorioApp(settings: new Dictionary<string, string?>
        {
            ["Oratorio:Settings:ConfigPath"] = overlayPath
        });
        var client = app.CreateClient();

        var current = await client.GetFromJsonAsync<ServerConfigurationResponse>(
            "/api/v1/settings/server-configuration",
            JsonOptions);
        Assert.NotNull(current);
        Assert.True(current!.Writable);

        var next = current.Configuration with
        {
            GitHub = current.Configuration.GitHub with
            {
                Endpoint = "https://api.github.test/v3",
                Repositories = ["example-owner/oratorio", "example-owner/companion-repo"],
                WritesEnabled = false
            },
            DotCraft = current.Configuration.DotCraft with
            {
                RepositoryWorkspaceRoutes =
                [
                    new("github:github.com/example-owner/oratorio", workspace)
                ],
                AppServerUrl = "ws://127.0.0.1:9191/ws",
                ApprovalPolicy = "autoApprove",
                RunTimeoutSeconds = 900
            },
            Runtime = current.Configuration.Runtime with
            {
                ManagedWorktreesEnabled = true,
                GlobalMaxActiveRuns = 3,
                MaxActiveRunsPerRepository = 2,
                MaxActiveRunsPerSource = 2,
                RetryBackoffSeconds = 12,
                WorktreeCleanupIntervalSeconds = 30
            },
            Automation = current.Configuration.Automation with
            {
                AutoReviewRepositories = ["example-owner/oratorio"]
            }
        };

        var updated = await PutAsync<ServerConfigurationUpdateResponse>(
            client,
            "/api/v1/settings/server-configuration",
            new ServerConfigurationUpdateRequest(current.Revision, true, next));

        Assert.NotEqual(current.Revision, updated.Configuration.Revision);
        Assert.False(updated.RestartRequired);
        Assert.Null(updated.RestartSignature);
        Assert.Null(updated.Configuration.RestartSignature);
        Assert.Contains("github.repositories", updated.AppliedFields);
        Assert.Contains("dotCraft.repositoryWorkspaceRoutes", updated.AppliedFields);
        Assert.Contains("automation.autoReviewRepositories", updated.AppliedFields);
        Assert.Equal(["example-owner/oratorio", "example-owner/companion-repo"], updated.Configuration.Configuration.GitHub.Repositories);
        Assert.Equal(["example-owner/oratorio"], updated.Configuration.Configuration.Automation.AutoReviewRepositories);
        Assert.True(File.Exists(overlayPath));
        var raw = await File.ReadAllTextAsync(overlayPath);
        Assert.Contains("example-owner/companion-repo", raw);
        Assert.Contains("autoReviewRepositories", raw);
        Assert.Contains("installationProfiles", raw);
        using (var overlay = JsonDocument.Parse(raw))
        {
            var gitHub = overlay.RootElement.GetProperty("Oratorio").GetProperty("GitHub");
            Assert.False(gitHub.TryGetProperty("installationId", out _));
            Assert.True(gitHub.TryGetProperty("installationProfiles", out _));
        }
        Assert.DoesNotContain("test-token", raw);
        Assert.DoesNotContain("test-secret", raw);
        Assert.DoesNotContain("test-private-key", raw);

        await WaitUntilAsync(async () =>
        {
            var status = await client.GetFromJsonAsync<GitHubSourceStatusResponse>("/api/v1/sources/github/status", JsonOptions);
            return status is { WritesEnabled: false }
                && status.Repositories.SequenceEqual(["example-owner/oratorio", "example-owner/companion-repo"]);
        });

        await WaitUntilAsync(async () =>
        {
            var status = await client.GetFromJsonAsync<DotCraftStatusResponse>("/api/v1/dotcraft/status", JsonOptions);
            return status is { WorkspacePath: var path, ApprovalPolicy: "autoApprove", GlobalMaxActiveRuns: 3 }
                && path == workspace;
        });

        var changes = await client.GetFromJsonAsync<IReadOnlyList<ConfigurationChangeDto>>(
            "/api/v1/settings/server-configuration/changes",
            JsonOptions);
        var change = Assert.Single(changes!);
        Assert.Contains("github.repositories", change.ChangedFields);
        Assert.Contains("dotCraft.repositoryWorkspaceRoutes", change.ChangedFields);
        Assert.Contains("automation.autoReviewRepositories", change.ChangedFields);
        Assert.DoesNotContain("test-token", JsonSerializer.Serialize(change, JsonOptions));

        var diagnostics = await client.GetFromJsonAsync<SettingsDiagnosticsResponse>(
            "/api/v1/settings/diagnostics",
            JsonOptions);
        Assert.True(diagnostics!.Capabilities["serverConfigurationWrites"]);
    }

    [Fact]
    public async Task ServerConfigurationWrite_PersistsCanonicalWorkspaceRoutesAcrossRestart()
    {
        var root = Directory.CreateTempSubdirectory("oratorio-config-routes-");
        var gitHubWorkspace = Directory.CreateDirectory(Path.Combine(root.FullName, "github")).FullName;
        var gitLabWorkspace = Directory.CreateDirectory(Path.Combine(root.FullName, "gitlab")).FullName;
        var overlayPath = Path.Combine(root.FullName, "config.json");
        const string gitLabKey = "gitlab:gitlab.example.test/group/subgroup/project";
        var settings = new Dictionary<string, string?>
        {
            ["Oratorio:Settings:ConfigPath"] = overlayPath
        };
        var configureServices = new Action<IServiceCollection>(services =>
        {
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.AddSingleton<IDotCraftAppServerProcessManager>(new FakeDotCraftProcessManager(
                connected: false,
                endpoint: new DotCraftAppServerEndpoint("ws://127.0.0.1:9200/ws", "hub")));
        });

        await using (var app = new TestOratorioApp(configureServices, settings))
        {
            var client = app.CreateClient();
            var current = await client.GetFromJsonAsync<ServerConfigurationResponse>(
                "/api/v1/settings/server-configuration",
                JsonOptions);
            Assert.NotNull(current);

            var next = current!.Configuration with
            {
                GitLab = current.Configuration.GitLab with
                {
                    Enabled = true,
                    Endpoint = "https://gitlab.example.test",
                    ApiBaseUrl = "https://gitlab.example.test/api/v4",
                    Projects = ["group/subgroup/project"]
                },
                DotCraft = current.Configuration.DotCraft with
                {
                    RepositoryWorkspaceRoutes =
                    [
                        new("github:github.com/example-owner/oratorio", gitHubWorkspace),
                        new(gitLabKey, gitLabWorkspace)
                    ]
                }
            };

            var updated = await PutAsync<ServerConfigurationUpdateResponse>(
                client,
                "/api/v1/settings/server-configuration",
                new ServerConfigurationUpdateRequest(current.Revision, true, next));

            Assert.Contains(updated.Configuration.Configuration.DotCraft.RepositoryWorkspaceRoutes,
                route => route.Project == gitLabKey && route.WorkspacePath == gitLabWorkspace);
        }

        var raw = await File.ReadAllTextAsync(overlayPath);
        using (var document = JsonDocument.Parse(raw))
        {
            var dotCraft = document.RootElement.GetProperty("Oratorio").GetProperty("DotCraft");
            var routes = dotCraft.GetProperty("repositoryWorkspaceRoutes").EnumerateArray().ToList();
            Assert.Contains(routes, route =>
                route.GetProperty("project").GetString() == "github:github.com/example-owner/oratorio" &&
                route.GetProperty("workspacePath").GetString() == gitHubWorkspace);
            Assert.Contains(routes, route =>
                route.GetProperty("project").GetString() == gitLabKey &&
                route.GetProperty("workspacePath").GetString() == gitLabWorkspace);
        }

        await using (var restarted = new TestOratorioApp(configureServices, settings))
        {
            var client = restarted.CreateClient();
            var restored = await client.GetFromJsonAsync<ServerConfigurationResponse>(
                "/api/v1/settings/server-configuration",
                JsonOptions);
            Assert.Contains(restored!.Configuration.DotCraft.RepositoryWorkspaceRoutes,
                route => route.Project == "github:github.com/example-owner/oratorio" && route.WorkspacePath == gitHubWorkspace);
            Assert.Contains(restored.Configuration.DotCraft.RepositoryWorkspaceRoutes,
                route => route.Project == gitLabKey && route.WorkspacePath == gitLabWorkspace);

            var workspaces = await client.GetFromJsonAsync<DotCraftWorkspacesResponse>("/api/v1/dotcraft/workspaces", JsonOptions);
            var gitLabRoute = Assert.Single(workspaces!.Workspaces, workspace => workspace.Path == gitLabWorkspace);
            Assert.Contains(gitLabKey, gitLabRoute.Repositories);
        }
    }

    [Fact]
    public async Task ServerConfigurationWrite_ReplacesClearsAndRedactsSecrets()
    {
        var root = Directory.CreateTempSubdirectory("oratorio-config-secrets-");
        var overlayPath = Path.Combine(root.FullName, "config.json");
        await using var app = new TestOratorioApp(settings: new Dictionary<string, string?>
        {
            ["Oratorio:Settings:ConfigPath"] = overlayPath,
            ["Oratorio:Settings:SecretKeyPath"] = Path.Combine(root.FullName, "secrets.key")
        });
        var client = app.CreateClient();

        var current = await client.GetFromJsonAsync<ServerConfigurationResponse>(
            "/api/v1/settings/server-configuration",
            JsonOptions);
        Assert.NotNull(current);

        var next = current.Configuration with
        {
            GitHub = current.Configuration.GitHub with
            {
                Secrets = current.Configuration.GitHub.Secrets! with
                {
                    PrivateKey = new SecretConfigurationFieldDto(true, "replace", "new-private-key"),
                    WebhookSecret = new SecretConfigurationFieldDto(true, "clear")
                }
            }
        };

        var updated = await PutAsync<ServerConfigurationUpdateResponse>(
            client,
            "/api/v1/settings/server-configuration",
            new ServerConfigurationUpdateRequest(current.Revision, true, next));

        Assert.False(updated.RestartRequired);
        Assert.Contains("github.secrets.privateKey", updated.AppliedFields);
        Assert.Contains("github.secrets.webhookSecret", updated.AppliedFields);
        var secrets = updated.Configuration.Configuration.GitHub.Secrets!;
        Assert.True(secrets.PrivateKey.Configured);
        Assert.False(secrets.WebhookSecret.Configured);

        var raw = await File.ReadAllTextAsync(overlayPath);
        Assert.Contains("enc:v1:", raw);
        Assert.DoesNotContain("new-private-key", raw);
        Assert.DoesNotContain("test-token", raw);
        Assert.DoesNotContain("test-private-key", raw);

        var changes = await client.GetFromJsonAsync<IReadOnlyList<ConfigurationChangeDto>>(
            "/api/v1/settings/server-configuration/changes",
            JsonOptions);
        var serializedAudit = JsonSerializer.Serialize(changes, JsonOptions);
        Assert.DoesNotContain("new-private-key", serializedAudit);
    }

    [Fact]
    public async Task ServerConfigurationWrite_PersistsGitLabProjectProfilesAndRedactsSecrets()
    {
        var root = Directory.CreateTempSubdirectory("oratorio-config-gitlab-profiles-");
        var overlayPath = Path.Combine(root.FullName, "config.json");
        await using var app = new TestOratorioApp(settings: new Dictionary<string, string?>
        {
            ["Oratorio:Settings:ConfigPath"] = overlayPath,
            ["Oratorio:Settings:SecretKeyPath"] = Path.Combine(root.FullName, "secrets.key")
        });
        var client = app.CreateClient();
        var current = await client.GetFromJsonAsync<ServerConfigurationResponse>(
            "/api/v1/settings/server-configuration",
            JsonOptions);
        Assert.NotNull(current);

        var next = current!.Configuration with
        {
            GitLab = current.Configuration.GitLab with
            {
                Enabled = true,
                WritesEnabled = true,
                Endpoint = "https://gitlab.example.test",
                ApiBaseUrl = "https://gitlab.example.test/api/v4",
                Projects = ["group/subgroup/project"],
                ProjectProfiles =
                [
                    new GitLabProjectProfileDto(
                        "gitlab.example.test",
                        "group/subgroup/project",
                        "projectAccessToken",
                        new GitLabSecretConfigurationDto(
                            new SecretConfigurationFieldDto(true, "replace", "profile-token"),
                            new SecretConfigurationFieldDto(true, "replace", "profile-webhook-secret"),
                            new SecretConfigurationFieldDto(true, "replace", "profile-signing-token")))
                ]
            }
        };

        var updated = await PutAsync<ServerConfigurationUpdateResponse>(
            client,
            "/api/v1/settings/server-configuration",
            new ServerConfigurationUpdateRequest(current.Revision, true, next));

        Assert.Contains("gitlab.projectProfiles", updated.AppliedFields);
        Assert.Contains("gitlab.projectProfiles.gitlab.example.test/group/subgroup/project.secrets.token", updated.AppliedFields);
        var profile = Assert.Single(updated.Configuration.Configuration.GitLab.ProjectProfiles);
        Assert.Equal("gitlab.example.test", profile.Instance);
        Assert.Equal("group/subgroup/project", profile.ProjectPath);
        Assert.Equal("projectAccessToken", profile.TokenKind);
        Assert.True(profile.Secrets!.Token.Configured);
        Assert.True(profile.Secrets.WebhookSecret.Configured);
        Assert.True(profile.Secrets.WebhookSigningToken.Configured);
        Assert.Null(profile.Secrets.Token.Value);
        Assert.Equal("unchanged", profile.Secrets.Token.Mode);

        var raw = await File.ReadAllTextAsync(overlayPath);
        Assert.Contains("enc:v1:", raw);
        Assert.DoesNotContain("profile-token", raw);
        Assert.DoesNotContain("profile-webhook-secret", raw);
        Assert.DoesNotContain("profile-signing-token", raw);
        using var document = JsonDocument.Parse(raw);
        var gitLab = document.RootElement.GetProperty("Oratorio").GetProperty("GitLab");
        var savedProfile = Assert.Single(gitLab.GetProperty("projectProfiles").EnumerateArray());
        Assert.Equal("gitlab.example.test", savedProfile.GetProperty("instance").GetString());
        Assert.Equal("group/subgroup/project", savedProfile.GetProperty("projectPath").GetString());
        Assert.StartsWith("enc:v1:", savedProfile.GetProperty("Token").GetString());
        Assert.StartsWith("enc:v1:", savedProfile.GetProperty("WebhookSecret").GetString());
        Assert.StartsWith("enc:v1:", savedProfile.GetProperty("WebhookSigningToken").GetString());
        Assert.False(gitLab.TryGetProperty("Token", out _));
        Assert.False(gitLab.TryGetProperty("WebhookSecret", out _));
        Assert.False(gitLab.TryGetProperty("WebhookSigningToken", out _));
    }

    [Fact]
    public async Task ServerConfigurationWrite_DropsGitLabProjectProfilesWhenEndpointHostChanges()
    {
        var root = Directory.CreateTempSubdirectory("oratorio-config-gitlab-host-");
        var overlayPath = Path.Combine(root.FullName, "config.json");
        await using var app = new TestOratorioApp(settings: new Dictionary<string, string?>
        {
            ["Oratorio:Settings:ConfigPath"] = overlayPath
        });
        var client = app.CreateClient();
        var current = await client.GetFromJsonAsync<ServerConfigurationResponse>(
            "/api/v1/settings/server-configuration",
            JsonOptions);
        Assert.NotNull(current);

        var withProfile = current!.Configuration with
        {
            GitLab = current.Configuration.GitLab with
            {
                Enabled = true,
                Endpoint = "https://gitlab.example.test",
                ApiBaseUrl = "https://gitlab.example.test/api/v4",
                Projects = ["group/subgroup/project"],
                ProjectProfiles =
                [
                    new GitLabProjectProfileDto(
                        "gitlab.example.test",
                        "group/subgroup/project",
                        "projectAccessToken",
                        new GitLabSecretConfigurationDto(
                            new SecretConfigurationFieldDto(true, "replace", "profile-token"),
                            new SecretConfigurationFieldDto(false),
                            new SecretConfigurationFieldDto(false)))
                ]
            }
        };
        var saved = await PutAsync<ServerConfigurationUpdateResponse>(
            client,
            "/api/v1/settings/server-configuration",
            new ServerConfigurationUpdateRequest(current.Revision, true, withProfile));
        Assert.Single(saved.Configuration.Configuration.GitLab.ProjectProfiles);

        var changedHost = saved.Configuration.Configuration with
        {
            GitLab = saved.Configuration.Configuration.GitLab with
            {
                Endpoint = "https://gitlab.other.test",
                ApiBaseUrl = "https://gitlab.other.test/api/v4"
            }
        };
        var cleared = await PutAsync<ServerConfigurationUpdateResponse>(
            client,
            "/api/v1/settings/server-configuration",
            new ServerConfigurationUpdateRequest(saved.Configuration.Revision, true, changedHost));

        Assert.Empty(cleared.Configuration.Configuration.GitLab.ProjectProfiles);
        var raw = await File.ReadAllTextAsync(overlayPath);
        Assert.DoesNotContain("profile-token", raw);
        using var document = JsonDocument.Parse(raw);
        var profiles = document.RootElement
            .GetProperty("Oratorio")
            .GetProperty("GitLab")
            .GetProperty("projectProfiles")
            .EnumerateArray()
            .ToArray();
        Assert.Empty(profiles);
    }

    [Fact]
    public async Task ServerConfigurationWrite_RejectsSecretsUnknownFieldsAndUnconfirmedImpact()
    {
        var root = Directory.CreateTempSubdirectory("oratorio-config-validation-");
        var workspace = Directory.CreateDirectory(Path.Combine(root.FullName, "workspace")).FullName;
        await using var app = new TestOratorioApp(settings: new Dictionary<string, string?>
        {
            ["Oratorio:Settings:ConfigPath"] = Path.Combine(root.FullName, "config.json")
        });
        var client = app.CreateClient();
        var current = await client.GetFromJsonAsync<ServerConfigurationResponse>(
            "/api/v1/settings/server-configuration",
            JsonOptions);
        Assert.NotNull(current);

        var secretResponse = await client.PutAsJsonAsync(
            "/api/v1/settings/server-configuration",
            new
            {
                baseRevision = current!.Revision,
                confirmImpact = true,
                configuration = new
                {
                    github = new
                    {
                        current.Configuration.GitHub.Endpoint,
                        token = "should-not-be-accepted",
                        current.Configuration.GitHub.AppId,
                        current.Configuration.GitHub.InstallationProfiles,
                        current.Configuration.GitHub.Repositories,
                        current.Configuration.GitHub.WritesEnabled
                    },
                    dotCraft = current.Configuration.DotCraft,
                    runtime = current.Configuration.Runtime
                }
            },
            JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, secretResponse.StatusCode);
        var secretError = await secretResponse.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        Assert.Equal("configurationValidationFailed", secretError?.Error.Code);

        var workspacePathResponse = await client.PutAsJsonAsync(
            "/api/v1/settings/server-configuration",
            new
            {
                baseRevision = current.Revision,
                confirmImpact = true,
                configuration = new
                {
                    gitHub = current.Configuration.GitHub,
                    dotCraft = new
                    {
                        workspacePath = workspace,
                        current.Configuration.DotCraft.RepositoryWorkspaceRoutes,
                        current.Configuration.DotCraft.AppServerUrl,
                        current.Configuration.DotCraft.HubDiscoveryEnabled,
                        current.Configuration.DotCraft.HubLockPath,
                        current.Configuration.DotCraft.ApprovalPolicy,
                        current.Configuration.DotCraft.RunTimeoutSeconds
                    },
                    runtime = current.Configuration.Runtime,
                    automation = current.Configuration.Automation
                }
            },
            JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, workspacePathResponse.StatusCode);
        var workspacePathError = await workspacePathResponse.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        Assert.Equal("configurationValidationFailed", workspacePathError?.Error.Code);

        var autoReviewResponse = await client.PutAsJsonAsync(
            "/api/v1/settings/server-configuration",
            new ServerConfigurationUpdateRequest(
                current.Revision,
                true,
                current.Configuration with
                {
                    Automation = current.Configuration.Automation with
                    {
                        AutoReviewRepositories = ["dotcraft\\oratorio"]
                    }
                }),
            JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, autoReviewResponse.StatusCode);
        var autoReviewError = await autoReviewResponse.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        Assert.Equal("configurationValidationFailed", autoReviewError?.Error.Code);

        var impactful = current.Configuration with
        {
            DotCraft = current.Configuration.DotCraft with
            {
                RepositoryWorkspaceRoutes =
                [
                    new("github:github.com/example-owner/oratorio", workspace)
                ]
            }
        };
        var unconfirmed = await client.PutAsJsonAsync(
            "/api/v1/settings/server-configuration",
            new ServerConfigurationUpdateRequest(current.Revision, false, impactful),
            JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);
        var impactError = await unconfirmed.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        Assert.Equal("configurationConfirmationRequired", impactError?.Error.Code);
    }
}
