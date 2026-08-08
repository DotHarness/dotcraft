using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using DotCraft.Protocol.AppServer;
using DotCraft.Sdk;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Oratorio.Server.Api;
using Oratorio.Server.Data;
using Oratorio.Server.DotCraft;
using Oratorio.Server.Domain;
using Oratorio.Server.GitHub;
using Oratorio.Server.Services;
using Oratorio.Server.Sources;
using DynamicToolCall = DotCraft.Protocol.AppServer.DynamicToolCallParams;
using DynamicToolResult = DotCraft.Protocol.AppServer.DynamicToolCallResult;
using RuntimeDynamicToolDeclaration = DotCraft.Protocol.AppServer.RuntimeDynamicToolDeclaration;
using RuntimeDynamicToolFunction = DotCraft.Protocol.AppServer.RuntimeDynamicToolFunction;
using RuntimeDynamicToolNamespace = DotCraft.Protocol.AppServer.RuntimeDynamicToolNamespace;

namespace Oratorio.Server.Tests;

public sealed class OratorioApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

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
    public async Task Dispatch_ReturnsActiveRun_BeforeMockRunnerCompletes()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();

        await CreateItemAsync(client, "task:test-active-run");
        var dispatched = await PostAsync<ItemDetailResponse>(
            client,
            "/api/v1/items/local/task:test-active-run/dispatch",
            new DispatchRequest("mock", "Long enough to observe.", MockOutcome.Success, 5));

        Assert.Equal(ItemState.Dispatching, dispatched.Item.State);
        Assert.Equal(CheckState.Pending, dispatched.Item.CheckState);
        Assert.Contains(dispatched.Runs, x => x.Status is RunStatus.Queued or RunStatus.Dispatching or RunStatus.Running);
        Assert.DoesNotContain(dispatched.Runs, x => x.Status == RunStatus.Succeeded);
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

        await Task.Delay(1200);
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

        await CreateItemAsync(client, "task:test-cancel-reviewed");
        await PostAsync<ItemDetailResponse>(
            client,
            "/api/v1/items/local/task:test-cancel-reviewed/dispatch",
            new DispatchRequest("mock", "Complete.", MockOutcome.Success, 1));
        await WaitForItemAsync(client, "task:test-cancel-reviewed", x => x.Item.State == ItemState.AwaitingReview);
        await AssertCancelRejectedAsync(client, "/api/v1/items/local/task:test-cancel-reviewed/cancel-run");

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
    public async Task TimeoutMockRun_EndsAsFailedItem()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();

        await CreateItemAsync(client, "task:test-timeout");
        await PostAsync<ItemDetailResponse>(
            client,
            "/api/v1/items/local/task:test-timeout/dispatch",
            new DispatchRequest("mock", "Timeout this run.", MockOutcome.Timeout, 1));

        var timedOut = await WaitForItemAsync(client, "task:test-timeout", x => x.Item.State == ItemState.Failed);
        Assert.Equal(CheckState.Failing, timedOut.Item.CheckState);
        Assert.Contains(timedOut.Runs, x => x.Status == RunStatus.TimedOut && x.ErrorCode == "mockTimedOut");
    }

    [Fact]
    public async Task Approve_IsRejected_WhileRunIsActive()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();

        await CreateItemAsync(client, "task:test-active-approve");
        await PostAsync<ItemDetailResponse>(
            client,
            "/api/v1/items/local/task:test-active-approve/dispatch",
            new DispatchRequest("mock", "Keep active.", MockOutcome.Success, 5));

        var response = await client.PostAsJsonAsync(
            "/api/v1/items/local/task:test-active-approve/approve",
            new DecisionRequest("Looks fine."),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        Assert.Equal("invalidTransition", error?.Error.Code);
    }

    [Fact]
    public async Task Approve_IsRejected_WhenItemIsNotAwaitingReview()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();

        await CreateItemAsync(client, "task:test-invalid-approve");

        var response = await client.PostAsJsonAsync(
            "/api/v1/items/local/task:test-invalid-approve/approve",
            new DecisionRequest("Looks fine."),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        Assert.Equal("invalidTransition", error?.Error.Code);
    }

    [Fact]
    public async Task AddComment_PersistsInItemDetail()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();

        await CreateItemAsync(client, "task:test-comment");
        await PostAsync<ItemDetailResponse>(
            client,
            "/api/v1/items/local/task:test-comment/comments",
            new CommentRequest("Keep the public API source-neutral.", null));

        var detail = await client.GetFromJsonAsync<ItemDetailResponse>(
            "/api/v1/items/local/task:test-comment",
            JsonOptions);

        Assert.NotNull(detail);
        Assert.Contains(detail!.Comments, x => x.Body == "Keep the public API source-neutral.");
        Assert.Contains(detail.Timeline, x => x.Kind == TimelineEventKind.CommentAdded);
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
    public async Task LocalTask_DispatchAndReview_UsesExistingLifecycle()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();

        var created = await CreateLocalTaskAsync(client);
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{created.Item.ItemId}/dispatch",
            new DispatchRequest("mock", "Run local task.", MockOutcome.Success, 1));

        var reviewed = await WaitForItemByIdAsync(client, created.Item.ItemId, x => x.Item.State == ItemState.AwaitingReview);
        Assert.Contains(reviewed.Runs, x => x.Status == RunStatus.Succeeded);

        var approved = await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{created.Item.ItemId}/approve",
            new DecisionRequest("Local task is done."));
        Assert.Equal(ItemState.Approved, approved.Item.State);
        Assert.Contains(approved.Decisions, x => x.Decision == DecisionType.Approve);
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
    public void StatePathDefaults_UseStateRoot_WhenConfigured()
    {
        var root = Directory.CreateTempSubdirectory("oratorio-state-paths-");
        var contentRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "release", "server")).FullName;
        var stateRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "desktop-state")).FullName;

        Assert.Equal(
            stateRoot,
            OratorioStatePaths.ResolveDefaultStateRoot(contentRoot, stateRoot));
        Assert.Equal(
            Path.Combine(stateRoot, "oratorio.db"),
            OratorioStatePaths.ResolveDefaultDatabasePath(contentRoot, stateRoot));
        Assert.Equal(
            Path.Combine(stateRoot, "config.json"),
            OratorioStatePaths.ResolveDefaultConfigurationOverlayPath(contentRoot, stateRoot));
    }

    [Fact]
    public void StatePathDefaults_UseContentRoot_WhenStateRootMissing()
    {
        var root = Directory.CreateTempSubdirectory("oratorio-state-paths-");
        var contentRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "release", "server")).FullName;
        var expectedRoot = Path.GetFullPath(Path.Combine(contentRoot, "..", ".craft", "oratorio"));

        Assert.Equal(
            expectedRoot,
            OratorioStatePaths.ResolveDefaultStateRoot(contentRoot));
        Assert.Equal(
            Path.Combine(expectedRoot, "oratorio.db"),
            OratorioStatePaths.ResolveDefaultDatabasePath(contentRoot));
        Assert.Equal(
            Path.Combine(expectedRoot, "config.json"),
            OratorioStatePaths.ResolveDefaultConfigurationOverlayPath(contentRoot));
    }

    [Fact]
    public async Task ServerConfiguration_DefaultOverlayPath_UsesStateRoot()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();
        var environment = app.Services.GetRequiredService<IWebHostEnvironment>();

        var configuration = await client.GetFromJsonAsync<ServerConfigurationResponse>(
            "/api/v1/settings/server-configuration",
            JsonOptions);

        Assert.NotNull(configuration);
        Assert.Equal(
            OratorioStatePaths.ResolveDefaultConfigurationOverlayPath(
                environment.ContentRootPath,
                Environment.GetEnvironmentVariable("DOTCRAFT_MANAGED_SERVICE_STATE_ROOT")),
            configuration!.OverlayPath);
        Assert.Equal(1800, configuration.Configuration.DotCraft.RunTimeoutSeconds);
    }

    [Fact]
    public async Task ServerConfiguration_ExplicitRunTimeoutOverlay_IsPreserved()
    {
        var root = Directory.CreateTempSubdirectory("oratorio-config-timeout-");
        var overlayPath = Path.Combine(root.FullName, "config.json");
        await File.WriteAllTextAsync(overlayPath, JsonSerializer.Serialize(new
        {
            Oratorio = new
            {
                DotCraft = new
                {
                    RunTimeoutSeconds = 600
                }
            }
        }, JsonOptions));
        await using var app = new TestOratorioApp(settings: new Dictionary<string, string?>
        {
            ["Oratorio:Settings:ConfigPath"] = overlayPath
        });
        var client = app.CreateClient();

        var configuration = await client.GetFromJsonAsync<ServerConfigurationResponse>(
            "/api/v1/settings/server-configuration",
            JsonOptions);

        Assert.NotNull(configuration);
        Assert.Equal(600, configuration!.Configuration.DotCraft.RunTimeoutSeconds);
    }

    [Fact]
    public async Task ServerConfiguration_AutomationLabels_UseExactOverlayValues()
    {
        var root = Directory.CreateTempSubdirectory("oratorio-config-labels-");
        var overlayPath = Path.Combine(root.FullName, "config.json");
        await File.WriteAllTextAsync(overlayPath, JsonSerializer.Serialize(new
        {
            Oratorio = new
            {
                Automation = new
                {
                    AutoDispatchAllowLabels = new[] { "custom-auto" },
                    AutoDispatchBlockLabels = new[] { "custom-block" }
                }
            }
        }, JsonOptions));
        await using var app = new TestOratorioApp(settings: new Dictionary<string, string?>
        {
            ["Oratorio:Settings:ConfigPath"] = overlayPath
        });
        var client = app.CreateClient();

        var configuration = await client.GetFromJsonAsync<ServerConfigurationResponse>(
            "/api/v1/settings/server-configuration",
            JsonOptions);

        Assert.NotNull(configuration);
        Assert.Equal(["custom-auto"], configuration!.Configuration.Automation.AutoDispatchAllowLabels);
        Assert.Equal(["custom-block"], configuration.Configuration.Automation.AutoDispatchBlockLabels);
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
    public async Task ServerConfigurationWrite_AllowedByDefaultForLocalBackend()
    {
        var root = Directory.CreateTempSubdirectory("oratorio-default-write-");
        var overlayPath = Path.Combine(root.FullName, "config.json");
        await using var app = new TestOratorioApp(settings: new Dictionary<string, string?>
        {
            ["Oratorio:Settings:ConfigPath"] = overlayPath
        });
        var client = app.CreateClient();

        var configuration = await client.GetFromJsonAsync<ServerConfigurationResponse>(
            "/api/v1/settings/server-configuration",
            JsonOptions);
        Assert.NotNull(configuration);
        Assert.True(configuration!.Writable);

        var response = await PutAsync<ServerConfigurationUpdateResponse>(
            client,
            "/api/v1/settings/server-configuration",
            new ServerConfigurationUpdateRequest(configuration.Revision, true, configuration.Configuration));

        Assert.True(response.Configuration.Writable);
        Assert.True(File.Exists(overlayPath));
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
                ApprovalPolicy = "default",
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
            return status is { WorkspacePath: var path, ApprovalPolicy: "default", GlobalMaxActiveRuns: 3 }
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

    [Fact]
    public async Task GitHubSync_RequiresAppAuthentication()
    {
        await using var app = new TestOratorioApp(settings: new Dictionary<string, string?>
        {
            ["Oratorio:GitHub:AppId"] = "",
            ["Oratorio:GitHub:PrivateKey"] = "",
            ["Oratorio:GitHub:PrivateKeyPath"] = ""
        });
        var client = app.CreateClient();

        var sync = await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });

        Assert.Equal(["example-owner/oratorio"], sync.RepositoriesScanned);
        Assert.Equal(0, sync.IssuesImported);
        Assert.Equal(0, sync.PullRequestsImported);
        var error = Assert.Single(sync.Errors);
        Assert.Equal("example-owner/oratorio", error.Repository);
        Assert.Equal("githubAppAuthRequired", error.Code);
        Assert.Contains("GitHub App ID and private key", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GitHubSync_ImportsIssuesPullRequestsComments_AndSupportsItemIdRoutes()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var clock = new MutableClock(DateTimeOffset.Parse("2026-05-03T00:00:00Z"));
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(clock);
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
        });
        var client = app.CreateClient();

        var sync = await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });

        Assert.Equal(["example-owner/oratorio"], sync.RepositoriesScanned);
        Assert.Equal(1, sync.IssuesImported);
        Assert.Equal(1, sync.PullRequestsImported);
        Assert.Equal(0, sync.CommentsImported);
        Assert.Equal(1, sync.Skipped);
        Assert.Equal(0, fakeGitHub.IssueCommentCallCount);
        Assert.Equal([GitHubListState.Open], fakeGitHub.IssueStateArguments["example-owner/oratorio"]);
        Assert.Equal([GitHubListState.Open], fakeGitHub.PullRequestStateArguments["example-owner/oratorio"]);

        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        Assert.NotNull(list);
        var issue = Assert.Single(list!.Items, x => x.Kind == ItemKind.Issue);
        Assert.False(string.IsNullOrWhiteSpace(issue.ShortId));
        Assert.InRange(issue.BoardSortOrder, 0, 999);
        var pr = Assert.Single(list.Items, x => x.Kind == ItemKind.PullRequest);
        Assert.Equal("pr:example-owner/oratorio#184", pr.ExternalId);
        Assert.Equal("feature/auth-refresh", pr.Branch);
        Assert.Equal("abc123", pr.HeadSha);
        Assert.Equal(["security", "backend"], pr.Labels);
        Assert.NotNull(pr.ItemId);

        var detail = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{pr.ItemId}", JsonOptions);
        Assert.NotNull(detail);
        Assert.NotNull(detail!.SourceSnapshot);
        Assert.Equal(SourceDetailsStatus.Stale, detail.Item.SourceDetailsStatus);
        Assert.DoesNotContain(detail.Comments, x => x.Source == "github");

        detail = await PostAsync<ItemDetailResponse>(client, $"/api/v1/items/id/{pr.ItemId}/source-details/sync", new { });
        Assert.Equal(SourceDetailsStatus.Current, detail.Item.SourceDetailsStatus);
        Assert.NotNull(detail.SourceSnapshot);
        var firstSnapshotId = detail.SourceSnapshot!.SnapshotId;
        var firstSnapshotSyncedAt = detail.SourceSnapshot.SyncedAt;
        Assert.Contains(detail.Comments, x => x.SourceCommentId == "issue-comment:9001");
        Assert.Contains(detail.Comments, x => x.SourceCommentId == "review:9101");
        Assert.Contains(detail.Comments, x => x.SourceCommentId == "review-comment:9201");
        Assert.Equal(1, fakeGitHub.IssueCommentCallCount);
        Assert.Equal(1, fakeGitHub.PullRequestReviewCallCount);
        Assert.Equal(1, fakeGitHub.PullRequestReviewCommentCallCount);
        Assert.Single(detail.Timeline, x => x.RoundId is null && x.Kind == TimelineEventKind.SourceSynced);

        var bySource = await client.GetFromJsonAsync<ItemDetailResponse>(
            "/api/v1/items/by-source?source=github&externalId=pr%3Aexample-owner%2Foratorio%23184",
            JsonOptions);
        Assert.Equal(pr.ItemId, bySource?.Item.ItemId);

        clock.Advance(TimeSpan.FromMinutes(5));
        var repeatedSync = await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        Assert.Equal(0, repeatedSync.CommentsImported);
        var afterSecondSync = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{pr.ItemId}", JsonOptions);
        Assert.NotNull(afterSecondSync);
        Assert.Equal(3, afterSecondSync!.Comments.Count(x => x.Source == "github"));
        Assert.Single(afterSecondSync.Timeline, x => x.RoundId is null && x.Kind == TimelineEventKind.SourceSynced);
        Assert.Equal(firstSnapshotId, afterSecondSync.SourceSnapshot?.SnapshotId);
        Assert.Equal(detail.Item.LastSourceSyncAt, afterSecondSync.Item.LastSourceSyncAt);
        Assert.Equal(firstSnapshotSyncedAt, afterSecondSync.SourceSnapshot?.SyncedAt);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            Assert.Equal(2, await db.SourceSnapshots.CountAsync(x => x.ItemId == pr.ItemId));
        }

        fakeGitHub.SourceReviewComments[0] = fakeGitHub.SourceReviewComments[0] with
        {
            Body = "Extract the token validation branch and cover retry order.",
            UpdatedAt = DateTimeOffset.Parse("2026-05-03T00:10:00Z")
        };
        fakeGitHub.Issues[1] = fakeGitHub.Issues[1] with { UpdatedAt = DateTimeOffset.Parse("2026-05-03T00:10:00Z") };
        fakeGitHub.PullRequests[0] = fakeGitHub.PullRequests[0] with { UpdatedAt = DateTimeOffset.Parse("2026-05-03T00:10:00Z") };
        clock.Advance(TimeSpan.FromMinutes(5));
        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var afterChangedSync = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{pr.ItemId}", JsonOptions);
        Assert.NotNull(afterChangedSync);
        Assert.Equal(SourceDetailsStatus.Stale, afterChangedSync!.Item.SourceDetailsStatus);
        afterChangedSync = await PostAsync<ItemDetailResponse>(client, $"/api/v1/items/id/{pr.ItemId}/source-details/sync", new { });
        Assert.Equal(3, afterChangedSync!.Comments.Count(x => x.Source == "github"));
        Assert.Contains(afterChangedSync.Comments, x =>
            x.SourceCommentId == "review-comment:9201" &&
            x.Body.Contains("cover retry order", StringComparison.Ordinal) &&
            x.SourceUpdatedAt == DateTimeOffset.Parse("2026-05-03T00:10:00Z"));
        Assert.Equal(2, afterChangedSync.Timeline.Count(x => x.RoundId is null && x.Kind == TimelineEventKind.SourceSynced));
        Assert.Contains(afterChangedSync.Timeline, x => x.Title == "GitHub source updated");
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            Assert.True(await db.SourceSnapshots.CountAsync(x => x.ItemId == pr.ItemId) >= 2);
        }

        var dispatched = await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("mock", "Run mock lifecycle on a GitHub-backed item.", MockOutcome.Success, 1));
        Assert.Equal(ItemState.Dispatching, dispatched.Item.State);
    }

    [Fact]
    public async Task GitHubSyncJob_ImportsRepositoriesIndependently_WhenFirstRepositoryIsSlow()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        fakeGitHub.ListIssueDelays["example-owner/oratorio"] = TimeSpan.FromSeconds(2);
        fakeGitHub.IssuesByRepository["example-owner/second-repo"] = [TestGitHubIssue(3001, 7, "Second repository issue", "example-owner/second-repo")];
        fakeGitHub.PullRequestsByRepository["example-owner/second-repo"] = [];
        await using var app = new TestOratorioApp(
            services =>
            {
                services.RemoveAll<IGitHubApiClient>();
                services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            },
            new Dictionary<string, string?>
            {
                ["Oratorio:GitHub:Repositories:1"] = "example-owner/second-repo"
            });
        var client = app.CreateClient();

        var job = await PostAsync<GitHubSyncJobDto>(client, "/api/v1/sources/github/sync-jobs", new { });

        await WaitUntilAsync(async () =>
        {
            var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
            return list!.Items.Any(x => x.Repository == "example-owner/second-repo");
        });
        var active = await client.GetFromJsonAsync<GitHubSyncJobDto>($"/api/v1/sources/github/sync-jobs/{job.JobId}", JsonOptions);
        Assert.Contains(active!.Repositories, x => x.Repository == "example-owner/second-repo" && x.Status == GitHubSyncRepositoryStatus.Succeeded);

        var completed = await WaitForGitHubSyncJobAsync(client, job.JobId);
        Assert.Equal(GitHubSyncStatus.Succeeded, completed.Status);
        Assert.Equal(["example-owner/oratorio", "example-owner/second-repo"], completed.Repositories.Select(x => x.Repository).ToArray());
    }

    [Fact]
    public async Task GitHubSyncJob_RecordsRepositoryFailures_AndContinuesOtherRepositories()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        fakeGitHub.FailingIssueRepositories.Add("example-owner/oratorio");
        fakeGitHub.IssuesByRepository["example-owner/second-repo"] = [TestGitHubIssue(3001, 7, "Second repository issue", "example-owner/second-repo")];
        fakeGitHub.PullRequestsByRepository["example-owner/second-repo"] = [];
        await using var app = new TestOratorioApp(
            services =>
            {
                services.RemoveAll<IGitHubApiClient>();
                services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            },
            new Dictionary<string, string?>
            {
                ["Oratorio:GitHub:Repositories:1"] = "example-owner/second-repo"
            });
        var client = app.CreateClient();

        var job = await PostAsync<GitHubSyncJobDto>(client, "/api/v1/sources/github/sync-jobs", new { });
        var completed = await WaitForGitHubSyncJobAsync(client, job.JobId);

        Assert.Equal(GitHubSyncStatus.PartialFailed, completed.Status);
        Assert.Contains(completed.Repositories, x => x.Repository == "example-owner/oratorio" && x.Status == GitHubSyncRepositoryStatus.Failed && x.ErrorCode == "githubSyncFailed");
        Assert.Contains(completed.Repositories, x => x.Repository == "example-owner/second-repo" && x.Status == GitHubSyncRepositoryStatus.Succeeded);
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        Assert.Contains(list!.Items, x => x.Repository == "example-owner/second-repo");
    }

    [Fact]
    public async Task GitHubSyncJob_RecordsRepositoryTimeouts_AndClearsActiveJob()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        fakeGitHub.CancelingIssueRepositories.Add("example-owner/oratorio");
        fakeGitHub.IssuesByRepository["example-owner/second-repo"] = [TestGitHubIssue(3001, 7, "Second repository issue", "example-owner/second-repo")];
        fakeGitHub.PullRequestsByRepository["example-owner/second-repo"] = [];
        await using var app = new TestOratorioApp(
            services =>
            {
                services.RemoveAll<IGitHubApiClient>();
                services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            },
            new Dictionary<string, string?>
            {
                ["Oratorio:GitHub:Repositories:1"] = "example-owner/second-repo"
            });
        var client = app.CreateClient();

        var job = await PostAsync<GitHubSyncJobDto>(client, "/api/v1/sources/github/sync-jobs", new { });
        var completed = await WaitForGitHubSyncJobAsync(client, job.JobId);

        Assert.Equal(GitHubSyncStatus.PartialFailed, completed.Status);
        var failed = Assert.Single(completed.Repositories, x => x.Repository == "example-owner/oratorio");
        Assert.Equal(GitHubSyncRepositoryStatus.Failed, failed.Status);
        Assert.Equal(GitHubSyncRepositoryPhase.Failed, failed.Phase);
        Assert.Equal("githubSyncFailed", failed.ErrorCode);
        Assert.Contains("timeout", failed.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Contains(completed.Repositories, x => x.Repository == "example-owner/second-repo" && x.Status == GitHubSyncRepositoryStatus.Succeeded);

        var activeResponse = await client.GetAsync("/api/v1/sources/github/sync-jobs/active");
        Assert.Equal(HttpStatusCode.OK, activeResponse.StatusCode);
        var activeBody = await activeResponse.Content.ReadAsStringAsync();
        Assert.True(string.IsNullOrWhiteSpace(activeBody) || activeBody == "null", activeBody);
    }

    [Fact]
    public async Task GitHubSyncJob_ImportsRepositoryWithOnlyPullRequestShapedIssue()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        fakeGitHub.IssuesByRepository["example-org/unity-example"] =
        [
            TestGitHubIssue(4001, 1, "Declare Unity runtime tools", "example-org/unity-example", isPullRequest: true)
        ];
        fakeGitHub.PullRequestsByRepository["example-org/unity-example"] =
        [
            TestGitHubPullRequest(5001, 1, "Declare Unity runtime tools", "example-org/unity-example")
        ];
        await using var app = new TestOratorioApp(
            services =>
            {
                services.RemoveAll<IGitHubApiClient>();
                services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            },
            new Dictionary<string, string?>
            {
                ["Oratorio:GitHub:Repositories:0"] = "example-org/unity-example"
            });
        var client = app.CreateClient();

        var sync = await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });

        Assert.Equal(["example-org/unity-example"], sync.RepositoriesScanned);
        Assert.Equal(0, sync.IssuesImported);
        Assert.Equal(1, sync.PullRequestsImported);
        Assert.Equal(1, sync.Skipped);
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items);
        Assert.Equal(ItemKind.PullRequest, pr.Kind);
        Assert.Equal("pr:example-org/unity-example#1", pr.ExternalId);
    }

    [Fact]
    public async Task GitHubSync_AutoArchivesClosedAndMergedSourceItems()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        fakeGitHub.Issues.Add(new GitHubIssue(
            1003,
            77,
            "Closed source issue",
            "Already closed upstream.",
            "closed",
            "https://github.example.test/example-owner/oratorio/issues/77",
            new GitHubUser("example-author"),
            [],
            [],
            DateTimeOffset.Parse("2026-05-01T10:00:00Z"),
            DateTimeOffset.Parse("2026-05-03T10:00:00Z"),
            DateTimeOffset.Parse("2026-05-03T09:55:00Z"),
            null));
        fakeGitHub.PullRequests.Add(new GitHubPullRequest(
            2002,
            185,
            "Merged source PR",
            "Already merged upstream.",
            "closed",
            "https://github.example.test/example-owner/oratorio/pull/185",
            new GitHubUser("example-author"),
            [],
            [],
            DateTimeOffset.Parse("2026-05-01T11:00:00Z"),
            DateTimeOffset.Parse("2026-05-03T12:00:00Z"),
            DateTimeOffset.Parse("2026-05-03T11:30:00Z"),
            DateTimeOffset.Parse("2026-05-03T11:45:00Z"),
            false,
            new GitHubBranchRef("feature/merged", "def456"),
            new GitHubBranchRef("main", "base123")));
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
        });
        var client = app.CreateClient();

        var lightJob = await PostAsync<GitHubSyncJobDto>(client, "/api/v1/sources/github/sync-jobs", new { });
        await WaitForGitHubSyncJobAsync(client, lightJob.JobId);
        var lightList = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github&includeArchived=true", JsonOptions);
        Assert.DoesNotContain(lightList!.Items, x => x.ExternalId == "issue:example-owner/oratorio#77");
        Assert.DoesNotContain(lightList.Items, x => x.ExternalId == "pr:example-owner/oratorio#185");
        Assert.Equal(0, fakeGitHub.IssueCommentCallCount);

        var fullJob = await PostAsync<GitHubSyncJobDto>(client, "/api/v1/sources/github/sync-jobs", new { mode = "full" });
        await WaitForGitHubSyncJobAsync(client, fullJob.JobId);

        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github&includeArchived=true", JsonOptions);
        var closedIssue = Assert.Single(list!.Items, x => x.ExternalId == "issue:example-owner/oratorio#77");
        var mergedPr = Assert.Single(list.Items, x => x.ExternalId == "pr:example-owner/oratorio#185");
        Assert.Equal(ItemState.Archived, closedIssue.State);
        Assert.Equal(SourceState.Closed, closedIssue.SourceState);
        Assert.Equal(ArchiveReason.SourceClosed, closedIssue.ArchiveReason);
        Assert.NotNull(closedIssue.SourceClosedAt);
        Assert.Equal(ItemState.Archived, mergedPr.State);
        Assert.Equal(SourceState.Merged, mergedPr.SourceState);
        Assert.Equal(ArchiveReason.SourceMerged, mergedPr.ArchiveReason);
        Assert.NotNull(mergedPr.SourceClosedAt);
        Assert.NotNull(mergedPr.SourceMergedAt);

        var closedIssueIndex = fakeGitHub.Issues.FindIndex(x => x.Number == 77);
        fakeGitHub.Issues[closedIssueIndex] = fakeGitHub.Issues[closedIssueIndex] with
        {
            State = "open",
            UpdatedAt = DateTimeOffset.UtcNow,
            ClosedAt = null
        };
        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var reopenedList = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var reopenedIssue = Assert.Single(reopenedList!.Items, x => x.ExternalId == "issue:example-owner/oratorio#77");
        Assert.Equal(ItemState.Discovered, reopenedIssue.State);
        Assert.Equal(SourceState.Open, reopenedIssue.SourceState);
        Assert.Null(reopenedIssue.ArchiveReason);
    }

    [Fact]
    public async Task GitHubDispatch_HydratesStaleSourceDetailsBeforeQueueing()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);
        Assert.Equal(SourceDetailsStatus.Stale, pr.SourceDetailsStatus);

        var dispatched = await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("mock", "Dispatch after source detail hydrate.", MockOutcome.Success, 1));

        Assert.Equal(ItemState.Dispatching, dispatched.Item.State);
        Assert.Equal(SourceDetailsStatus.Current, dispatched.Item.SourceDetailsStatus);
        Assert.Contains(dispatched.Comments, x => x.SourceCommentId == "review-comment:9201");
        Assert.Equal(1, fakeGitHub.IssueCommentCallCount);
        Assert.Equal(1, fakeGitHub.PullRequestReviewCallCount);
        Assert.Equal(1, fakeGitHub.PullRequestReviewCommentCallCount);
    }

    [Fact]
    public async Task GitHubSourceDetailsSync_FailureIsRecorded_AndBlocksDispatch()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);
        fakeGitHub.FailSourceDetailReads = true;

        var hydrateResponse = await client.PostAsJsonAsync($"/api/v1/items/id/{pr.ItemId}/source-details/sync", new { }, JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, hydrateResponse.StatusCode);
        var hydrateError = await hydrateResponse.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        Assert.Equal("sourceDetailsSyncFailed", hydrateError?.Error.Code);
        var failedDetail = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{pr.ItemId}", JsonOptions);
        Assert.Equal(SourceDetailsStatus.Failed, failedDetail!.Item.SourceDetailsStatus);
        Assert.Equal("sourceDetailsSyncFailed", failedDetail.Item.SourceDetailsErrorCode);

        var dispatchResponse = await client.PostAsJsonAsync(
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("mock", "Dispatch should block.", MockOutcome.Success, 1),
            JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, dispatchResponse.StatusCode);
        var dispatchError = await dispatchResponse.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        Assert.Equal("sourceDetailsSyncFailed", dispatchError?.Error.Code);
    }

    [Fact]
    public async Task SourceBackedItem_CanBeManuallyArchivedAndReopened()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        var archived = await PostAsync<ItemDetailResponse>(client, $"/api/v1/items/id/{pr.ItemId}/archive", new { });
        Assert.Equal(ItemState.Archived, archived.Item.State);
        Assert.Equal(ArchiveReason.Manual, archived.Item.ArchiveReason);

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var afterSync = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github&includeArchived=true", JsonOptions);
        var stillArchived = Assert.Single(afterSync!.Items, x => x.ExternalId == pr.ExternalId);
        Assert.Equal(ItemState.Archived, stillArchived.State);
        Assert.Equal(ArchiveReason.Manual, stillArchived.ArchiveReason);

        var reopened = await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/reopen",
            new DecisionRequest("Return it to queue."));
        Assert.Equal(ItemState.Discovered, reopened.Item.State);
        Assert.Null(reopened.Item.ArchiveReason);
    }

    [Fact]
    public async Task DotCraftStatus_UsesHealthInsteadOfConfiguredAsReady()
    {
        var workspace = Directory.CreateTempSubdirectory("oratorio-status-workspace-").FullName;
        await using var app = new TestOratorioApp(
            services =>
            {
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.AddSingleton<IDotCraftAppServerProcessManager>(
                    new FakeDotCraftProcessManager(connected: false, endpoint: new DotCraftAppServerEndpoint("ws://127.0.0.1:9100/ws", "manual")));
            },
            new Dictionary<string, string?>
            {
                ["Oratorio:DotCraft:RepositoryWorkspaceRoutes:0:Project"] = "github:github.com/example-owner/oratorio",
                ["Oratorio:DotCraft:RepositoryWorkspaceRoutes:0:WorkspacePath"] = workspace
            });
        var client = app.CreateClient();

        var status = await client.GetFromJsonAsync<DotCraftStatusResponse>("/api/v1/dotcraft/status", JsonOptions);

        Assert.NotNull(status);
        Assert.True(status!.Configured);
        Assert.False(status.Connected);
        Assert.Equal("configured", status.Health);
        Assert.True(status.ManagedWorktreesEnabled);
        Assert.Equal("<repositoryWorkspace>/.craft/oratorio/worktrees", status.WorktreeRootPolicy);
        Assert.Equal(2, status.GlobalMaxActiveRuns);
        Assert.Equal(1, status.MaxActiveRunsPerRepository);
        Assert.Equal(2, status.MaxActiveRunsPerSource);
        Assert.Contains("not reachable", status.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DotCraftWorkspaces_ReturnsInventoryAndStatusUsesMultiWorkspaceMode()
    {
        var root = Directory.CreateTempSubdirectory("oratorio-api-workspaces-");
        var mappedWorkspace = Directory.CreateDirectory(Path.Combine(root.FullName, "mapped")).FullName;
        var secondWorkspace = Directory.CreateDirectory(Path.Combine(root.FullName, "second")).FullName;
        var fakeProcess = new FakeDotCraftProcessManager(
            connected: false,
            endpoint: new DotCraftAppServerEndpoint("ws://127.0.0.1:9200/ws", "hub"));
        await using var app = new TestOratorioApp(
            services =>
            {
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.AddSingleton<IDotCraftAppServerProcessManager>(fakeProcess);
            },
            new Dictionary<string, string?>
            {
                ["Oratorio:DotCraft:RepositoryWorkspaceRoutes:0:Project"] = "github:github.com/example-owner/oratorio",
                ["Oratorio:DotCraft:RepositoryWorkspaceRoutes:0:WorkspacePath"] = mappedWorkspace,
                ["Oratorio:DotCraft:RepositoryWorkspaceRoutes:1:Project"] = "github:github.com/example-owner/desktop-repo",
                ["Oratorio:DotCraft:RepositoryWorkspaceRoutes:1:WorkspacePath"] = secondWorkspace
            });
        var client = app.CreateClient();

        try
        {
            using var status = await client.GetFromJsonAsync<JsonDocument>("/api/v1/status", JsonOptions);
            Assert.Equal("multi", status!.RootElement.GetProperty("workspaceMode").GetString());
            Assert.True(status.RootElement.GetProperty("capabilities").GetProperty("multiWorkspaceRouting").GetBoolean());

            var workspaces = await client.GetFromJsonAsync<DotCraftWorkspacesResponse>("/api/v1/dotcraft/workspaces", JsonOptions);
            Assert.NotNull(workspaces);
            Assert.Equal(2, workspaces!.Summary.Total);
            Assert.Equal(0, workspaces.Summary.Connected);
            Assert.All(workspaces.Workspaces, workspace => Assert.False(workspace.IsDefault));
            var mapped = Assert.Single(workspaces.Workspaces, workspace => workspace.Path == mappedWorkspace);
            Assert.Equal("hub", mapped.EndpointSource);
            Assert.Equal(["github:github.com/example-owner/oratorio"], mapped.Repositories);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task DotCraftAppServerStart_EnsuresEveryConfiguredWorkspace()
    {
        var root = Directory.CreateTempSubdirectory("oratorio-start-workspaces-");
        var firstWorkspace = Directory.CreateDirectory(Path.Combine(root.FullName, "first")).FullName;
        var secondWorkspace = Directory.CreateDirectory(Path.Combine(root.FullName, "second")).FullName;
        var fakeProcess = new FakeDotCraftProcessManager(
            connected: false,
            endpoint: new DotCraftAppServerEndpoint("ws://127.0.0.1:9200/ws", "hub"));
        await using var app = new TestOratorioApp(
            services =>
            {
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.AddSingleton<IDotCraftAppServerProcessManager>(fakeProcess);
            },
            new Dictionary<string, string?>
            {
                ["Oratorio:DotCraft:RepositoryWorkspaceRoutes:0:Project"] = "github:github.com/example-owner/oratorio",
                ["Oratorio:DotCraft:RepositoryWorkspaceRoutes:0:WorkspacePath"] = firstWorkspace,
                ["Oratorio:DotCraft:RepositoryWorkspaceRoutes:1:Project"] = "github:github.com/example-owner/desktop-repo",
                ["Oratorio:DotCraft:RepositoryWorkspaceRoutes:1:WorkspacePath"] = secondWorkspace
            });
        var client = app.CreateClient();

        try
        {
            var status = await PostAsync<DotCraftStatusResponse>(
                client,
                "/api/v1/dotcraft/appserver/start",
                new { });

            Assert.NotNull(status);
            Assert.Equal(2, fakeProcess.EnsureWorkspacePaths.Count);
            Assert.Contains(firstWorkspace, fakeProcess.EnsureWorkspacePaths);
            Assert.Contains(secondWorkspace, fakeProcess.EnsureWorkspacePaths);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task GitHubPullRequestDispatch_CreatesInProgressReviewGateCheck()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        var dispatched = await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("mock", "Run GitHub PR review.", MockOutcome.Success, 5));

        var check = Assert.Single(fakeGitHub.CheckRuns);
        Assert.Equal("oratorio/review", check.Name);
        Assert.Equal("abc123", check.HeadSha);
        Assert.Equal("in_progress", check.Status);
        Assert.Null(check.Conclusion);
        Assert.Contains(dispatched.SourceWrites, write =>
            write.Kind == SourceWriteKind.CheckRun &&
            write.Intent == "reviewGateStart" &&
            write.Status == SourceWriteStatus.Succeeded);
    }

    [Fact]
    public async Task GitHubPullRequestCancelRun_CompletesReviewGateCheckAsCancelled()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("mock", "Run GitHub PR review until cancelled.", MockOutcome.Success, 30));

        var cancelled = await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/cancel-run",
            new CancelRunRequest("No longer needed."));

        var check = Assert.Single(fakeGitHub.CheckRuns);
        Assert.Equal("completed", check.Status);
        Assert.Equal("cancelled", check.Conclusion);
        Assert.Equal(1, check.UpdateCount);
        Assert.Contains(cancelled.SourceWrites, write =>
            write.Kind == SourceWriteKind.CheckRun &&
            write.Intent == "reviewGateRunCancelled" &&
            write.Status == SourceWriteStatus.Succeeded);
    }

    [Fact]
    public async Task GitHubPullRequestApprove_WritesReviewAndCheckRun()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
        });
        var client = app.CreateClient();

        var pr = await CreateGitHubPullRequestInReviewAsync(client);
        var approved = await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.Item.ItemId}/approve",
            new DecisionRequest("Ship it from Oratorio."));

        Assert.Equal(ItemState.Approved, approved.Item.State);
        var review = Assert.Single(fakeGitHub.PullRequestReviews);
        Assert.Equal("APPROVE", review.Event);
        Assert.Equal("Ship it from Oratorio.", review.Body);
        Assert.Equal("example-owner/oratorio", review.Repository.FullName);
        Assert.Equal(184, review.Number);

        var check = Assert.Single(fakeGitHub.CheckRuns);
        Assert.Equal("oratorio/review", check.Name);
        Assert.Equal("abc123", check.HeadSha);
        Assert.Equal("completed", check.Status);
        Assert.Equal("success", check.Conclusion);
        Assert.Equal(2, check.UpdateCount);
        Assert.Equal(4, approved.SourceWrites.Count);
        Assert.All(approved.SourceWrites, write => Assert.Equal(SourceWriteStatus.Succeeded, write.Status));
    }

    [Fact]
    public async Task GitHubPullRequestRequestChanges_WritesReviewAndActionRequiredCheck()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
        });
        var client = app.CreateClient();

        var pr = await CreateGitHubPullRequestInReviewAsync(client);
        var changed = await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.Item.ItemId}/request-changes",
            new DecisionRequest("Please add regression coverage."));

        Assert.Equal(ItemState.Discovered, changed.Item.State);
        var review = Assert.Single(fakeGitHub.PullRequestReviews);
        Assert.Equal("REQUEST_CHANGES", review.Event);
        Assert.Contains("Please add regression coverage.", review.Body);
        var check = Assert.Single(fakeGitHub.CheckRuns);
        Assert.Equal("completed", check.Status);
        Assert.Equal("action_required", check.Conclusion);
        Assert.Contains("Please add regression coverage.", check.Summary);
    }

    [Fact]
    public async Task GitHubPullRequestReject_WritesReviewAndFailureCheck()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
        });
        var client = app.CreateClient();

        var pr = await CreateGitHubPullRequestInReviewAsync(client);
        var rejected = await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.Item.ItemId}/reject",
            new DecisionRequest("Rejected because the approach is unsafe."));

        Assert.Equal(ItemState.Rejected, rejected.Item.State);
        var review = Assert.Single(fakeGitHub.PullRequestReviews);
        Assert.Equal("REQUEST_CHANGES", review.Event);
        Assert.Contains("Rejected", review.Body, StringComparison.OrdinalIgnoreCase);
        var check = Assert.Single(fakeGitHub.CheckRuns);
        Assert.Equal("completed", check.Status);
        Assert.Equal("failure", check.Conclusion);
        Assert.Contains("Rejected", check.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GitHubIssueDecision_WritesIssueCommentOnly()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
        });
        var client = app.CreateClient();

        var issue = await CreateGitHubIssueInReviewAsync(client);
        var approved = await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{issue.Item.ItemId}/approve",
            new DecisionRequest("Issue review accepted."));

        Assert.Equal(ItemState.Approved, approved.Item.State);
        var comment = Assert.Single(fakeGitHub.IssueComments);
        Assert.Equal("example-owner/oratorio", comment.Repository.FullName);
        Assert.Equal(42, comment.Number);
        Assert.Contains("Issue review accepted.", comment.Body);
        Assert.Empty(fakeGitHub.PullRequestReviews);
        Assert.Empty(fakeGitHub.CheckRuns);
        var write = Assert.Single(approved.SourceWrites);
        Assert.Equal(SourceWriteKind.IssueComment, write.Kind);
        Assert.Equal(SourceWriteStatus.Succeeded, write.Status);
    }

    [Fact]
    public async Task GitHubWriteFailure_IsAuditedAndRetryable()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
        });
        var client = app.CreateClient();

        var pr = await CreateGitHubPullRequestInReviewAsync(client);
        fakeGitHub.FailNextWriteCount = 1;
        var approved = await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.Item.ItemId}/approve",
            new DecisionRequest("Approve even if the first write fails."));

        Assert.Equal(ItemState.Approved, approved.Item.State);
        var failed = Assert.Single(approved.SourceWrites, write => write.Status == SourceWriteStatus.Failed);
        Assert.Equal("githubWriteFailed", failed.ErrorCode);
        Assert.Contains(approved.Timeline, x => x.Kind == TimelineEventKind.SourceWriteFailed);

        var retried = await PostAsync<ItemDetailResponse>(client, $"/api/v1/source-writes/{failed.WriteId}/retry", new { });
        var writeAfterRetry = Assert.Single(retried.SourceWrites, write => write.WriteId == failed.WriteId);
        Assert.Equal(SourceWriteStatus.Succeeded, writeAfterRetry.Status);
        Assert.Equal(2, writeAfterRetry.AttemptCount);
    }

    [Fact]
    public async Task GitHubWritesDisabled_RecordsBlockedWriteWithoutCallingGitHub()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IOptionsMonitor<GitHubOptions>>();
            services.AddSingleton<IOptionsMonitor<GitHubOptions>>(new StaticOptionsMonitor<GitHubOptions>(new GitHubOptions
            {
                Endpoint = "https://api.github.test",
                AppId = "12345",
                PrivateKey = "test-private-key",
                Repositories = ["example-owner/oratorio"],
                WritesEnabled = false
            }));
        });
        var client = app.CreateClient();

        var pr = await CreateGitHubPullRequestInReviewAsync(client);
        var approved = await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.Item.ItemId}/approve",
            new DecisionRequest("Decision should remain local."));

        Assert.Equal(ItemState.Approved, approved.Item.State);
        Assert.Empty(fakeGitHub.PullRequestReviews);
        Assert.Empty(fakeGitHub.CheckRuns);
        Assert.All(approved.SourceWrites, write =>
        {
            Assert.Equal(SourceWriteStatus.Failed, write.Status);
            Assert.Equal("githubWritesDisabled", write.ErrorCode);
        });
    }

    [Fact]
    public async Task LocalTaskDecision_DoesNotCreateGitHubWrites()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
        });
        var client = app.CreateClient();

        var task = await CreateLocalTaskAsync(client);
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{task.Item.ItemId}/dispatch",
            new DispatchRequest("mock", "Run local task.", MockOutcome.Success, 1));
        var reviewed = await WaitForItemByIdAsync(client, task.Item.ItemId, x => x.Item.State == ItemState.AwaitingReview);
        var approved = await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{reviewed.Item.ItemId}/approve",
            new DecisionRequest("Local task done."));

        Assert.Empty(approved.SourceWrites);
        Assert.Empty(fakeGitHub.IssueComments);
        Assert.Empty(fakeGitHub.PullRequestReviews);
        Assert.Empty(fakeGitHub.CheckRuns);
    }

    [Fact]
    public async Task GitHubWebhook_RejectsInvalidSignature_AndSyncsValidPayload()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
        });
        var client = app.CreateClient();
        const string payload = """{"repository":{"full_name":"example-owner/oratorio"}}""";

        var invalid = new HttpRequestMessage(HttpMethod.Post, "/api/v1/sources/github/webhook")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        invalid.Headers.TryAddWithoutValidation("X-Hub-Signature-256", "sha256=bad");
        var invalidResponse = await client.SendAsync(invalid);
        Assert.Equal(HttpStatusCode.Forbidden, invalidResponse.StatusCode);

        var valid = new HttpRequestMessage(HttpMethod.Post, "/api/v1/sources/github/webhook")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        valid.Headers.TryAddWithoutValidation("X-Hub-Signature-256", TestHelpers.Signature(payload, "test-secret"));
        var validResponse = await client.SendAsync(valid);
        validResponse.EnsureSuccessStatusCode();
        var job = await validResponse.Content.ReadFromJsonAsync<GitHubSyncJobDto>(JsonOptions);
        Assert.NotNull(job);
        Assert.Equal(GitHubSyncTrigger.Webhook, job.Trigger);
        Assert.Equal(["example-owner/oratorio"], job.Repositories.Select(x => x.Repository).ToArray());
        var completed = await WaitForGitHubSyncJobAsync(client, job.JobId);
        Assert.Equal(1, completed.PullRequestsImported);
    }

    [Fact]
    public async Task AppServerDispatch_CompletesRun_AndCreatesThread()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        var fakeProcess = new FakeDotCraftProcessManager();
        var fakeWorktree = new FakeWorktreeManager();
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.RemoveAll<IWorktreeManager>();
            services.AddSingleton<IDotCraftAppServerProcessManager>(fakeProcess);
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            services.AddSingleton<IWorktreeManager>(fakeWorktree);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        var dispatched = await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Operator wants a read-only DotCraft pass.", null, null));

        Assert.Equal(ItemState.Dispatching, dispatched.Item.State);
        Assert.Contains(dispatched.Runs, x => x.RunnerKind == "appServer" && x.Status is (RunStatus.Queued or RunStatus.Dispatching or RunStatus.Running));

        var reviewed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.AwaitingReview);
        var run = Assert.Single(reviewed.Runs, x => x.RunnerKind == "appServer");
        Assert.Equal(RunStatus.Succeeded, run.Status);
        Assert.Equal("thread-test-1", run.ThreadId);
        Assert.Equal("turn-test-1", run.TurnId);
        Assert.Contains("DotCraft analysis complete", run.Summary);
        Assert.Equal(WorktreeStatus.CleanupPending, run.WorktreeStatus);
        Assert.False(string.IsNullOrWhiteSpace(run.BaseWorkspacePath));
        Assert.Equal($"local:{Path.GetFullPath(run.BaseWorkspacePath)}", run.AppServerIdentity);
        Assert.False(string.IsNullOrWhiteSpace(run.WorktreePath));
        Assert.StartsWith("oratorio/run/", run.WorktreeBranch);
        Assert.Equal("abc123", run.BaseRef);
        Assert.Equal("abc123", run.BaseSha);
        var prepareRequest = Assert.Single(fakeWorktree.PrepareRequests);
        Assert.Equal("refs/pull/184/head", prepareRequest.ReviewTargetFetchRef);
        Assert.Equal(run.BaseWorkspacePath, fakeProcess.EnsureWorkspacePaths.Single());
        Assert.Equal(run.BaseWorkspacePath, fakeAppServer.LastThreadStartRequest?.BaseWorkspacePath);
        Assert.Equal(run.WorktreePath, fakeAppServer.LastThreadStartRequest?.ExecutionWorkspacePath);
        Assert.Equal(CheckState.Attention, reviewed.Item.CheckState);
        Assert.Contains(reviewed.Timeline, x => x.Title == "DotCraft thread created");
        Assert.Contains(reviewed.Timeline, x => x.Title == "Agent summary captured");

        Assert.NotNull(fakeAppServer.LastThreadStartRequest?.RuntimeAdditionalContext);
        Assert.True((fakeAppServer.LastThreadStartRequest?.DynamicTools ?? []).ContainsTool("oratorio_run", "SubmitReviewDraft"));
        var reviewInstructions = fakeAppServer.LastThreadStartRequest!.RuntimeAdditionalContext!["oratorio.reviewDraft"].Value;
        Assert.Contains("kind: suggestion", reviewInstructions);
        Assert.Contains("Never send the retired nested suggestion or commentOnly objects", reviewInstructions);
    }

    [Fact]
    public async Task AppServerDispatch_ContinuesWhenDrawerItemPublishFails()
    {
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.DrawerItemPublishFailure);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await CreateItemAsync(client, "task:test-drawer-publish-failure");
        await PostAsync<ItemDetailResponse>(
            client,
            "/api/v1/items/local/task:test-drawer-publish-failure/dispatch",
            new DispatchRequest("appServer", "Complete despite drawer publish failure.", null, null));

        var reviewed = await WaitForItemAsync(client, "task:test-drawer-publish-failure", x => x.Item.State == ItemState.AwaitingReview);
        var run = Assert.Single(reviewed.Runs, x => x.RunnerKind == "appServer");
        Assert.Equal(RunStatus.Succeeded, run.Status);
        Assert.NotEqual("appServerFailed", run.ErrorCode);
        Assert.Contains("DotCraft analysis complete", run.Summary);
        Assert.Equal(1, fakeAppServer.StartThreadCount);
    }

    [Fact]
    public async Task CancelRun_InterruptsActiveAppServerTurn_AndKeepsRunCancelled()
    {
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.Hold);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await CreateItemAsync(client, "task:test-cancel-appserver");
        await PostAsync<ItemDetailResponse>(
            client,
            "/api/v1/items/local/task:test-cancel-appserver/dispatch",
            new DispatchRequest("appServer", "Hold until cancellation.", null, null));

        var running = await WaitForItemAsync(client, "task:test-cancel-appserver", x => x.Item.State == ItemState.Running);
        var runId = running.Item.CurrentRunId!;

        var cancelled = await PostAsync<ItemDetailResponse>(
            client,
            "/api/v1/items/local/task:test-cancel-appserver/cancel-run",
            new CancelRunRequest("Stop this AppServer run."));

        Assert.Equal(ItemState.Discovered, cancelled.Item.State);
        Assert.Null(cancelled.Item.CurrentRunId);
        Assert.Contains(cancelled.Runs, x => x.RunId == runId && x.Status == RunStatus.Cancelled);
        Assert.Contains(fakeAppServer.InterruptedTurns, x => x.ThreadId == "thread-test-1" && x.TurnId == "turn-test-1");

        await Task.Delay(300);
        var afterCancelledNotification = await client.GetFromJsonAsync<ItemDetailResponse>("/api/v1/items/local/task:test-cancel-appserver", JsonOptions);
        Assert.Equal(ItemState.Discovered, afterCancelledNotification!.Item.State);
        Assert.Contains(afterCancelledNotification.Runs, x => x.RunId == runId && x.Status == RunStatus.Cancelled);
        Assert.DoesNotContain(afterCancelledNotification.Runs, x => x.RunId == runId && x.Status == RunStatus.Failed);
    }

    [Fact]
    public async Task CancelRun_RemainsAuthoritativeWhenReviewDraftAlreadyExists()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.Hold);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Hold after a Review Draft is stored.", null, null));
        var running = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.Running);
        var run = Assert.Single(running.Runs, x => x.RunId == running.Item.CurrentRunId);

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            var now = DateTimeOffset.UtcNow;
            db.ReviewDrafts.Add(new OratorioReviewDraft
            {
                ItemId = pr.ItemId!,
                RoundId = run.RoundId,
                RunId = run.RunId,
                Status = ReviewDraftStatus.Draft,
                SummaryBody = "No issues found.",
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync();
        }

        var cancelled = await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/cancel-run",
            new CancelRunRequest("Operator cancellation wins."));
        await Task.Delay(300);
        var afterNotification = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{pr.ItemId}", JsonOptions);

        Assert.Equal(ItemState.Discovered, cancelled.Item.State);
        Assert.Equal(RunStatus.Cancelled, Assert.Single(afterNotification!.Runs, x => x.RunId == run.RunId).Status);
        Assert.Equal("operatorCancelled", Assert.Single(afterNotification.Runs, x => x.RunId == run.RunId).ErrorCode);
        Assert.Single(afterNotification.ReviewDrafts);
        Assert.DoesNotContain(afterNotification.Timeline, x => x.Title == "Review draft retained after AppServer termination");
        Assert.Equal("cancelled", Assert.Single(fakeGitHub.CheckRuns).Conclusion);
    }

    [Fact]
    public async Task AppServerDispatch_RunTimeoutInterruptsTurn_AndKeepsRunTimedOut()
    {
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.TimeoutAfterTurnStarted);
        await using var app = new TestOratorioApp(
            services =>
            {
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            },
            new Dictionary<string, string?>
            {
                ["Oratorio:DotCraft:MaxRunAttempts"] = "1"
            });
        var client = app.CreateClient();

        await CreateItemAsync(client, "task:test-appserver-timeout-interrupt");
        await PostAsync<ItemDetailResponse>(
            client,
            "/api/v1/items/local/task:test-appserver-timeout-interrupt/dispatch",
            new DispatchRequest("appServer", "Timeout and interrupt this DotCraft run.", null, null));

        var timedOut = await WaitForItemAsync(client, "task:test-appserver-timeout-interrupt", x => x.Item.State == ItemState.Failed);
        var run = Assert.Single(timedOut.Runs, x => x.RunnerKind == "appServer");
        Assert.Equal(RunStatus.TimedOut, run.Status);
        Assert.Equal("appServerTimedOut", run.ErrorCode);
        Assert.Contains("interrupted and cancelled", run.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(fakeAppServer.InterruptedTurns, x => x.ThreadId == "thread-test-1" && x.TurnId == "turn-test-1");
        Assert.DoesNotContain(timedOut.Runs, x => x.ErrorCode == "appServerTurnCancelled");
    }

    [Fact]
    public async Task AppServerDispatch_RunTimeoutCompletesWhenInterruptHasNoTerminalNotification()
    {
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.TimeoutAfterTurnStartedWithoutTerminal);
        await using var app = new TestOratorioApp(
            services =>
            {
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            },
            new Dictionary<string, string?>
            {
                ["Oratorio:DotCraft:MaxRunAttempts"] = "1"
            });
        var client = app.CreateClient();

        await CreateItemAsync(client, "task:test-appserver-timeout-no-terminal");
        await PostAsync<ItemDetailResponse>(
            client,
            "/api/v1/items/local/task:test-appserver-timeout-no-terminal/dispatch",
            new DispatchRequest("appServer", "Timeout and simulate missing terminal notification.", null, null));

        var timedOut = await WaitForItemAsync(client, "task:test-appserver-timeout-no-terminal", x => x.Item.State == ItemState.Failed);
        var run = Assert.Single(timedOut.Runs, x => x.RunnerKind == "appServer");
        Assert.Equal(RunStatus.TimedOut, run.Status);
        Assert.Equal("appServerTimedOut", run.ErrorCode);
        Assert.Contains("no terminal notification", run.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(fakeAppServer.InterruptedTurns, x => x.ThreadId == "thread-test-1" && x.TurnId == "turn-test-1");
    }

    [Fact]
    public async Task AppServerDispatch_MissingBaseWorkspaceFailsWithoutStartingThread()
    {
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.Success);
        var fakeProcess = new FakeDotCraftProcessManager();
        var fakeWorktree = new FakeWorktreeManager
        {
            Failure = new WorktreeException("baseWorkspaceMissing", "Base workspace is missing.")
        };
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.RemoveAll<IWorktreeManager>();
            services.AddSingleton<IDotCraftAppServerProcessManager>(fakeProcess);
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            services.AddSingleton<IWorktreeManager>(fakeWorktree);
        });
        var client = app.CreateClient();

        await CreateItemAsync(client, "task:test-missing-base-workspace");
        await PostAsync<ItemDetailResponse>(
            client,
            "/api/v1/items/local/task:test-missing-base-workspace/dispatch",
            new DispatchRequest("appServer", "Try AppServer with no configured checkout.", null, null));

        var failed = await WaitForItemAsync(client, "task:test-missing-base-workspace", x => x.Item.State == ItemState.Failed);
        var run = Assert.Single(failed.Runs, x => x.RunnerKind == "appServer");
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal("baseWorkspaceMissing", run.ErrorCode);
        Assert.Equal(WorktreeStatus.Failed, run.WorktreeStatus);
        Assert.Equal("baseWorkspaceMissing", run.WorktreeErrorCode);
        Assert.Empty(fakeProcess.EnsureWorkspacePaths);
        Assert.Equal(0, fakeAppServer.StartThreadCount);
    }

    [Fact]
    public async Task AppServerScheduler_RespectsPerRepositoryConcurrencyLimit()
    {
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.Hold);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        var first = await CreateLocalTaskAsync(client, "AppServer hold one", repository: "example-owner/oratorio");
        var second = await CreateLocalTaskAsync(client, "AppServer hold two", repository: "example-owner/oratorio");
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{first.Item.ItemId}/dispatch",
            new DispatchRequest("appServer", "Hold the first AppServer run.", null, null));
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{second.Item.ItemId}/dispatch",
            new DispatchRequest("appServer", "This should wait for the repository slot.", null, null));

        await WaitUntilAsync(() => fakeAppServer.StartThreadCount == 1);
        await Task.Delay(400);

        var firstDetail = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{first.Item.ItemId}", JsonOptions);
        var secondDetail = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{second.Item.ItemId}", JsonOptions);
        var activeRuns = firstDetail!.Runs.Concat(secondDetail!.Runs)
            .Where(x => x.RunnerKind == "appServer" && x.Status is RunStatus.Dispatching or RunStatus.Running)
            .ToList();
        var queuedRuns = firstDetail.Runs.Concat(secondDetail.Runs)
            .Where(x => x.RunnerKind == "appServer" && x.Status == RunStatus.Queued)
            .ToList();
        Assert.Single(activeRuns);
        Assert.Single(queuedRuns);
        Assert.Equal(1, fakeAppServer.StartThreadCount);
    }

    [Fact]
    public async Task AppServerRedispatch_ReusesThreadAndRebindsTools()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Initial read-only DotCraft pass.", null, null));

        await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.AwaitingReview);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/request-changes",
            new DecisionRequest("Please address the refresh-token race before another review."));

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Follow up on requested changes.", null, null));

        await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.AwaitingReview && x.Item.CurrentRound == 2);
        Assert.Equal(1, fakeAppServer.StartThreadCount);
        Assert.Equal(["thread-test-1", "thread-test-1"], fakeAppServer.TurnThreadIds);
        var resume = Assert.Single(fakeAppServer.ThreadResumeRequests);
        Assert.Equal("thread-test-1", resume.ThreadId);
        Assert.True((resume.DynamicTools ?? []).ContainsTool("oratorio_run", "SubmitReviewDraft"));
        Assert.True((resume.DynamicTools ?? []).ContainsTool("oratorio_run", "SubmitFollowUpDraft"));
        Assert.NotNull(resume.RuntimeAdditionalContext);

        var secondRun = Assert.Single(
            (await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{pr.ItemId}", JsonOptions))!.Runs,
            x => x.RunnerKind == "appServer" && x.Attempt == 1 && x.RoundId != null && x.ThreadId == "thread-test-1" && x.Status == RunStatus.Succeeded && x.Summary?.Contains("DotCraft analysis complete") == true && x.TurnId == "turn-test-2");
        Assert.Equal("thread-test-1", secondRun.ThreadId);
        var detail = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{pr.ItemId}", JsonOptions);
        Assert.Contains(detail!.Timeline, x => x.Title == "DotCraft thread reused" && x.Body?.Contains("thread-test-1", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task PullRequestReReview_QueuesNewRoundWithoutGitHubWrite_AndUsesLatestHead()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        var clock = new MutableClock(DateTimeOffset.Parse("2026-05-03T00:00:00Z"));
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(clock);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Initial read-only DotCraft pass.", null, null));
        await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.AwaitingReview);

        clock.Advance(TimeSpan.FromMinutes(5));
        MoveDefaultPullRequestHead(fakeGitHub, "def456", clock.UtcNow);
        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });

        var queued = await PostAsync<ItemDetailResponse>(client, $"/api/v1/items/id/{pr.ItemId}/rereview", new { });
        Assert.Equal(ItemState.Dispatching, queued.Item.State);
        Assert.Equal(2, queued.Item.CurrentRound);
        Assert.Contains(queued.Rounds, x => x.RoundNumber == 1 && x.Status == RoundStatus.Superseded);
        Assert.Contains(queued.Rounds, x => x.RoundNumber == 2 && x.Status == RoundStatus.Running);
        Assert.Contains(queued.Decisions, x => x.Decision == DecisionType.ReReview && x.Body?.Contains("abc123 to def456", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(queued.SourceWrites, x => x.DecisionId is not null);
        Assert.Contains(queued.SourceWrites, x => x.Kind == SourceWriteKind.CheckRun && x.Intent == "reviewGateStart" && x.HeadSha == "def456");
        Assert.Empty(queued.DiscussionTurns);

        var reviewed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.AwaitingReview && x.Item.CurrentRound == 2);
        Assert.Equal(1, fakeAppServer.StartThreadCount);
        Assert.Equal(["thread-test-1", "thread-test-1"], fakeAppServer.TurnThreadIds);
        Assert.Contains(reviewed.Runs, x => x.Status == RunStatus.Succeeded && x.BaseSha == "def456");
    }

    [Fact]
    public async Task PullRequestReReview_ApprovedPullRequest_CreatesNewRoundWithoutExtraGitHubWrite()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        var clock = new MutableClock(DateTimeOffset.Parse("2026-05-03T00:00:00Z"));
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(clock);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Initial read-only DotCraft pass.", null, null));
        await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.AwaitingReview);

        var approved = await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/approve",
            new DecisionRequest("Looks good."));
        Assert.Equal(ItemState.Approved, approved.Item.State);
        Assert.Equal(4, approved.SourceWrites.Count);

        clock.Advance(TimeSpan.FromMinutes(5));
        MoveDefaultPullRequestHead(fakeGitHub, "fed789", clock.UtcNow);
        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });

        var queued = await PostAsync<ItemDetailResponse>(client, $"/api/v1/items/id/{pr.ItemId}/rereview", new { });
        Assert.Equal(ItemState.Dispatching, queued.Item.State);
        Assert.Equal(2, queued.Item.CurrentRound);
        Assert.Contains(queued.Rounds, x => x.RoundNumber == 1 && x.Status == RoundStatus.Superseded);
        Assert.Equal(5, queued.SourceWrites.Count);
        Assert.Contains(queued.SourceWrites, x => x.Kind == SourceWriteKind.CheckRun && x.Intent == "reviewGateStart" && x.HeadSha == "fed789");
        Assert.Single(fakeGitHub.PullRequestReviews);
        Assert.Equal(2, fakeGitHub.CheckRuns.Count);
    }

    [Fact]
    public async Task PullRequestReReview_RejectsWhenHeadHasNotChanged()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Initial read-only DotCraft pass.", null, null));
        await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.AwaitingReview);

        var response = await client.PostAsJsonAsync($"/api/v1/items/id/{pr.ItemId}/rereview", new { }, JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        Assert.Equal("noNewPullRequestHead", error?.Error.Code);
    }

    [Fact]
    public async Task PullRequestReReview_RequiresGitHubPullRequestCompletedReviewRun_AndInactiveItem()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.Hold);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var issue = Assert.Single(list!.Items, x => x.Kind == ItemKind.Issue);
        var pr = Assert.Single(list.Items, x => x.Kind == ItemKind.PullRequest);

        var issueResponse = await client.PostAsJsonAsync($"/api/v1/items/id/{issue.ItemId}/rereview", new { }, JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, issueResponse.StatusCode);
        var issueError = await issueResponse.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        Assert.Equal("invalidReReviewTarget", issueError?.Error.Code);

        var missingRunResponse = await client.PostAsJsonAsync($"/api/v1/items/id/{pr.ItemId}/rereview", new { }, JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, missingRunResponse.StatusCode);
        var missingRunError = await missingRunResponse.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        Assert.Equal("reviewRunRequired", missingRunError?.Error.Code);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Keep active.", null, null));
        await WaitUntilAsync(() => fakeAppServer.StartThreadCount == 1);

        var activeResponse = await client.PostAsJsonAsync($"/api/v1/items/id/{pr.ItemId}/rereview", new { }, JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, activeResponse.StatusCode);
        var activeError = await activeResponse.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        Assert.Equal("activeRunExists", activeError?.Error.Code);
    }

    [Fact]
    public async Task AppServerRedispatch_CreatesFreshThreadWhenDynamicToolRebindUnsupported()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft)
        {
            SupportsDynamicToolRebind = false
        };
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Initial read-only DotCraft pass.", null, null));

        await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.AwaitingReview);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/request-changes",
            new DecisionRequest("Please address the refresh-token race before another review."));

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Follow up on requested changes.", null, null));

        var reviewed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.AwaitingReview && x.Item.CurrentRound == 2);
        Assert.Equal(2, fakeAppServer.StartThreadCount);
        Assert.Empty(fakeAppServer.ThreadResumeRequests);
        Assert.Equal(["thread-test-1", "thread-test-2"], fakeAppServer.TurnThreadIds);

        var secondRun = reviewed.Runs.Single(x => x.RunnerKind == "appServer" && x.ThreadId == "thread-test-2");
        Assert.Equal(RunStatus.Succeeded, secondRun.Status);
        Assert.Contains(reviewed.Timeline, x =>
            x.Title == "DotCraft thread created" &&
            x.Body?.Contains("does not support dynamic tool rebind", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task AppServerDispatch_FailsWhenRuntimeAdditionalContextUnsupported()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft)
        {
            SupportsRuntimeAdditionalContext = false
        };
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Runtime context is required.", null, null));

        var failed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.Failed);
        var run = Assert.Single(failed.Runs, x => x.RunnerKind == "appServer");
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal("runtimeAdditionalContextUnsupported", run.ErrorCode);
        Assert.Equal(0, fakeAppServer.StartThreadCount);
    }

    [Fact]
    public async Task AppServerDispatch_DoesNotReuseIncompatibleThreadContext()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Initial read-only DotCraft pass.", null, null));
        var first = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.AwaitingReview);
        var firstRun = Assert.Single(first.Runs, x => x.RunnerKind == "appServer");
        await MarkRunAsIncompatibleThreadContextAsync(app, firstRun.RunId);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/request-changes",
            new DecisionRequest("Run another review from incompatible context."));
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Follow up from incompatible context.", null, null));

        var reviewed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.AwaitingReview && x.Item.CurrentRound == 2);
        Assert.Equal(2, fakeAppServer.StartThreadCount);
        var secondRun = reviewed.Runs.Single(x => x.RunnerKind == "appServer" && x.RoundId != firstRun.RoundId);
        Assert.Equal("thread-test-2", secondRun.ThreadId);
        Assert.Contains(reviewed.Timeline, x => x.Title == "DotCraft thread created" && x.Body?.Contains("No compatible compact AppServer thread", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task AgentDiscussionTurn_RecordsReplyWithoutChangingTaskLifecycle()
    {
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.Success);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        var task = await CreateLocalTaskAsync(client, "Discuss a completed run", repository: "example-owner/oratorio");
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{task.Item.ItemId}/dispatch",
            new DispatchRequest("appServer", "Initial DotCraft analysis.", null, null));

        var reviewed = await WaitForItemByIdAsync(client, task.Item.ItemId, x => x.Item.State == ItemState.AwaitingReview);
        var baseRun = Assert.Single(reviewed.Runs, x => x.RunnerKind == "appServer");
        var originalState = reviewed.Item.State;
        var originalCurrentRunId = reviewed.Item.CurrentRunId;
        var originalCheckState = reviewed.Item.CheckState;

        fakeAppServer.Outcome = FakeAppServerOutcome.SubmitDiscussionReply;
        var asked = await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{task.Item.ItemId}/discussion-turns",
            new DiscussionTurnRequest("Why did you call out the refresh-token path?", reviewed.Item.CurrentRound, null));

        Assert.Contains(asked.Comments, x => x.Purpose == CommentPurpose.DiscussionQuestion && x.Body == "Why did you call out the refresh-token path?");
        Assert.Single(asked.DiscussionTurns);

        var answered = await WaitForItemByIdAsync(
            client,
            task.Item.ItemId,
            x => x.DiscussionTurns.Any(turn => turn.Status == DiscussionTurnStatus.Succeeded));
        var turn = Assert.Single(answered.DiscussionTurns);

        Assert.Equal(DiscussionTurnStatus.Succeeded, turn.Status);
        Assert.Equal(baseRun.RunId, turn.BaseRunId);
        Assert.Equal(baseRun.ThreadId, turn.ThreadId);
        Assert.Equal("turn-test-2", turn.TurnId);
        Assert.NotNull(turn.ReplyCommentId);
        Assert.Equal(originalState, answered.Item.State);
        Assert.Equal(originalCurrentRunId, answered.Item.CurrentRunId);
        Assert.Equal(originalCheckState, answered.Item.CheckState);
        Assert.Contains(answered.Comments, x => x.Purpose == CommentPurpose.DiscussionReply && x.AuthorKind == AuthorKind.Agent && x.Body == "Agent answer from DotCraft.");
        Assert.Contains(answered.Timeline, x => x.Title == "Agent answered discussion question");
        Assert.Equal(1, fakeAppServer.StartThreadCount);
        Assert.Equal(["thread-test-1", "thread-test-1"], fakeAppServer.TurnThreadIds);
        var resume = Assert.Single(fakeAppServer.ThreadResumeRequests);
        Assert.Equal("thread-test-1", resume.ThreadId);
        Assert.True((resume.DynamicTools ?? []).ContainsTool("oratorio_run", "SubmitDiscussionReply"));
        Assert.NotNull(resume.RuntimeAdditionalContext);
        Assert.NotNull(fakeAppServer.LastToolResult);
        Assert.True(fakeAppServer.LastToolResult!.Success);
    }

    [Fact]
    public async Task AgentDiscussionTurn_RequiresCompatibleCompletedThread()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();
        var task = await CreateLocalTaskAsync(client, "Ask too early", repository: "example-owner/oratorio");

        var response = await client.PostAsJsonAsync(
            $"/api/v1/items/id/{task.Item.ItemId}/discussion-turns",
            new DiscussionTurnRequest("Can you answer without a base thread?", null, null),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        Assert.Equal("noCompatibleDiscussionThread", error?.Error.Code);
    }

    [Fact]
    public async Task AgentDiscussionTurn_FailsWhenRuntimeAdditionalContextUnsupported()
    {
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.Success);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        var task = await CreateLocalTaskAsync(client, "Discuss without runtime context", repository: "example-owner/oratorio");
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{task.Item.ItemId}/dispatch",
            new DispatchRequest("appServer", "Initial DotCraft analysis.", null, null));
        var reviewed = await WaitForItemByIdAsync(client, task.Item.ItemId, x => x.Item.State == ItemState.AwaitingReview);

        fakeAppServer.SupportsRuntimeAdditionalContext = false;
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{task.Item.ItemId}/discussion-turns",
            new DiscussionTurnRequest("Can you answer without runtime context?", reviewed.Item.CurrentRound, null));

        var failed = await WaitForItemByIdAsync(
            client,
            task.Item.ItemId,
            x => x.DiscussionTurns.Any(turn => turn.Status == DiscussionTurnStatus.Failed));
        var turn = Assert.Single(failed.DiscussionTurns);
        Assert.Equal("runtimeAdditionalContextUnsupported", turn.ErrorCode);
        Assert.Single(fakeAppServer.ThreadStartRequests);
        Assert.Empty(fakeAppServer.ThreadResumeRequests);
    }

    [Fact]
    public async Task AgentDiscussionTurn_RejectsSecondActiveQuestion()
    {
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.Success);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        var task = await CreateLocalTaskAsync(client, "One question at a time", repository: "example-owner/oratorio");
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{task.Item.ItemId}/dispatch",
            new DispatchRequest("appServer", "Initial DotCraft analysis.", null, null));
        var reviewed = await WaitForItemByIdAsync(client, task.Item.ItemId, x => x.Item.State == ItemState.AwaitingReview);

        fakeAppServer.Outcome = FakeAppServerOutcome.Hold;
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{task.Item.ItemId}/discussion-turns",
            new DiscussionTurnRequest("First question?", reviewed.Item.CurrentRound, null));

        var response = await client.PostAsJsonAsync(
            $"/api/v1/items/id/{task.Item.ItemId}/discussion-turns",
            new DiscussionTurnRequest("Second question?", reviewed.Item.CurrentRound, null),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        Assert.Equal("activeDiscussionTurnExists", error?.Error.Code);
    }

    [Fact]
    public async Task AgentDiscussionTurn_MismatchedToolBindingFailsStably()
    {
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.Success)
        {
            UseMismatchedToolThreadId = true
        };
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        var task = await CreateLocalTaskAsync(client, "Reject mismatched discussion reply", repository: "example-owner/oratorio");
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{task.Item.ItemId}/dispatch",
            new DispatchRequest("appServer", "Initial DotCraft analysis.", null, null));
        var reviewed = await WaitForItemByIdAsync(client, task.Item.ItemId, x => x.Item.State == ItemState.AwaitingReview);

        fakeAppServer.Outcome = FakeAppServerOutcome.SubmitDiscussionReply;
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{task.Item.ItemId}/discussion-turns",
            new DiscussionTurnRequest("Please answer from the wrong thread.", reviewed.Item.CurrentRound, null));

        var failed = await WaitForItemByIdAsync(
            client,
            task.Item.ItemId,
            x => x.DiscussionTurns.Any(turn => turn.Status == DiscussionTurnStatus.Failed));
        var turn = Assert.Single(failed.DiscussionTurns);

        Assert.Equal(DiscussionTurnStatus.Failed, turn.Status);
        Assert.Null(turn.ReplyCommentId);
        Assert.Equal("discussionReplyMissing", turn.ErrorCode);
        Assert.NotNull(fakeAppServer.LastToolResult);
        Assert.False(fakeAppServer.LastToolResult!.Success);
        Assert.Equal("InvalidDiscussionTurnBinding", fakeAppServer.LastToolResult.ErrorCode);
    }

    [Fact]
    public async Task PullRequestReviewRun_RequiresSubmitReviewDraft()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.Success);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Review without calling the draft tool.", null, null));

        var failed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.Failed);
        var run = Assert.Single(failed.Runs, x => x.RunnerKind == "appServer");
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal("reviewDraftRequired", run.ErrorCode);
        Assert.Empty(failed.ReviewDrafts);
        var check = Assert.Single(fakeGitHub.CheckRuns);
        Assert.Equal("completed", check.Status);
        Assert.Equal("failure", check.Conclusion);
        Assert.Equal(1, check.UpdateCount);
        Assert.Contains(failed.SourceWrites, write =>
            write.Kind == SourceWriteKind.CheckRun &&
            write.Intent == "reviewGateRunFailed" &&
            write.Status == SourceWriteStatus.Succeeded);
    }

    [Theory]
    [InlineData(FakeAppServerOutcome.Success, "reviewDraftRequired")]
    [InlineData(FakeAppServerOutcome.Fail, "appServerTurnFailed")]
    [InlineData(FakeAppServerOutcome.Cancel, "appServerTurnCancelled")]
    public async Task PullRequestReviewRun_RetriesRecoverableFailure_AndReusesFailedThread(
        FakeAppServerOutcome firstOutcome,
        string expectedErrorCode)
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        fakeAppServer.ScriptedOutcomes.Enqueue(firstOutcome);
        fakeAppServer.ScriptedOutcomes.Enqueue(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        await using var app = new TestOratorioApp(
            services =>
            {
                services.RemoveAll<IGitHubApiClient>();
                services.AddSingleton<IGitHubApiClient>(fakeGitHub);
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            },
            new Dictionary<string, string?>
            {
                ["Oratorio:DotCraft:MaxRunAttempts"] = "2",
                ["Oratorio:DotCraft:RetryBackoffSeconds"] = "1",
                ["Oratorio:DotCraft:MaxRetryBackoffSeconds"] = "1"
            });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Retry a missing review draft.", null, null));

        var reviewed = await WaitForItemByIdAsync(
            client,
            pr.ItemId!,
            x => x.Item.State == ItemState.AwaitingReview && x.ReviewDrafts.Count == 1 && x.Runs.Count(run => run.RunnerKind == "appServer") == 2);
        var runs = reviewed.Runs.Where(x => x.RunnerKind == "appServer").OrderBy(x => x.Attempt).ToArray();

        Assert.Equal(RunStatus.Failed, runs[0].Status);
        Assert.Equal(expectedErrorCode, runs[0].ErrorCode);
        Assert.Equal(RunStatus.Succeeded, runs[1].Status);
        Assert.Equal(1, runs[1].RetryCount);
        Assert.Equal("thread-test-1", runs[0].ThreadId);
        Assert.Equal("thread-test-1", runs[1].ThreadId);
        Assert.Equal(1, fakeAppServer.StartThreadCount);
        Assert.Single(fakeAppServer.ThreadResumeRequests);
        Assert.Contains("Retry recovery:", fakeAppServer.TurnPrompts[1], StringComparison.Ordinal);
        Assert.Contains(expectedErrorCode, fakeAppServer.TurnPrompts[1], StringComparison.Ordinal);
        Assert.Contains(reviewed.Timeline, x => x.Title == "Retry scheduled");
        var check = Assert.Single(fakeGitHub.CheckRuns);
        Assert.Equal("completed", check.Status);
        Assert.Equal("neutral", check.Conclusion);
    }

    [Theory]
    [InlineData(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraftThenFail, "Synthetic failure after draft submission")]
    [InlineData(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraftThenDisconnect, "disconnected before the run completed")]
    public async Task PullRequestReviewRun_UsesPersistedDraftWhenAppServerTerminatesAfterSubmission(
        FakeAppServerOutcome outcome,
        string expectedWarning)
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(outcome);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Keep the submitted draft if the turn fails.", null, null));

        var reviewed = await WaitForItemByIdAsync(
            client,
            pr.ItemId!,
            x => x.Item.State == ItemState.AwaitingReview && x.ReviewDrafts.Count == 1);
        var run = Assert.Single(reviewed.Runs, x => x.RunnerKind == "appServer");

        Assert.Equal(RunStatus.Succeeded, run.Status);
        Assert.Null(run.ErrorCode);
        Assert.Equal("No issues found.", run.Summary);
        Assert.Single(reviewed.ReviewDrafts);
        Assert.Equal(1, fakeAppServer.StartThreadCount);
        Assert.Empty(fakeAppServer.ThreadResumeRequests);
        Assert.Contains(reviewed.Timeline, x =>
            x.Title == "Review draft retained after AppServer termination" &&
            x.Body!.Contains(expectedWarning, StringComparison.Ordinal));
    }

    [Fact]
    public async Task PullRequestReviewRun_StartsFreshThreadWhenFailedThreadResumeIsRejected()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft)
        {
            FailThreadResume = true
        };
        fakeAppServer.ScriptedOutcomes.Enqueue(FakeAppServerOutcome.Success);
        fakeAppServer.ScriptedOutcomes.Enqueue(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        await using var app = new TestOratorioApp(
            services =>
            {
                services.RemoveAll<IGitHubApiClient>();
                services.AddSingleton<IGitHubApiClient>(fakeGitHub);
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            },
            new Dictionary<string, string?>
            {
                ["Oratorio:DotCraft:MaxRunAttempts"] = "2"
            });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Fall back when thread resume is rejected.", null, null));

        var reviewed = await WaitForItemByIdAsync(
            client,
            pr.ItemId!,
            x => x.Item.State == ItemState.AwaitingReview && x.Runs.Count(run => run.RunnerKind == "appServer") == 2);

        Assert.Equal(1, fakeAppServer.ThreadResumeAttemptCount);
        Assert.Empty(fakeAppServer.ThreadResumeRequests);
        Assert.Equal(2, fakeAppServer.StartThreadCount);
        Assert.Equal(["thread-test-1", "thread-test-2"], fakeAppServer.TurnThreadIds);
        Assert.Contains(reviewed.Timeline, x =>
            x.Title == "DotCraft thread created" &&
            x.Body!.Contains("could not be resumed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Retry recovery:", fakeAppServer.TurnPrompts[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task PullRequestReviewRun_StartsFreshThreadAfterInfrastructureFailure()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        fakeAppServer.ScriptedOutcomes.Enqueue(FakeAppServerOutcome.Disconnect);
        fakeAppServer.ScriptedOutcomes.Enqueue(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        await using var app = new TestOratorioApp(
            services =>
            {
                services.RemoveAll<IGitHubApiClient>();
                services.AddSingleton<IGitHubApiClient>(fakeGitHub);
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            },
            new Dictionary<string, string?>
            {
                ["Oratorio:DotCraft:MaxRunAttempts"] = "2"
            });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Use a fresh thread after infrastructure failure.", null, null));

        var reviewed = await WaitForItemByIdAsync(
            client,
            pr.ItemId!,
            x => x.Item.State == ItemState.AwaitingReview && x.Runs.Count(run => run.RunnerKind == "appServer") == 2);
        var runs = reviewed.Runs.Where(x => x.RunnerKind == "appServer").OrderBy(x => x.Attempt).ToArray();

        Assert.Equal("appServerDisconnected", runs[0].ErrorCode);
        Assert.Equal(RunStatus.Succeeded, runs[1].Status);
        Assert.Equal(2, fakeAppServer.StartThreadCount);
        Assert.Equal(0, fakeAppServer.ThreadResumeAttemptCount);
        Assert.Equal(["thread-test-1", "thread-test-2"], fakeAppServer.TurnThreadIds);
    }

    [Fact]
    public async Task PullRequestReviewRun_AllowsSummaryOnlyReviewDraft()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Submit a clean structured draft.", null, null));

        var reviewed = await WaitForItemByIdAsync(
            client,
            pr.ItemId!,
            x => x.Item.State == ItemState.AwaitingReview && x.ReviewDrafts.Count == 1);
        var draft = Assert.Single(reviewed.ReviewDrafts);
        var run = Assert.Single(reviewed.Runs, x => x.RunnerKind == "appServer");

        Assert.Equal(RunStatus.Succeeded, run.Status);
        Assert.Equal(0, draft.MajorCount);
        Assert.Equal(0, draft.MinorCount);
        Assert.Equal(0, draft.SuggestionCount);
        Assert.Equal(0, draft.AcceptedCount);
        Assert.Empty(draft.Comments);
        Assert.Equal("No issues found.", draft.SummaryBody);
        var check = Assert.Single(fakeGitHub.CheckRuns);
        Assert.Equal("completed", check.Status);
        Assert.Equal("neutral", check.Conclusion);
        Assert.Equal(1, check.UpdateCount);
        Assert.Contains(reviewed.SourceWrites, write =>
            write.Kind == SourceWriteKind.CheckRun &&
            write.Intent == "reviewGateComplete" &&
            write.Status == SourceWriteStatus.Succeeded);

        var published = await PostAsync<ItemDetailResponse>(client, $"/api/v1/review-drafts/{draft.DraftId}/publish", new { });
        Assert.Equal(ReviewDraftStatus.Published, Assert.Single(published.ReviewDrafts).Status);
        var review = Assert.Single(fakeGitHub.PullRequestReviews);
        Assert.Equal("COMMENT", review.Event);
        Assert.Equal("No issues found.", review.Body);
        Assert.Empty(review.Comments);
    }

    [Theory]
    [InlineData("example-owner/oratorio", "example-owner/oratorio")]
    [InlineData("github:github.com/example-owner/oratorio", "github:github.com/example-owner/oratorio")]
    public async Task AutoReview_BaselinesExistingPullRequestsWhenRepositoryIsEnabled(string configuredRepository, string stateRepository)
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var automation = new MutableOptionsMonitor<OratorioAutomationOptions>(new OratorioAutomationOptions());
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IOptionsMonitor<OratorioAutomationOptions>>();
            services.AddSingleton<IOptionsMonitor<OratorioAutomationOptions>>(automation);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        automation.Set(new OratorioAutomationOptions { AutoReviewRepositories = [configuredRepository] });
        await DispatchAutoReviewAsync(app);

        var detail = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{pr.ItemId}", JsonOptions);
        Assert.NotNull(detail);
        Assert.Empty(detail!.Runs);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var itemState = await db.AutoReviewItemStates.AsNoTracking().SingleAsync(x => x.ItemId == pr.ItemId);
        Assert.Equal(stateRepository, itemState.Repository);
        Assert.Equal("abc123", itemState.LastObservedHeadSha);
        Assert.Equal("abc123", itemState.LastQueuedHeadSha);
    }

    [Theory]
    [InlineData("example-owner/oratorio")]
    [InlineData("github:github.com/example-owner/oratorio")]
    public async Task AutoReview_QueuesNewPullRequestsAfterRepositoryBaseline(string configuredRepository)
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        var automation = new MutableOptionsMonitor<OratorioAutomationOptions>(
            new OratorioAutomationOptions { AutoReviewRepositories = [configuredRepository] });
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            services.RemoveAll<IOptionsMonitor<OratorioAutomationOptions>>();
            services.AddSingleton<IOptionsMonitor<OratorioAutomationOptions>>(automation);
        });
        var client = app.CreateClient();

        await DispatchAutoReviewAsync(app);
        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);
        await DispatchAutoReviewAsync(app);

        var reviewed = await WaitForItemByIdAsync(
            client,
            pr.ItemId!,
            x => x.Item.State == ItemState.AwaitingReview && x.ReviewDrafts.Count == 1);
        var run = Assert.Single(reviewed.Runs, x => x.RunnerKind == "appServer");

        Assert.Equal(RunDispatchTrigger.AutoReview, run.DispatchTrigger);
        Assert.Equal("abc123", run.TargetHeadSha);
        Assert.Equal(RunStatus.Succeeded, run.Status);
        Assert.Contains(reviewed.SourceWrites, x => x.Kind == SourceWriteKind.CheckRun && x.Intent == "reviewGateStart" && x.HeadSha == "abc123");
        Assert.Contains(reviewed.SourceWrites, x => x.Kind == SourceWriteKind.CheckRun && x.Intent == "reviewGateComplete" && x.HeadSha == "abc123");
        Assert.DoesNotContain(reviewed.SourceWrites, x => x.DecisionId is not null);
    }

    [Fact]
    public async Task AutoReview_StopsAfterReviewRetryAttemptsAreExhaustedForSameHead()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.Success);
        var automation = new MutableOptionsMonitor<OratorioAutomationOptions>(
            new OratorioAutomationOptions { AutoReviewRepositories = ["example-owner/oratorio"] });
        await using var app = new TestOratorioApp(
            services =>
            {
                services.RemoveAll<IGitHubApiClient>();
                services.AddSingleton<IGitHubApiClient>(fakeGitHub);
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
                services.RemoveAll<IOptionsMonitor<OratorioAutomationOptions>>();
                services.AddSingleton<IOptionsMonitor<OratorioAutomationOptions>>(automation);
            },
            new Dictionary<string, string?>
            {
                ["Oratorio:DotCraft:MaxRunAttempts"] = "2"
            });
        var client = app.CreateClient();

        await DispatchAutoReviewAsync(app);
        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);
        await DispatchAutoReviewAsync(app);

        var failed = await WaitForItemByIdAsync(
            client,
            pr.ItemId!,
            x => x.Item.State == ItemState.Failed && x.Runs.Count(run => run.RunnerKind == "appServer") == 2);
        Assert.All(failed.Runs.Where(x => x.RunnerKind == "appServer"), x => Assert.Equal("reviewDraftRequired", x.ErrorCode));

        await DispatchAutoReviewAsync(app);
        await Task.Delay(1200);
        await DispatchAutoReviewAsync(app);
        var afterRescan = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{pr.ItemId}", JsonOptions);

        Assert.Equal(ItemState.Failed, afterRescan!.Item.State);
        Assert.Equal(2, afterRescan.Runs.Count(x => x.RunnerKind == "appServer"));
        Assert.Single(fakeGitHub.CheckRuns);
        Assert.Equal("failure", fakeGitHub.CheckRuns[0].Conclusion);
    }

    [Fact]
    public async Task AutoReview_SupersedesQueuedRetryWhenHeadChanges()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        fakeAppServer.ScriptedOutcomes.Enqueue(FakeAppServerOutcome.Success);
        fakeAppServer.ScriptedOutcomes.Enqueue(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        var automation = new MutableOptionsMonitor<OratorioAutomationOptions>(
            new OratorioAutomationOptions { AutoReviewRepositories = ["example-owner/oratorio"] });
        await using var app = new TestOratorioApp(
            services =>
            {
                services.RemoveAll<IGitHubApiClient>();
                services.AddSingleton<IGitHubApiClient>(fakeGitHub);
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
                services.RemoveAll<IOptionsMonitor<OratorioAutomationOptions>>();
                services.AddSingleton<IOptionsMonitor<OratorioAutomationOptions>>(automation);
            },
            new Dictionary<string, string?>
            {
                ["Oratorio:DotCraft:MaxRunAttempts"] = "2",
                ["Oratorio:DotCraft:RetryBackoffSeconds"] = "2",
                ["Oratorio:DotCraft:MaxRetryBackoffSeconds"] = "2"
            });
        var client = app.CreateClient();

        await DispatchAutoReviewAsync(app);
        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);
        await DispatchAutoReviewAsync(app);
        await WaitForItemByIdAsync(
            client,
            pr.ItemId!,
            x => x.Runs.Any(run => run.Attempt == 2 && run.Status == RunStatus.Queued));

        MoveDefaultPullRequestHead(fakeGitHub, "def456", DateTimeOffset.UtcNow.AddMinutes(1));
        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });

        var reviewed = await WaitForItemByIdAsync(
            client,
            pr.ItemId!,
            x => x.Item.State == ItemState.AwaitingReview && x.Item.CurrentRound == 2 && x.ReviewDrafts.Count == 1);
        var oldRetry = Assert.Single(reviewed.Runs, x => x.RoundId == reviewed.Rounds.Single(r => r.RoundNumber == 1).RoundId && x.Attempt == 2);
        var newHeadRun = Assert.Single(reviewed.Runs, x => x.RoundId == reviewed.Rounds.Single(r => r.RoundNumber == 2).RoundId);

        Assert.Equal(RunStatus.Cancelled, oldRetry.Status);
        Assert.Equal("reviewRetrySuperseded", oldRetry.ErrorCode);
        Assert.Equal(RoundStatus.Superseded, reviewed.Rounds.Single(x => x.RoundNumber == 1).Status);
        Assert.Equal(1, newHeadRun.Attempt);
        Assert.Equal("def456", newHeadRun.TargetHeadSha);
        Assert.Equal(RunStatus.Succeeded, newHeadRun.Status);
        Assert.Equal(2, fakeAppServer.StartThreadCount);
        Assert.Equal(0, fakeAppServer.ThreadResumeAttemptCount);
        Assert.Contains(reviewed.SourceWrites, x => x.Intent == "reviewGateRunCancelled" && x.HeadSha == "abc123");
        Assert.Contains(reviewed.SourceWrites, x => x.Intent == "reviewGateComplete" && x.HeadSha == "def456");
    }

    [Theory]
    [InlineData("example-owner/oratorio")]
    [InlineData("github:github.com/example-owner/oratorio")]
    public async Task AutoReview_ReReviewsWhenPullRequestHeadChanges(string configuredRepository)
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        var clock = new MutableClock(DateTimeOffset.Parse("2026-05-03T00:00:00Z"));
        var automation = new MutableOptionsMonitor<OratorioAutomationOptions>(
            new OratorioAutomationOptions { AutoReviewRepositories = [configuredRepository] });
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(clock);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            services.RemoveAll<IOptionsMonitor<OratorioAutomationOptions>>();
            services.AddSingleton<IOptionsMonitor<OratorioAutomationOptions>>(automation);
        });
        var client = app.CreateClient();

        await DispatchAutoReviewAsync(app);
        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);
        await DispatchAutoReviewAsync(app);
        await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.AwaitingReview);

        clock.Advance(TimeSpan.FromMinutes(5));
        MoveDefaultPullRequestHead(fakeGitHub, "def456", clock.UtcNow);
        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        await DispatchAutoReviewAsync(app);

        var reviewed = await WaitForItemByIdAsync(
            client,
            pr.ItemId!,
            x => x.Item.State == ItemState.AwaitingReview && x.Item.CurrentRound == 2);
        var run = reviewed.Runs
            .Where(x => x.RunnerKind == "appServer")
            .OrderByDescending(x => x.StartedAt ?? x.CompletedAt ?? DateTimeOffset.MinValue)
            .First();

        Assert.Equal(RunDispatchTrigger.AutoReview, run.DispatchTrigger);
        Assert.Equal("def456", run.TargetHeadSha);
        Assert.Contains(reviewed.Rounds, x => x.RoundNumber == 1 && x.Status == RoundStatus.Superseded);
        Assert.Contains(reviewed.Decisions, x => x.Decision == DecisionType.ReReview && x.AuthorName == "oratorio/auto-review");
        Assert.Contains(reviewed.SourceWrites, x => x.Kind == SourceWriteKind.CheckRun && x.Intent == "reviewGateStart" && x.HeadSha == "def456");
        Assert.Contains(reviewed.SourceWrites, x => x.Kind == SourceWriteKind.CheckRun && x.Intent == "reviewGateComplete" && x.HeadSha == "def456");
        Assert.DoesNotContain(reviewed.SourceWrites, x => x.DecisionId is not null);
    }

    [Fact]
    public async Task AppServerReviewDraftTool_RejectsMismatchedReusedThreadBinding()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitReviewDraft)
        {
            UseMismatchedToolThreadId = true
        };
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare structured PR review suggestions.", null, null));

        var reviewed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.Failed);
        var run = Assert.Single(reviewed.Runs, x => x.RunnerKind == "appServer");
        Assert.Equal("reviewDraftRequired", run.ErrorCode);
        Assert.Empty(reviewed.ReviewDrafts);
        Assert.NotNull(fakeAppServer.LastToolResult);
        Assert.False(fakeAppServer.LastToolResult!.Success);
        Assert.Equal("InvalidRunBinding", fakeAppServer.LastToolResult.ErrorCode);
    }

    [Fact]
    public async Task AppServerReviewDraftTool_RejectsInlineCommentWithoutSuggestionOrReason()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitInvalidReviewDraft);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Submit a malformed structured PR review draft.", null, null));

        var failed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.Failed);
        var run = Assert.Single(failed.Runs, x => x.RunnerKind == "appServer");

        Assert.Equal("reviewDraftRequired", run.ErrorCode);
        Assert.Empty(failed.ReviewDrafts);
        Assert.NotNull(fakeAppServer.LastToolResult);
        Assert.False(fakeAppServer.LastToolResult!.Success);
        Assert.Equal("InvalidArguments", fakeAppServer.LastToolResult.ErrorCode);
    }

    [Fact]
    public async Task AppServerReviewDraftTool_RejectsMissingSuggestionOldText()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitMissingSuggestionOldTextReviewDraft);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Submit a review draft without oldText.", null, null));

        var failed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.Failed);
        Assert.Empty(failed.ReviewDrafts);
        Assert.NotNull(fakeAppServer.LastToolResult);
        Assert.False(fakeAppServer.LastToolResult!.Success);
        Assert.Equal("InvalidArguments", fakeAppServer.LastToolResult.ErrorCode);
    }

    [Fact]
    public async Task AppServerReviewDraftTool_RejectsSuggestionOldTextNotFound()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSuggestionTextNotFoundReviewDraft);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Submit a review draft with unmatched oldText.", null, null));

        var failed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.Failed);
        Assert.Empty(failed.ReviewDrafts);
        Assert.NotNull(fakeAppServer.LastToolResult);
        Assert.False(fakeAppServer.LastToolResult!.Success);
        Assert.Equal("reviewDraftSuggestionTextNotFound", fakeAppServer.LastToolResult.ErrorCode);
    }

    [Fact]
    public async Task AppServerReviewDraftTool_RejectsAmbiguousSuggestionOldText()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        fakeGitHub.PullRequestFiles[0] = fakeGitHub.PullRequestFiles[0] with
        {
            Patch = """
@@ -84,6 +84,7 @@ public sealed class RefreshTokenStore
     public string Refresh(string token)
     {
+        return refreshed;
+        return refreshed;
     }
 }
"""
        };
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitAmbiguousSuggestionTextReviewDraft);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Submit a review draft with ambiguous oldText.", null, null));

        var failed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.Failed);
        Assert.Empty(failed.ReviewDrafts);
        Assert.NotNull(fakeAppServer.LastToolResult);
        Assert.False(fakeAppServer.LastToolResult!.Success);
        Assert.Equal("reviewDraftSuggestionTextAmbiguous", fakeAppServer.LastToolResult.ErrorCode);
    }

    [Fact]
    public async Task AppServerReviewDraftTool_FailsCorrectableInvalidAnchorsWithoutPersistingDraft()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitReviewDraft);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare structured PR review suggestions.", null, null));

        var failed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.Failed);
        var run = Assert.Single(failed.Runs, x => x.RunnerKind == "appServer");

        Assert.Equal("reviewDraftRequired", run.ErrorCode);
        Assert.Empty(failed.ReviewDrafts);
        Assert.True((fakeAppServer.LastThreadStartRequest?.DynamicTools ?? []).ContainsTool("oratorio_run", "SubmitReviewDraft"));
        var toolResult = Assert.Single(fakeAppServer.ToolResults);
        Assert.False(toolResult.Success);
        Assert.Equal("reviewDraftAnchorNotCommentable", toolResult.ErrorCode);
        Assert.Contains("commentable ranges", toolResult.ContentItems!.Single().Text, StringComparison.Ordinal);
        Assert.Contains("missing/File.cs", toolResult.ContentItems!.Single().Text, StringComparison.Ordinal);
        using var structured = JsonDocument.Parse(JsonSerializer.Serialize(toolResult.StructuredContent, JsonOptions));
        var invalidComments = structured.RootElement.GetProperty("error").GetProperty("details").GetProperty("invalidComments");
        Assert.Contains(invalidComments.EnumerateArray(), comment => comment.GetProperty("path").GetString() == "missing/File.cs" && comment.GetProperty("reason").GetString() == "fileNotInDiff");
        Assert.Contains(invalidComments.EnumerateArray(), comment => comment.GetProperty("path").GetString() == "src/Auth/RefreshTokenStore.cs" && comment.GetProperty("reason").GetString() == "lineNotCommentable");
    }

    [Fact]
    public async Task AppServerReviewDraftTool_ReturnsCommentableRangesForSyntheticBadAnchor()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        fakeGitHub.PullRequestFiles.Clear();
        fakeGitHub.PullRequestFiles.Add(new GitHubChangedFile(
            "src/Example/SyntheticFileSearcher.cs",
            "modified",
            0,
            0,
            0,
            FakeGitHubApiClient.SyntheticFileSearcherPatch));
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSyntheticBadAnchorReviewDraft);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare a synthetic review anchor regression.", null, null));

        var failed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.Failed);
        var toolResult = Assert.Single(fakeAppServer.ToolResults);

        Assert.Equal("reviewDraftRequired", Assert.Single(failed.Runs, x => x.RunnerKind == "appServer").ErrorCode);
        Assert.Empty(failed.ReviewDrafts);
        Assert.False(toolResult.Success);
        Assert.Equal("reviewDraftAnchorNotCommentable", toolResult.ErrorCode);
        Assert.Contains("255-277", toolResult.ContentItems!.Single().Text, StringComparison.Ordinal);
        using var structured = JsonDocument.Parse(JsonSerializer.Serialize(toolResult.StructuredContent, JsonOptions));
        var invalidComment = Assert.Single(structured.RootElement.GetProperty("error").GetProperty("details").GetProperty("invalidComments").EnumerateArray());
        Assert.Equal("Synthetic anchor drift", invalidComment.GetProperty("title").GetString());
        Assert.Equal("src/Example/SyntheticFileSearcher.cs", invalidComment.GetProperty("path").GetString());
        Assert.Equal(129, invalidComment.GetProperty("line").GetInt32());
        Assert.Equal("RIGHT", invalidComment.GetProperty("side").GetString());
        Assert.Equal("lineNotCommentable", invalidComment.GetProperty("reason").GetString());
        Assert.Equal("255-277", invalidComment.GetProperty("rightCommentableRanges").GetString());
    }

    [Fact]
    public async Task AppServerReviewDraftTool_AllowsRetryAfterAnchorFailure()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        fakeGitHub.PullRequestFiles.Clear();
        fakeGitHub.PullRequestFiles.Add(new GitHubChangedFile(
            "src/Example/SyntheticFileSearcher.cs",
            "modified",
            0,
            0,
            0,
            FakeGitHubApiClient.SyntheticFileSearcherPatch));
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitRetryAnchorReviewDraft);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare a retryable synthetic review anchor regression.", null, null));

        var reviewed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.AwaitingReview && x.ReviewDrafts.Count == 1);
        var draft = Assert.Single(reviewed.ReviewDrafts);

        Assert.Collection(
            fakeAppServer.ToolResults,
            first =>
            {
                Assert.False(first.Success);
                Assert.Equal("reviewDraftAnchorNotCommentable", first.ErrorCode);
            },
            second => Assert.True(second.Success));
        Assert.Equal(ReviewDraftStatus.Draft, draft.Status);
        Assert.Equal(1, draft.AcceptedCount);
        Assert.Equal(0, draft.WarningCount);
        var comment = Assert.Single(draft.Comments);
        Assert.Equal(ReviewDraftCommentStatus.Accepted, comment.Status);
        Assert.Equal("src/Example/SyntheticFileSearcher.cs", comment.Path);
        Assert.Equal(255, comment.Line);
    }

    [Fact]
    public async Task AppServerReviewDraftTool_DerivesSuggestionCountFromAcceptedCodeSuggestions()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitMismatchedSuggestionCountReviewDraft);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare a review draft with mismatched suggestion count.", null, null));

        var reviewed = await WaitForItemByIdAsync(
            client,
            pr.ItemId!,
            x => x.Item.State == ItemState.AwaitingReview && x.ReviewDrafts.Count == 1);
        var draft = Assert.Single(reviewed.ReviewDrafts);

        Assert.Equal(1, draft.SuggestionCount);
        Assert.Contains(draft.Warnings, warning => warning.Contains("reviewDraftSuggestionCountMismatch", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AppServerReviewDraftTool_SkipsNoOpSuggestionReplacement()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitNoOpReviewDraft);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare a no-op review suggestion.", null, null));

        var reviewed = await WaitForItemByIdAsync(
            client,
            pr.ItemId!,
            x => x.Item.State == ItemState.AwaitingReview && x.ReviewDrafts.Count == 1);
        var draft = Assert.Single(reviewed.ReviewDrafts);

        Assert.Equal(0, draft.AcceptedCount);
        Assert.Equal(0, draft.SuggestionCount);
        Assert.Contains(draft.Warnings, warning => warning.Contains("reviewDraftNoOpSuggestion", StringComparison.Ordinal));
        Assert.Contains(draft.Comments, comment => comment.Status == ReviewDraftCommentStatus.Skipped && comment.Warning?.Contains("reviewDraftNoOpSuggestion", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task AppServerReviewDraftTool_ReturnsFailedToolResultWhenSourceDiffRequestFails()
    {
        var fakeGitHub = new FakeGitHubApiClient { FailPullRequestFileReads = true };
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitReviewDraft);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare structured PR review suggestions.", null, null));

        var failed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.Failed);
        var run = Assert.Single(failed.Runs, x => x.RunnerKind == "appServer");

        Assert.Equal("reviewDraftRequired", run.ErrorCode);
        Assert.Empty(failed.ReviewDrafts);
        Assert.NotNull(fakeAppServer.LastToolResult);
        Assert.False(fakeAppServer.LastToolResult!.Success);
        Assert.Equal("upstreamSourceRequestFailed", fakeAppServer.LastToolResult.ErrorCode);
    }

    [Fact]
    public async Task AppServerReviewDraftTool_DoesNotReadFullDiffWhenFilePatchesAreAvailable()
    {
        var fakeGitHub = new FakeGitHubApiClient { FailPullRequestDiffReads = true };
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitCleanReviewDraft);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare structured PR review suggestions.", null, null));

        var reviewed = await WaitForItemByIdAsync(
            client,
            pr.ItemId!,
            x => x.Item.State == ItemState.AwaitingReview && x.ReviewDrafts.Count == 1);
        var draft = Assert.Single(reviewed.ReviewDrafts);

        Assert.Equal(0, fakeGitHub.FullDiffCallCount);
        Assert.Equal(2, draft.AcceptedCount);
        Assert.Equal(0, draft.WarningCount);
        Assert.Equal(1, draft.MajorCount);
        Assert.Equal(1, draft.MinorCount);
        Assert.Equal("Found 2 issues.", draft.SummaryBody);
        var suggestion = Assert.Single(draft.Comments, comment => comment.SuggestionReplacement is not null);
        Assert.Equal("        return refreshed;", suggestion.SuggestionOriginal);
        Assert.Equal("        return token;", suggestion.SuggestionReplacement);
        Assert.Equal(87, suggestion.Line);
        Assert.Null(suggestion.StartLine);
        Assert.Equal("RIGHT", suggestion.Side);
        Assert.Null(suggestion.StartSide);
    }

    [Fact]
    public async Task AppServerReviewDraftTool_UsesLocalDiffWhenGitHubFilePatchIsMissing()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        fakeGitHub.PullRequestFiles[0] = fakeGitHub.PullRequestFiles[0] with { Patch = null };
        var fakeLocalDiff = new FakeReviewLocalDiffProvider(new Dictionary<string, string>
        {
            ["src/Auth/RefreshTokenStore.cs"] = FakeGitHubApiClient.RefreshTokenStorePatch
        });
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitCleanReviewDraft);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IReviewLocalDiffProvider>();
            services.AddSingleton<IReviewLocalDiffProvider>(fakeLocalDiff);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare structured PR review suggestions.", null, null));

        var reviewed = await WaitForItemByIdAsync(
            client,
            pr.ItemId!,
            x => x.Item.State == ItemState.AwaitingReview && x.ReviewDrafts.Count == 1);
        var draft = Assert.Single(reviewed.ReviewDrafts);

        Assert.Contains("src/Auth/RefreshTokenStore.cs", fakeLocalDiff.RequestedPaths);
        Assert.Equal(2, draft.AcceptedCount);
        Assert.Equal(0, draft.WarningCount);
    }

    [Fact]
    public async Task AppServerReviewDraftTool_SkipsOnlyAffectedCommentWhenPatchAndFallbackAreMissing()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        fakeGitHub.PullRequestFiles[0] = fakeGitHub.PullRequestFiles[0] with { Patch = null };
        var fakeLocalDiff = new FakeReviewLocalDiffProvider();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitCleanReviewDraft);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IReviewLocalDiffProvider>();
            services.AddSingleton<IReviewLocalDiffProvider>(fakeLocalDiff);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare structured PR review suggestions.", null, null));

        var reviewed = await WaitForItemByIdAsync(
            client,
            pr.ItemId!,
            x => x.Item.State == ItemState.AwaitingReview && x.ReviewDrafts.Count == 1);
        var draft = Assert.Single(reviewed.ReviewDrafts);

        Assert.Contains("src/Auth/RefreshTokenStore.cs", fakeLocalDiff.RequestedPaths);
        Assert.Equal(1, draft.AcceptedCount);
        Assert.Equal(2, draft.WarningCount);
        Assert.Equal(0, draft.MajorCount);
        Assert.Equal(1, draft.MinorCount);
        Assert.Equal("Found 1 issue.", draft.SummaryBody);
        Assert.Contains(draft.Warnings, warning => warning.Contains("src/Auth/RefreshTokenStore.cs", StringComparison.Ordinal));
        Assert.Contains(draft.Warnings, warning => warning.Contains("reviewDraftSuggestionCountMismatch", StringComparison.Ordinal));
        Assert.Contains(draft.Comments, comment => comment.Status == ReviewDraftCommentStatus.Accepted && comment.Path == "src/Auth/JwtMiddleware.cs");
        Assert.Contains(draft.Comments, comment => comment.Status == ReviewDraftCommentStatus.Skipped && comment.Path == "src/Auth/RefreshTokenStore.cs");
    }

    [Fact]
    public async Task AppServerImplementationDraft_AutoPrCreatesGeneratedPullRequestItem()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitImplementationDraft);
        var fakeGit = new FakeGitDeliveryClient();
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IGitDeliveryClient>();
            services.AddSingleton<IGitDeliveryClient>(fakeGit);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var issue = Assert.Single(list!.Items, x => x.Kind == ItemKind.Issue);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{issue.ItemId}/dispatch",
            new DispatchRequest("appServer", "Implement the synced issue.", null, null, "implementation", DeliveryPolicy.AutoPr));

        var reviewed = await WaitForItemByIdAsync(client, issue.ItemId!, x => x.Item.State == ItemState.AwaitingReview && x.ImplementationDrafts.Count == 1);
        var draft = Assert.Single(reviewed.ImplementationDrafts);

        Assert.True((fakeAppServer.LastThreadStartRequest?.DynamicTools ?? []).ContainsTool("oratorio_run", "SubmitImplementationDraft"));
        Assert.NotNull(fakeAppServer.LastThreadStartRequest?.RuntimeAdditionalContext);
        Assert.Equal(RunPurpose.Implementation, Assert.Single(reviewed.Runs, x => x.RunnerKind == "appServer").Purpose);
        Assert.Equal(ImplementationDraftStatus.Delivered, draft.Status);
        Assert.Equal(DeliveryPolicy.AutoPr, draft.DeliveryPolicy);
        Assert.Equal("commit-sha-123", draft.CommitSha);
        Assert.Equal("https://github.example.test/example-owner/oratorio/pull/501", draft.PullRequestUrl);
        Assert.Single(fakeGit.CommitMessages);
        Assert.Single(fakeGit.Pushes);
        var create = Assert.Single(fakeGitHub.PullRequestCreates);
        Assert.Contains("Refs #42", create.Body);
        Assert.NotNull(draft.PullRequestItemId);

        var generated = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{draft.PullRequestItemId}", JsonOptions);
        Assert.NotNull(generated);
        Assert.Equal(ItemKind.PullRequest, generated!.Item.Kind);
        Assert.Equal(issue.ItemId, generated.Item.ParentItemId);
        Assert.Equal(draft.DraftId, generated.Item.GeneratedFromDraftId);
        Assert.Contains(reviewed.SourceWrites, write => write.Kind == SourceWriteKind.LocalCommit && write.Status == SourceWriteStatus.Succeeded);
        Assert.Contains(reviewed.SourceWrites, write => write.Kind == SourceWriteKind.BranchPush && write.Status == SourceWriteStatus.Succeeded);
        Assert.Contains(reviewed.SourceWrites, write => write.Kind == SourceWriteKind.PullRequestCreation && write.Status == SourceWriteStatus.Succeeded);
    }

    [Fact]
    public async Task AppServerImplementationDraft_SourceWriteRetryReusesPushedGitHubBranch()
    {
        var fakeGitHub = new FakeGitHubApiClient { FailNextWriteCount = 1 };
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitImplementationDraft);
        var fakeGit = new FakeGitDeliveryClient();
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IGitDeliveryClient>();
            services.AddSingleton<IGitDeliveryClient>(fakeGit);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var issue = Assert.Single(list!.Items, x => x.Kind == ItemKind.Issue);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{issue.ItemId}/dispatch",
            new DispatchRequest("appServer", "Implement the synced issue.", null, null, "implementation", DeliveryPolicy.AutoPr));
        var failed = await WaitForItemByIdAsync(
            client,
            issue.ItemId!,
            x => x.ImplementationDrafts.Any(draft => draft.Status == ImplementationDraftStatus.DeliveryFailed) &&
                x.SourceWrites.Any(write => write.Kind == SourceWriteKind.PullRequestCreation && write.Status == SourceWriteStatus.Failed));
        var failedWrite = Assert.Single(failed.SourceWrites, write => write.Kind == SourceWriteKind.PullRequestCreation);
        Assert.Equal("deliveryFailed", failedWrite.ErrorCode);
        Assert.Single(fakeGit.CommitMessages);
        Assert.Single(fakeGit.Pushes);
        Assert.Empty(fakeGitHub.PullRequestCreates);

        fakeGit.EmptyDiff = true;
        var retried = await PostAsync<ItemDetailResponse>(client, $"/api/v1/source-writes/{failedWrite.WriteId}/retry", new { });

        var draft = Assert.Single(retried.ImplementationDrafts);
        Assert.Equal(ImplementationDraftStatus.Delivered, draft.Status);
        Assert.Equal("commit-sha-123", draft.CommitSha);
        Assert.Single(fakeGit.CommitMessages);
        Assert.Single(fakeGit.Pushes);
        Assert.Single(fakeGitHub.PullRequestCreates);
        var retriedWrite = Assert.Single(retried.SourceWrites, write => write.WriteId == failedWrite.WriteId);
        Assert.Equal(SourceWriteStatus.Succeeded, retriedWrite.Status);
        Assert.Null(retriedWrite.ErrorCode);
        Assert.DoesNotContain(retried.SourceWrites, write => write.ErrorCode == "invalidGitHubTarget");
    }

    [Fact]
    public async Task ManualImplementationDraft_BlocksApprovalUntilDelivered()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitImplementationDraft);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();
        var task = await CreateLocalTaskAsync(client);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{task.Item.ItemId}/dispatch",
            new DispatchRequest("appServer", "Implement this local task.", null, null, "implementation", DeliveryPolicy.ManualDelivery));

        var reviewed = await WaitForItemByIdAsync(client, task.Item.ItemId, x => x.Item.State == ItemState.AwaitingReview && x.ImplementationDrafts.Count == 1);
        var draft = Assert.Single(reviewed.ImplementationDrafts);
        Assert.Equal(ImplementationDraftStatus.Draft, draft.Status);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/items/id/{task.Item.ItemId}/approve",
            new DecisionRequest("Accept handoff."),
            JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        Assert.Equal("implementationDraftUndelivered", error?.Error.Code);
    }

    [Fact]
    public async Task LocalTaskImplementationDraft_DeliverUsesTaskRepository()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitImplementationDraft);
        var fakeGit = new FakeGitDeliveryClient();
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IGitDeliveryClient>();
            services.AddSingleton<IGitDeliveryClient>(fakeGit);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();
        var task = await CreateLocalTaskAsync(client, repository: "example-owner/oratorio");

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{task.Item.ItemId}/dispatch",
            new DispatchRequest("appServer", "Implement this local task.", null, null, "implementation", DeliveryPolicy.ManualDelivery));

        var reviewed = await WaitForItemByIdAsync(client, task.Item.ItemId, x => x.Item.State == ItemState.AwaitingReview && x.ImplementationDrafts.Count == 1);
        var draft = Assert.Single(reviewed.ImplementationDrafts);
        var delivered = await PostAsync<ItemDetailResponse>(client, $"/api/v1/implementation-drafts/{draft.DraftId}/deliver", new { });
        draft = Assert.Single(delivered.ImplementationDrafts);

        Assert.Equal(ImplementationDraftStatus.Delivered, draft.Status);
        Assert.Equal("example-owner/oratorio", delivered.Item.Repository);
        Assert.Equal("example-owner/oratorio", Assert.Single(fakeGit.Pushes).Repository.FullName);
        Assert.Equal("example-owner/oratorio", Assert.Single(fakeGitHub.PullRequestCreates).Repository.FullName);
        Assert.Contains(delivered.SourceWrites, write => write.Kind == SourceWriteKind.PullRequestCreation && write.Status == SourceWriteStatus.Succeeded);
    }

    [Fact]
    public async Task LocalTaskImplementationDraft_DeliverUsesConfiguredRepositoryWorkspace()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitImplementationDraft);
        var fakeGit = new FakeGitDeliveryClient();
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IGitDeliveryClient>();
            services.AddSingleton<IGitDeliveryClient>(fakeGit);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();
        var task = await CreateLocalTaskAsync(client, repository: "example-owner/oratorio");

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{task.Item.ItemId}/dispatch",
            new DispatchRequest("appServer", "Implement this local task.", null, null, "implementation", DeliveryPolicy.ManualDelivery));

        var reviewed = await WaitForItemByIdAsync(client, task.Item.ItemId, x => x.Item.State == ItemState.AwaitingReview && x.ImplementationDrafts.Count == 1);
        var draft = Assert.Single(reviewed.ImplementationDrafts);
        var delivered = await PostAsync<ItemDetailResponse>(client, $"/api/v1/implementation-drafts/{draft.DraftId}/deliver", new { });
        draft = Assert.Single(delivered.ImplementationDrafts);

        Assert.Equal(ImplementationDraftStatus.Delivered, draft.Status);
        Assert.Equal("example-owner/oratorio", delivered.Item.Repository);
        Assert.Equal("example-owner/oratorio", Assert.Single(fakeGit.Pushes).Repository.FullName);
        Assert.Equal("example-owner/oratorio", Assert.Single(fakeGitHub.PullRequestCreates).Repository.FullName);

        var refreshed = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{task.Item.ItemId}", JsonOptions);
        Assert.Equal("example-owner/oratorio", refreshed!.Item.Repository);
    }

    [Fact]
    public async Task LocalTaskImplementationDraft_DeliverUsesRepositoryWorkspaceMapping()
    {
        var workspacePath = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "oratorio-test-workspace-mapped")).FullName;
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitImplementationDraft);
        await using var app = new TestOratorioApp(
            services =>
            {
                services.RemoveAll<IGitHubApiClient>();
                services.AddSingleton<IGitHubApiClient>(fakeGitHub);
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            },
            new Dictionary<string, string?>
            {
                ["Oratorio:DotCraft:RepositoryWorkspaceRoutes:0:Project"] = "github:github.com/example-owner/oratorio",
                ["Oratorio:DotCraft:RepositoryWorkspaceRoutes:0:WorkspacePath"] = workspacePath,
                ["Oratorio:GitHub:Repositories:1"] = "example-owner/other-repo"
            });
        var client = app.CreateClient();
        var task = await CreateLocalTaskAsync(client, repository: "example-owner/oratorio");

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{task.Item.ItemId}/dispatch",
            new DispatchRequest("appServer", "Implement this local task.", null, null, "implementation", DeliveryPolicy.ManualDelivery));

        var reviewed = await WaitForItemByIdAsync(client, task.Item.ItemId, x => x.Item.State == ItemState.AwaitingReview && x.ImplementationDrafts.Count == 1);
        var draft = Assert.Single(reviewed.ImplementationDrafts);
        var delivered = await PostAsync<ItemDetailResponse>(client, $"/api/v1/implementation-drafts/{draft.DraftId}/deliver", new { });

        Assert.Equal(ImplementationDraftStatus.Delivered, Assert.Single(delivered.ImplementationDrafts).Status);
        Assert.Equal("example-owner/oratorio", delivered.Item.Repository);
        Assert.Equal("example-owner/oratorio", Assert.Single(fakeGitHub.PullRequestCreates).Repository.FullName);
    }

    [Fact]
    public async Task LocalTaskDispatch_MissingRepositoryWorkspaceFails()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitImplementationDraft);
        await using var app = new TestOratorioApp(
            services =>
            {
                services.RemoveAll<IGitHubApiClient>();
                services.AddSingleton<IGitHubApiClient>(fakeGitHub);
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            },
            new Dictionary<string, string?>
            {
                ["Oratorio:GitHub:Repositories:0"] = null
            });
        var client = app.CreateClient();
        var task = await CreateLocalTaskAsync(client, repository: null);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{task.Item.ItemId}/dispatch",
            new DispatchRequest("appServer", "Implement this local task.", null, null, "implementation", DeliveryPolicy.ManualDelivery));

        var failed = await WaitForItemByIdAsync(client, task.Item.ItemId, x => x.Item.State == ItemState.Failed);

        Assert.Empty(failed.ImplementationDrafts);
        Assert.Contains("No repository workspace is configured", failed.Item.LatestSummary);
    }

    [Fact]
    public async Task AppServerFollowUpDraft_InheritsPullRequestRouteWhenBranchOmitted()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitFollowUpDraft)
        {
            FollowUpDraftBranch = null
        };
        var fakeWorktree = new FakeWorktreeManager();
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IWorktreeManager>();
            services.AddSingleton<IWorktreeManager>(fakeWorktree);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Review and capture follow-up work.", null, null));

        var reviewed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.AwaitingReview && x.FollowUpDrafts.Count == 1);
        var draft = Assert.Single(reviewed.FollowUpDrafts);
        Assert.Null(draft.Branch);

        var created = await PostAsync<ItemDetailResponse>(client, $"/api/v1/follow-up-drafts/{draft.DraftId}/create-local-task", new { });
        var createdDraft = Assert.Single(created.FollowUpDrafts);
        var localTask = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{createdDraft.CreatedItemId}", JsonOptions);
        Assert.NotNull(localTask);
        Assert.Equal(ItemKind.LocalTask, localTask!.Item.Kind);
        Assert.Equal("example-owner/oratorio", localTask.Item.Repository);
        Assert.Equal("feature/auth-refresh", localTask.Item.Branch);
        Assert.Equal("abc123", localTask.Item.HeadSha);
        Assert.Equal(pr.ItemId, localTask.Item.ParentItemId);

        fakeAppServer.Outcome = FakeAppServerOutcome.SubmitImplementationDraft;
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{localTask.Item.ItemId}/dispatch",
            new DispatchRequest("appServer", "Implement this inherited follow-up task.", null, null, "implementation", DeliveryPolicy.ManualDelivery));

        await WaitForItemByIdAsync(client, localTask.Item.ItemId, x => x.Item.State == ItemState.AwaitingReview && x.ImplementationDrafts.Count == 1);
        var localPrepareRequest = Assert.Single(fakeWorktree.PrepareRequests, x => x.ItemId == localTask.Item.ItemId);
        Assert.Equal("local", localPrepareRequest.Source);
        Assert.Equal("feature/auth-refresh", localPrepareRequest.SourceBranch);
        Assert.Equal("abc123", localPrepareRequest.HeadSha);
        Assert.Equal("refs/pull/184/head", localPrepareRequest.ReviewTargetFetchRef);
    }

    [Fact]
    public async Task FollowUpDraft_CreateLocalTask_DoesNotInheritBranchFromNonPullRequestParent()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();
        var parent = await CreateLocalTaskAsync(client, "Parent local follow-up", repository: "example-owner/oratorio");
        string draftId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            var item = await db.Items.FirstAsync(x => x.ItemId == parent.Item.ItemId);
            var now = DateTimeOffset.UtcNow;
            item.State = ItemState.AwaitingReview;
            item.CurrentRound = 1;
            item.HeadSha = "local-parent-head";
            var round = new OratorioRound
            {
                ItemId = item.ItemId,
                RoundNumber = 1,
                Status = RoundStatus.AwaitingReview,
                CreatedAt = now
            };
            var run = new OratorioRun
            {
                ItemId = item.ItemId,
                RoundId = round.RoundId,
                Attempt = 1,
                Status = RunStatus.Succeeded,
                RunnerKind = "appServer",
                StartedAt = now,
                CompletedAt = now,
                Purpose = RunPurpose.ReviewAnalysis
            };
            var draft = new OratorioFollowUpDraft
            {
                ItemId = item.ItemId,
                RoundId = round.RoundId,
                RunId = run.RunId,
                Status = FollowUpDraftStatus.Draft,
                Title = "Separate local cleanup",
                Body = "This should become a local task without inheriting the parent local branch.",
                CreatedAt = now,
                UpdatedAt = now
            };
            db.Rounds.Add(round);
            db.Runs.Add(run);
            db.FollowUpDrafts.Add(draft);
            await db.SaveChangesAsync();
            draftId = draft.DraftId;
        }

        var created = await PostAsync<ItemDetailResponse>(client, $"/api/v1/follow-up-drafts/{draftId}/create-local-task", new { });
        var createdDraft = Assert.Single(created.FollowUpDrafts);
        var localTask = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{createdDraft.CreatedItemId}", JsonOptions);
        Assert.NotNull(localTask);
        Assert.Equal("example-owner/oratorio", localTask!.Item.Repository);
        Assert.Null(localTask.Item.Branch);
        Assert.Null(localTask.Item.HeadSha);
    }

    [Fact]
    public async Task AppServerFollowUpDraft_CanBeEditedAndCreatedAsLocalTask()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitFollowUpDraft);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Review and capture follow-up work.", null, null));

        var reviewed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.AwaitingReview && x.FollowUpDrafts.Count == 1);
        var draft = Assert.Single(reviewed.FollowUpDrafts);

        Assert.True((fakeAppServer.LastThreadStartRequest?.DynamicTools ?? []).ContainsTool("oratorio_run", "SubmitReviewDraft"));
        Assert.True((fakeAppServer.LastThreadStartRequest?.DynamicTools ?? []).ContainsTool("oratorio_run", "SubmitFollowUpDraft"));
        Assert.NotNull(fakeAppServer.LastThreadStartRequest?.RuntimeAdditionalContext);
        Assert.Equal(FollowUpDraftStatus.Draft, draft.Status);
        Assert.Equal("Split out migration cleanup", draft.Title);
        Assert.Equal("example-owner/oratorio", draft.Repository);
        Assert.Contains("backend", draft.Labels);

        var edited = await PatchAsync<ItemDetailResponse>(
            client,
            $"/api/v1/follow-up-drafts/{draft.DraftId}",
            new FollowUpDraftUpdateRequest(
                "Edited follow-up",
                "Edited local task body.",
                "Still separate scope.",
                "example-owner/oratorio",
                "operator",
                "feature/follow-up",
                ["follow-up", "edited"]));
        var editedDraft = Assert.Single(edited.FollowUpDrafts);
        Assert.Equal("Edited follow-up", editedDraft.Title);
        Assert.Equal("operator", editedDraft.Assignee);

        var created = await PostAsync<ItemDetailResponse>(client, $"/api/v1/follow-up-drafts/{draft.DraftId}/create-local-task", new { });
        var createdDraft = Assert.Single(created.FollowUpDrafts);
        Assert.Equal(FollowUpDraftStatus.Created, createdDraft.Status);
        Assert.NotNull(createdDraft.CreatedItemId);

        var localTask = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{createdDraft.CreatedItemId}", JsonOptions);
        Assert.NotNull(localTask);
        Assert.Equal(ItemKind.LocalTask, localTask!.Item.Kind);
        Assert.Equal("Edited follow-up", localTask.Item.Title);
        Assert.Equal("Edited local task body.", localTask.Item.Description);
        Assert.Equal("feature/follow-up", localTask.Item.Branch);
        Assert.Null(localTask.Item.HeadSha);
        Assert.Equal(pr.ItemId, localTask.Item.ParentItemId);
        Assert.Equal(draft.DraftId, localTask.Item.GeneratedFromDraftId);

        var createAgain = await client.PostAsJsonAsync($"/api/v1/follow-up-drafts/{draft.DraftId}/create-local-task", new { }, JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, createAgain.StatusCode);
    }

    [Fact]
    public async Task ReviewDraftPublish_CreatesCommentReviewWithInlineSuggestions()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitCleanReviewDraft);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare structured PR review suggestions.", null, null));
        var reviewed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.AwaitingReview && x.ReviewDrafts.Count == 1);
        var draft = Assert.Single(reviewed.ReviewDrafts);

        var edited = await PatchAsync<ItemDetailResponse>(
            client,
            $"/api/v1/review-drafts/{draft.DraftId}",
            new ReviewDraftUpdateRequest(
                "Edited summary.",
                [new ReviewDraftCommentUpdateRequest(draft.Comments.First(x => x.Status == ReviewDraftCommentStatus.Accepted && x.SuggestionReplacement is not null).DraftCommentId, "Edited inline body.", "return editedToken;")]));
        Assert.Equal("Edited summary.", Assert.Single(edited.ReviewDrafts).SummaryBody);

        var published = await PostAsync<ItemDetailResponse>(client, $"/api/v1/review-drafts/{draft.DraftId}/publish", new { });

        var publishedDraft = Assert.Single(published.ReviewDrafts);
        Assert.Equal(ReviewDraftStatus.Published, publishedDraft.Status);
        Assert.Equal(ItemState.AwaitingReview, published.Item.State);
        Assert.Single(fakeGitHub.PullRequestReviews);
        var review = fakeGitHub.PullRequestReviews[0];
        Assert.Equal("COMMENT", review.Event);
        Assert.Equal("Edited summary.", review.Body);
        Assert.Equal("abc123", review.CommitId);
        Assert.Equal(2, review.Comments.Count);
        Assert.Contains(review.Comments, comment => comment.Path == "src/Auth/RefreshTokenStore.cs" && comment.Line == 87 && comment.Side == "RIGHT");
        Assert.DoesNotContain(review.Comments, comment => comment.Path == "missing/File.cs");
        Assert.DoesNotContain(review.Comments, comment => comment.Side == "LEFT");
        Assert.Contains(review.Comments, comment => comment.Body.StartsWith("**🔴 Missing refresh guard**", StringComparison.Ordinal) && comment.Body.Contains("```suggestion", StringComparison.Ordinal) && comment.Body.Contains("return editedToken;", StringComparison.Ordinal));
        Assert.Contains(review.Comments, comment => comment.Body.StartsWith("**🟡 Validate middleware setup**", StringComparison.Ordinal) && comment.Path == "src/Auth/JwtMiddleware.cs" && !comment.Body.Contains("```suggestion", StringComparison.Ordinal));
        Assert.Contains(published.SourceWrites, write => write.Kind == SourceWriteKind.PullRequestReview && write.Intent == "reviewDraftPublish" && write.Status == SourceWriteStatus.Succeeded);

        var publishAgain = await client.PostAsJsonAsync($"/api/v1/review-drafts/{draft.DraftId}/publish", new { }, JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, publishAgain.StatusCode);
    }

    [Fact]
    public async Task ReviewFindingResolution_CapturesThreadIdsAndResolvesGitHubReviewThread()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitCleanReviewDraft);
        await using var app = new TestOratorioApp(
            services =>
            {
                services.RemoveAll<IGitHubApiClient>();
                services.AddSingleton<IGitHubApiClient>(fakeGitHub);
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare structured PR review suggestions.", null, null));
        var reviewed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.AwaitingReview && x.ReviewDrafts.Count == 1);
        var draft = Assert.Single(reviewed.ReviewDrafts);

        await PostAsync<ItemDetailResponse>(client, $"/api/v1/review-drafts/{draft.DraftId}/publish", new { });

        // Step B: GraphQL review-thread mapping captured a remote thread id per accepted finding.
        string findingId;
        string threadId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            var comments = await db.ReviewDraftComments
                .Where(c => c.DraftId == draft.DraftId && c.Status == ReviewDraftCommentStatus.Accepted)
                .ToListAsync();
            Assert.NotEmpty(comments);
            Assert.All(comments, c => Assert.False(string.IsNullOrWhiteSpace(c.RemoteThreadId)));
            findingId = comments[0].DraftCommentId;
            threadId = comments[0].RemoteThreadId!;
        }

        // Step C: resolving and reopening propagate to the GitHub review thread.
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/review-drafts/{draft.DraftId}/comments/{findingId}/resolve",
            new ResolveReviewFindingOperatorRequest("fixed", null));
        Assert.Contains(fakeGitHub.ReviewThreadResolutions, x => x.ThreadId == threadId && x.Resolved);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/review-drafts/{draft.DraftId}/comments/{findingId}/reopen",
            new { });
        Assert.Contains(fakeGitHub.ReviewThreadResolutions, x => x.ThreadId == threadId && !x.Resolved);
    }

    [Fact]
    public async Task AutoReviewPublish_PublishesValidDraftAsCommentReviewOnly()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitCleanReviewDraft);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        }, settings: new Dictionary<string, string?>
        {
            ["Oratorio:Automation:AutoReviewPublishEnabled"] = "true",
            ["Oratorio:Automation:AutoReviewPublishRepositories:0"] = "example-owner/oratorio"
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare structured PR review suggestions.", null, null));
        var reviewed = await WaitForItemByIdAsync(
            client,
            pr.ItemId!,
            x => x.SourceWrites.Any(write =>
                write.Kind == SourceWriteKind.PullRequestReview &&
                write.Intent == "reviewDraftPublish" &&
                write.Status == SourceWriteStatus.Succeeded));

        var draft = Assert.Single(reviewed.ReviewDrafts);
        Assert.Equal(ReviewDraftStatus.Published, draft.Status);
        Assert.Equal(ItemState.AwaitingReview, reviewed.Item.State);
        var review = Assert.Single(fakeGitHub.PullRequestReviews);
        Assert.Equal("COMMENT", review.Event);
        Assert.Equal("Found 2 issues.", review.Body);
        Assert.Equal("abc123", review.CommitId);
        Assert.Equal(2, review.Comments.Count);
        var check = Assert.Single(fakeGitHub.CheckRuns);
        Assert.Equal("completed", check.Status);
        Assert.Equal("neutral", check.Conclusion);
        Assert.Contains(reviewed.SourceWrites, write => write.Kind == SourceWriteKind.PullRequestReview && write.Intent == "reviewDraftPublish" && write.Status == SourceWriteStatus.Succeeded);
    }

    [Fact]
    public async Task AutoReviewPublish_BlocksDraftsWithWarningsAsFailedSourceWrite()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitNoOpReviewDraft);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        }, settings: new Dictionary<string, string?>
        {
            ["Oratorio:Automation:AutoReviewPublishEnabled"] = "true",
            ["Oratorio:Automation:AutoReviewPublishRepositories:0"] = "example-owner/oratorio"
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare structured PR review suggestions.", null, null));
        var reviewed = await WaitForItemByIdAsync(
            client,
            pr.ItemId!,
            x => x.SourceWrites.Any(write =>
                write.Intent == "reviewDraftPublish" &&
                write.Status == SourceWriteStatus.Failed &&
                write.ErrorCode == "reviewDraftWarnings"));

        var draft = Assert.Single(reviewed.ReviewDrafts);
        Assert.Equal(ReviewDraftStatus.PublishFailed, draft.Status);
        Assert.Equal(2, draft.WarningCount);
        Assert.Empty(fakeGitHub.PullRequestReviews);
        var write = Assert.Single(reviewed.SourceWrites, x => x.Intent == "reviewDraftPublish");
        Assert.Equal(SourceWriteStatus.Failed, write.Status);
        Assert.Equal("reviewDraftWarnings", write.ErrorCode);
    }

    [Fact]
    public async Task AutoReviewPublish_BlocksStaleHeadAsFailedSourceWrite()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitCleanReviewDraft);
        var fakeWorktree = new FakeWorktreeManager { BaseShaOverride = "older-sha" };
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.RemoveAll<IWorktreeManager>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            services.AddSingleton<IWorktreeManager>(fakeWorktree);
        }, settings: new Dictionary<string, string?>
        {
            ["Oratorio:Automation:AutoReviewPublishEnabled"] = "true",
            ["Oratorio:Automation:AutoReviewPublishRepositories:0"] = "example-owner/oratorio"
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare structured PR review suggestions.", null, null));
        var reviewed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.SourceWrites.Any(write => write.ErrorCode == "stalePullRequestHead"));

        Assert.Equal(ReviewDraftStatus.PublishFailed, Assert.Single(reviewed.ReviewDrafts).Status);
        Assert.Empty(fakeGitHub.PullRequestReviews);
        var write = Assert.Single(reviewed.SourceWrites, x => x.Intent == "reviewDraftPublish");
        Assert.Equal(SourceWriteStatus.Failed, write.Status);
        Assert.Equal("stalePullRequestHead", write.ErrorCode);
    }

    [Fact]
    public async Task AutoReviewPublish_BlocksDisabledGitHubWritesAsFailedSourceWrite()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitCleanReviewDraft);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        }, settings: new Dictionary<string, string?>
        {
            ["Oratorio:GitHub:WritesEnabled"] = "false",
            ["Oratorio:Automation:AutoReviewPublishEnabled"] = "true",
            ["Oratorio:Automation:AutoReviewPublishRepositories:0"] = "example-owner/oratorio"
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare structured PR review suggestions.", null, null));
        var reviewed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.SourceWrites.Any(write => write.Intent == "reviewDraftPublish" && write.ErrorCode == "githubWritesDisabled"));

        Assert.Equal(ReviewDraftStatus.PublishFailed, Assert.Single(reviewed.ReviewDrafts).Status);
        Assert.Empty(fakeGitHub.PullRequestReviews);
        var write = Assert.Single(reviewed.SourceWrites, x => x.Intent == "reviewDraftPublish");
        Assert.Equal(SourceWriteStatus.Failed, write.Status);
        Assert.Equal("githubWritesDisabled", write.ErrorCode);
    }

    [Fact]
    public async Task ReviewDraftPublishRetry_PreservesInlineSuggestionsAndMarksDraftPublished()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitCleanReviewDraft);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare structured PR review suggestions.", null, null));
        var reviewed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.AwaitingReview && x.ReviewDrafts.Count == 1);
        var draft = Assert.Single(reviewed.ReviewDrafts);

        var acceptedComment = draft.Comments.First(x => x.Status == ReviewDraftCommentStatus.Accepted && x.SuggestionReplacement is not null);
        _ = await PatchAsync<ItemDetailResponse>(
            client,
            $"/api/v1/review-drafts/{draft.DraftId}",
            new ReviewDraftUpdateRequest(
                "Retryable review summary.",
                [new ReviewDraftCommentUpdateRequest(acceptedComment.DraftCommentId, "Retry inline body.", "return retryToken;")]));

        fakeGitHub.FailNextWriteCount = 1;
        var failedPublish = await PostAsync<ItemDetailResponse>(client, $"/api/v1/review-drafts/{draft.DraftId}/publish", new { });

        var failedDraft = Assert.Single(failedPublish.ReviewDrafts);
        Assert.Equal(ReviewDraftStatus.PublishFailed, failedDraft.Status);
        Assert.Empty(fakeGitHub.PullRequestReviews);
        var failedWrite = Assert.Single(failedPublish.SourceWrites, write => write.Intent == "reviewDraftPublish");
        Assert.Equal(SourceWriteStatus.Failed, failedWrite.Status);
        using (var document = JsonDocument.Parse(failedWrite.RequestJson))
        {
            var comments = document.RootElement.GetProperty("comments");
            Assert.Contains(
                comments.EnumerateArray(),
                comment => (comment.GetProperty("body").GetString() ?? "").Contains("```suggestion", StringComparison.Ordinal) &&
                    (comment.GetProperty("body").GetString() ?? "").Contains("return retryToken;", StringComparison.Ordinal));
        }

        var retried = await PostAsync<ItemDetailResponse>(client, $"/api/v1/source-writes/{failedWrite.WriteId}/retry", new { });

        var retriedWrite = Assert.Single(retried.SourceWrites, write => write.WriteId == failedWrite.WriteId);
        Assert.Equal(SourceWriteStatus.Succeeded, retriedWrite.Status);
        Assert.Equal(2, retriedWrite.AttemptCount);
        var publishedDraft = Assert.Single(retried.ReviewDrafts);
        Assert.Equal(ReviewDraftStatus.Published, publishedDraft.Status);
        Assert.NotNull(publishedDraft.PublishedAt);
        Assert.Equal(failedWrite.WriteId, publishedDraft.SourceWriteId);
        var review = Assert.Single(fakeGitHub.PullRequestReviews);
        Assert.Equal("COMMENT", review.Event);
        Assert.Equal(2, review.Comments.Count);
        Assert.Contains(review.Comments, comment => comment.Body.StartsWith("**🔴 Missing refresh guard**", StringComparison.Ordinal) && comment.Body.Contains("```suggestion", StringComparison.Ordinal) && comment.Body.Contains("return retryToken;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AppServerDispatch_FailedTurn_MapsToFailedItem()
    {
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.Fail);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await CreateItemAsync(client, "task:test-appserver-fail");
        await PostAsync<ItemDetailResponse>(
            client,
            "/api/v1/items/local/task:test-appserver-fail/dispatch",
            new DispatchRequest("appServer", "Fail this DotCraft run.", null, null));

        var failed = await WaitForItemAsync(client, "task:test-appserver-fail", x => x.Item.State == ItemState.Failed);
        Assert.Equal(CheckState.Failing, failed.Item.CheckState);
        Assert.Contains(failed.Runs, x => x.RunnerKind == "appServer" && x.Status == RunStatus.Failed && x.ErrorCode == "appServerTurnFailed");
    }

    [Fact]
    public async Task AppServerDispatch_IsRejectedByDecisionsWhileActive()
    {
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.Hold);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await CreateItemAsync(client, "task:test-appserver-active");
        await PostAsync<ItemDetailResponse>(
            client,
            "/api/v1/items/local/task:test-appserver-active/dispatch",
            new DispatchRequest("appServer", "Keep the DotCraft run active.", null, null));

        var response = await client.PostAsJsonAsync(
            "/api/v1/items/local/task:test-appserver-active/approve",
            new DecisionRequest("Looks fine."),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        Assert.Equal("invalidTransition", error?.Error.Code);
    }

    [Fact]
    public async Task GitHubTokenProvider_RequiresAppAuthentication()
    {
        var handler = new CountingHandler();
        var provider = new GitHubTokenProvider(
            new StaticHttpClientFactory(new HttpClient(handler) { BaseAddress = new Uri("https://api.github.test") }),
            new StaticOptionsMonitor<GitHubOptions>(new GitHubOptions
            {
                Endpoint = "https://api.github.test"
            }),
            new FixedClock(DateTimeOffset.Parse("2026-05-04T00:00:00Z")),
            new GitHubCredentialResolver(new PassthroughConfigurationSecretProtector()),
            new GitHubInstallationResolver(
                new StaticHttpClientFactory(new HttpClient(handler) { BaseAddress = new Uri("https://api.github.test") }),
                new FixedClock(DateTimeOffset.Parse("2026-05-04T00:00:00Z")),
                new GitHubCredentialResolver(new PassthroughConfigurationSecretProtector())));

        var error = await Assert.ThrowsAsync<GitHubAppAuthenticationRequiredException>(
            () => provider.GetBearerTokenAsync(new GitHubRepositoryRef("dotcraft", "oratorio"), CancellationToken.None));

        Assert.Equal("githubAppAuthRequired", error.ErrorCode);
        Assert.Contains("GitHub App ID and private key", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task GitHubTokenProvider_ExchangesJwt_AndCachesInstallationToken()
    {
        using var rsa = RSA.Create(2048);
        var privateKey = TestHelpers.ExportPrivateKeyPem(rsa);
        var handler = new CountingHandler();
        var provider = new GitHubTokenProvider(
            new StaticHttpClientFactory(new HttpClient(handler) { BaseAddress = new Uri("https://api.github.test") }),
            new StaticOptionsMonitor<GitHubOptions>(new GitHubOptions
            {
                Endpoint = "https://api.github.test",
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
                PrivateKey = privateKey
            }),
            new FixedClock(DateTimeOffset.Parse("2026-05-04T00:00:00Z")),
            new GitHubCredentialResolver(new PassthroughConfigurationSecretProtector()),
            new GitHubInstallationResolver(
                new StaticHttpClientFactory(new HttpClient(handler) { BaseAddress = new Uri("https://api.github.test") }),
                new FixedClock(DateTimeOffset.Parse("2026-05-04T00:00:00Z")),
                new GitHubCredentialResolver(new PassthroughConfigurationSecretProtector())));

        var first = await provider.GetBearerTokenAsync(new GitHubRepositoryRef("dotcraft", "oratorio"), CancellationToken.None);
        var second = await provider.GetBearerTokenAsync(new GitHubRepositoryRef("dotcraft", "second"), CancellationToken.None);

        Assert.Equal("installation-token", first);
        Assert.Equal(first, second);
        Assert.Equal(1, handler.Count);
        Assert.Equal("/app/installations/98765/access_tokens", handler.LastRequestUri?.AbsolutePath);
        Assert.Equal("Bearer", handler.LastAuthorization?.Scheme);
    }

    [Fact]
    public async Task GitHubInstallationResolver_DiscoversRepositoryInstallation()
    {
        using var rsa = RSA.Create(2048);
        var privateKey = TestHelpers.ExportPrivateKeyPem(rsa);
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { id = 24680 })
        });
        var resolver = new GitHubInstallationResolver(
            new StaticHttpClientFactory(new HttpClient(handler)),
            new FixedClock(DateTimeOffset.Parse("2026-05-04T00:00:00Z")),
            new GitHubCredentialResolver(new PassthroughConfigurationSecretProtector()));

        var result = await resolver.DiscoverAsync(
            new GitHubOptions
            {
                Endpoint = "https://api.github.test",
                AppId = "12345",
                PrivateKey = privateKey
            },
            new GitHubRepositoryRef("example-owner", "oratorio"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("24680", result.InstallationId);
        Assert.Equal("api.github.test", result.Instance);
        Assert.Equal("/repos/example-owner/oratorio/installation", handler.LastRequestUri?.AbsolutePath);
        Assert.Equal("Bearer", handler.LastAuthorization?.Scheme);
    }

    [Fact]
    public async Task GitHubInstallationResolver_ReturnsFailureForInvalidPrivateKey()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { id = 24680 })
        });
        var resolver = new GitHubInstallationResolver(
            new StaticHttpClientFactory(new HttpClient(handler)),
            new FixedClock(DateTimeOffset.Parse("2026-05-04T00:00:00Z")),
            new GitHubCredentialResolver(new PassthroughConfigurationSecretProtector()));

        var result = await resolver.DiscoverAsync(
            new GitHubOptions
            {
                Endpoint = "https://api.github.test",
                AppId = "12345",
                PrivateKey = "not a pem private key"
            },
            new GitHubRepositoryRef("dotcraft", "oratorio"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("githubInstallationDiscoveryFailed", result.Code);
        Assert.Null(handler.LastRequestUri);
    }

    [Fact]
    public async Task GitHubApiClient_CreatePullRequestReview_OmitsNullOptionalCommentFields()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(new { id = 200, html_url = "https://github.example/review/200" })
        });
        var client = CreateGitHubApiClient(handler);

        await client.CreatePullRequestReviewAsync(
            new GitHubRepositoryRef("dotcraft", "oratorio"),
            184,
            "COMMENT",
            "Summary",
            "abc123",
            [new GitHubPullRequestReviewCommentWrite("README.md", "Body", 4, "RIGHT", null, null)],
            CancellationToken.None);

        using var document = JsonDocument.Parse(handler.LastRequestBody!);
        var comment = document.RootElement.GetProperty("comments")[0];
        Assert.Equal("README.md", comment.GetProperty("path").GetString());
        Assert.Equal(4, comment.GetProperty("line").GetInt32());
        Assert.False(comment.TryGetProperty("start_line", out _));
        Assert.False(comment.TryGetProperty("start_side", out _));
    }

    [Fact]
    public async Task GitHubApiClient_CreatePullRequestReviewThreadReply_UsesGraphQlMutation()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"data":{"addPullRequestReviewThreadReply":{"comment":{"id":"PRRC_reply"}}}}""",
                Encoding.UTF8,
                "application/json")
        });
        var client = CreateGitHubApiClient(handler);

        var response = await client.CreatePullRequestReviewThreadReplyAsync(
            new GitHubRepositoryRef("dotcraft", "oratorio"),
            "PRRT_thread",
            "Resolved because the follow-up commit addressed it.",
            CancellationToken.None);

        Assert.Equal("PRRC_reply", response.ExternalId);
        Assert.Equal("/graphql", handler.LastRequestUri?.AbsolutePath);
        using var document = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Contains("addPullRequestReviewThreadReply", document.RootElement.GetProperty("query").GetString(), StringComparison.Ordinal);
        var variables = document.RootElement.GetProperty("variables");
        Assert.Equal("PRRT_thread", variables.GetProperty("threadId").GetString());
        Assert.Equal("Resolved because the follow-up commit addressed it.", variables.GetProperty("body").GetString());
    }

    [Fact]
    public async Task GitHubApiClient_WriteFailure_IncludesGitHubResponseBody()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent("""{"message":"Validation Failed","errors":[{"field":"comments"}]}""", Encoding.UTF8, "application/json")
        });
        var client = CreateGitHubApiClient(handler);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.CreatePullRequestReviewAsync(
            new GitHubRepositoryRef("dotcraft", "oratorio"),
            184,
            "COMMENT",
            "Summary",
            "abc123",
            [new GitHubPullRequestReviewCommentWrite("README.md", "Body", 4, "RIGHT", null, null)],
            CancellationToken.None));

        Assert.Contains("422", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Validation Failed", exception.Message, StringComparison.Ordinal);
        Assert.Contains("comments", exception.Message, StringComparison.Ordinal);
    }

    private static GitHubApiClient CreateGitHubApiClient(HttpMessageHandler handler) =>
        new(
            new StaticHttpClientFactory(new HttpClient(handler)),
            new StaticOptionsMonitor<GitHubOptions>(new GitHubOptions { Endpoint = "https://api.github.test" }),
            new StaticGitHubTokenProvider());

    private static async Task CreateItemAsync(HttpClient client, string externalId)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/items",
            new CreateItemRequest(
                "local",
                externalId,
                ItemKind.PullRequest,
                "Test item",
                "A test item.",
                "example-owner/oratorio",
                "operator",
                "test/oratorio"),
            JsonOptions);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Create item failed with {(int)response.StatusCode}: {body}");
        }
    }

    private static Task<ItemDetailResponse> CreateLocalTaskAsync(
        HttpClient client,
        string title = "Write local task docs",
        string? repository = "example-owner/oratorio") =>
        PostAsync<ItemDetailResponse>(
            client,
            "/api/v1/local-tasks",
            new CreateLocalTaskRequest(
                title,
                "Document the M1 local task workflow.",
                repository,
                "operator",
                "local/m1",
                ["m1", "local"]));

    private static async Task AssertCancelRejectedAsync(HttpClient client, string path)
    {
        var response = await client.PostAsJsonAsync(path, new CancelRunRequest("Stop."), JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        Assert.Equal("runNotCancellable", error?.Error.Code);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(8);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Timed out waiting for predicate.");
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(8);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await predicate())
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Timed out waiting for async predicate.");
    }

    private static async Task<GitHubSyncJobDto> WaitForGitHubSyncJobAsync(HttpClient client, string jobId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(8);
        GitHubSyncJobDto? latest = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            latest = await client.GetFromJsonAsync<GitHubSyncJobDto>($"/api/v1/sources/github/sync-jobs/{jobId}", JsonOptions);
            if (latest is not null && latest.Status is not (GitHubSyncStatus.Queued or GitHubSyncStatus.Running))
            {
                return latest;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Timed out waiting for GitHub sync job {jobId}. Latest state: {latest?.Status}");
    }

    private static async Task<ItemDetailResponse> CreateGitHubPullRequestInReviewAsync(HttpClient client)
    {
        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("mock", "Run GitHub PR review.", MockOutcome.Success, 1));
        return await WaitForItemByIdAsync(client, pr.ItemId, x => x.Item.State == ItemState.AwaitingReview);
    }

    private static async Task<ItemDetailResponse> CreateGitHubIssueInReviewAsync(HttpClient client)
    {
        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var issue = Assert.Single(list!.Items, x => x.Kind == ItemKind.Issue);
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{issue.ItemId}/dispatch",
            new DispatchRequest("mock", "Run GitHub issue review.", MockOutcome.Success, 1));
        return await WaitForItemByIdAsync(client, issue.ItemId, x => x.Item.State == ItemState.AwaitingReview);
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string path, object request)
    {
        var response = await client.PostAsJsonAsync(path, request, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions)
            ?? throw new InvalidOperationException($"Expected {typeof(T).Name} response.");
    }

    private static async Task<T> PutAsync<T>(HttpClient client, string path, object request)
    {
        var response = await client.PutAsJsonAsync(path, request, JsonOptions);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Response status code does not indicate success: {(int)response.StatusCode} ({response.ReasonPhrase}). {body}");
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions)
            ?? throw new InvalidOperationException($"Expected {typeof(T).Name} response.");
    }

    private static async Task<T> PatchAsync<T>(HttpClient client, string path, object request)
    {
        var response = await PatchRawAsync(client, path, request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions)
            ?? throw new InvalidOperationException($"Expected {typeof(T).Name} response.");
    }

    private static Task<HttpResponseMessage> PatchRawAsync(HttpClient client, string path, object request) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, path)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        });

    private static async Task<ItemDetailResponse> WaitForItemAsync(
        HttpClient client,
        string externalId,
        Func<ItemDetailResponse, bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(8);
        ItemDetailResponse? latest = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            latest = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/local/{externalId}", JsonOptions);
            if (latest is not null && predicate(latest))
            {
                return latest;
            }

            await Task.Delay(150);
        }

        throw new TimeoutException($"Timed out waiting for {externalId}. Latest state: {latest?.Item.State}");
    }

    private static async Task<ItemDetailResponse> WaitForItemByIdAsync(
        HttpClient client,
        string itemId,
        Func<ItemDetailResponse, bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(8);
        ItemDetailResponse? latest = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            latest = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{itemId}", JsonOptions);
            if (latest is not null && predicate(latest))
            {
                return latest;
            }

            await Task.Delay(150);
        }

        throw new TimeoutException($"Timed out waiting for {itemId}. Latest state: {latest?.Item.State}");
    }

    private static async Task DispatchAutoReviewAsync(TestOratorioApp app)
    {
        using var scope = app.Services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<AutoReviewDispatchService>();
        await dispatcher.DispatchEligibleAsync(CancellationToken.None);
    }

    private static GitHubIssue TestGitHubIssue(long id, int number, string title, string repository, bool isPullRequest = false) =>
        new(
            id,
            number,
            title,
            $"Body for {title}.",
            "open",
            $"https://github.example.test/{repository}/{(isPullRequest ? "pull" : "issues")}/{number}",
            new GitHubUser("tester"),
            [],
            [],
            DateTimeOffset.Parse("2026-05-01T10:00:00Z"),
            DateTimeOffset.Parse("2026-05-02T10:00:00Z"),
            null,
            isPullRequest ? new GitHubPullMarker($"https://github.example.test/{repository}/pull/{number}") : null);

    private static GitHubPullRequest TestGitHubPullRequest(long id, int number, string title, string repository) =>
        new(
            id,
            number,
            title,
            $"Body for {title}.",
            "open",
            $"https://github.example.test/{repository}/pull/{number}",
            new GitHubUser("tester"),
            [],
            [],
            DateTimeOffset.Parse("2026-05-01T11:00:00Z"),
            DateTimeOffset.Parse("2026-05-02T12:00:00Z"),
            null,
            null,
            false,
            new GitHubBranchRef("feature/test", "head-sha"),
            new GitHubBranchRef("main", "base-sha"));

    private static void MoveDefaultPullRequestHead(FakeGitHubApiClient fakeGitHub, string headSha, DateTimeOffset? updatedAt = null)
    {
        var updated = updatedAt ?? DateTimeOffset.Parse("2026-05-04T12:00:00Z");
        fakeGitHub.Issues[1] = fakeGitHub.Issues[1] with { UpdatedAt = updated };
        fakeGitHub.PullRequests[0] = fakeGitHub.PullRequests[0] with
        {
            UpdatedAt = updated,
            Head = fakeGitHub.PullRequests[0].Head with { Sha = headSha }
        };
    }

    private static async Task MarkRunAsIncompatibleThreadContextAsync(TestOratorioApp app, string runId)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var run = await db.Runs.Include(x => x.Round).FirstAsync(x => x.RunId == runId);
        var context = JsonNode.Parse(run.PromptContextJson!)!.AsObject();
        context.Remove("threadStateWorkspaceMode");
        run.PromptContextJson = context.ToJsonString(JsonOptions);
        if (run.Round is not null)
        {
            run.Round.PromptContextJson = run.PromptContextJson;
        }

        await db.SaveChangesAsync();
    }
}

internal sealed class TestOratorioApp : WebApplicationFactory<Program>
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), "oratorio-tests", $"{Guid.NewGuid():n}.db");
    private readonly Action<IServiceCollection>? _configureServices;
    private readonly IReadOnlyDictionary<string, string?> _settings;

    public TestOratorioApp(
        Action<IServiceCollection>? configureServices = null,
        IReadOnlyDictionary<string, string?>? settings = null)
    {
        _configureServices = configureServices;
        _settings = settings ?? new Dictionary<string, string?>();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        var defaultWorkspace = Path.Combine(Path.GetTempPath(), "oratorio-test-workspace");
        Directory.CreateDirectory(defaultWorkspace);
        builder.UseSetting("Oratorio:DatabasePath", _databasePath);
        builder.UseSetting("Oratorio:DotCraft:MaxRunAttempts", "1");
        builder.UseSetting("Oratorio:DotCraft:RetryBackoffSeconds", "1");
        builder.UseSetting("Oratorio:DotCraft:MaxRetryBackoffSeconds", "1");
        builder.UseSetting("Oratorio:DotCraft:RepositoryWorkspaceRoutes:0:Project", "github:github.com/example-owner/oratorio");
        builder.UseSetting("Oratorio:DotCraft:RepositoryWorkspaceRoutes:0:WorkspacePath", defaultWorkspace);
        builder.UseSetting("Oratorio:GitHub:Token", "test-token");
        builder.UseSetting("Oratorio:GitHub:WritesEnabled", "true");
        builder.UseSetting("Oratorio:GitHub:AppId", "12345");
        builder.UseSetting("Oratorio:GitHub:InstallationProfiles:0:Instance", "github.com");
        builder.UseSetting("Oratorio:GitHub:InstallationProfiles:0:Owner", "example-owner");
        builder.UseSetting("Oratorio:GitHub:InstallationProfiles:0:InstallationId", "98765");
        builder.UseSetting("Oratorio:GitHub:InstallationProfiles:0:Source", "manual");
        builder.UseSetting("Oratorio:GitHub:PrivateKey", "test-private-key");
        builder.UseSetting("Oratorio:GitHub:WebhookSecret", "test-secret");
        builder.UseSetting("Oratorio:GitHub:Repositories:0", "example-owner/oratorio");
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IWorktreeManager>();
            services.AddSingleton<IWorktreeManager, FakeWorktreeManager>();
            services.RemoveAll<IGitDeliveryClient>();
            services.AddSingleton<IGitDeliveryClient, FakeGitDeliveryClient>();
            _configureServices?.Invoke(services);
        });
    }
}

internal sealed class FakeGitHubApiClient : IGitHubApiClient
{
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
    public HashSet<string> FailingIssueRepositories { get; } = new(StringComparer.OrdinalIgnoreCase);
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
    public int FailNextWriteCount { get; set; }
    public int IssueCommentCallCount { get; private set; }
    public int PullRequestReviewCallCount { get; private set; }
    public int PullRequestReviewCommentCallCount { get; private set; }
    public int FullDiffCallCount { get; private set; }
    public int GetPullRequestCallCount { get; private set; }
    public int FailNextGetPullRequestCount { get; set; }
    public bool FailSourceDetailReads { get; set; }
    public bool FailPullRequestFileReads { get; set; }
    public bool FailPullRequestDiffReads { get; set; }

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

        if (FailingIssueRepositories.Contains(repository.FullName))
        {
            throw new HttpRequestException($"Synthetic GitHub list failure for {repository.FullName}.");
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

        return Task.FromResult<IReadOnlyList<GitHubComment>>(number == 184 ? PullRequestIssueComments : IssueSourceComments);
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

        return Task.FromResult<IReadOnlyList<GitHubReviewComment>>(SourceReviewComments);
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
        if (FailPullRequestDiffReads)
        {
            throw new HttpRequestException("Synthetic diff too large.", null, HttpStatusCode.NotAcceptable);
        }

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

    public Task<GitHubWriteResponse> CreateIssueCommentAsync(GitHubRepositoryRef repository, int number, string body, CancellationToken ct)
    {
        MaybeFailWrite();
        IssueComments.Add(new GitHubIssueCommentWrite(repository, number, body));
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
        var index = 0;
        foreach (var review in PullRequestReviews.Where(r => r.Number == number))
        {
            foreach (var comment in review.Comments)
            {
                var threadId = $"thread-{number}-{index++}";
                var bodies = new List<string> { comment.Body };
                bodies.AddRange(ReviewThreadReplies.Where(reply => reply.ThreadId == threadId).Select(reply => reply.Body));
                threads.Add(new GitHubReviewThread(threadId, false, bodies));
            }
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

internal sealed record GitHubPullRequestReviewWrite(GitHubRepositoryRef Repository, int Number, string Event, string Body, string? CommitId, IReadOnlyList<GitHubPullRequestReviewCommentWrite> Comments);

internal sealed record GitHubReviewThreadReplyWrite(GitHubRepositoryRef Repository, string ThreadId, string Body);

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

internal sealed class FakeReviewLocalDiffProvider : IReviewLocalDiffProvider
{
    private readonly IReadOnlyDictionary<string, string> _patches;

    public FakeReviewLocalDiffProvider(IReadOnlyDictionary<string, string>? patches = null)
    {
        _patches = patches ?? new Dictionary<string, string>();
    }

    public List<string> RequestedPaths { get; } = [];

    public Task<string?> GetFilePatchAsync(OratorioRun run, string path, CancellationToken ct)
    {
        RequestedPaths.Add(path);
        return Task.FromResult(_patches.TryGetValue(path, out var patch) ? patch : null);
    }
}

internal sealed class CountingHandler : HttpMessageHandler
{
    public int Count { get; private set; }
    public Uri? LastRequestUri { get; private set; }
    public AuthenticationHeaderValue? LastAuthorization { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Count++;
        LastRequestUri = request.RequestUri;
        LastAuthorization = request.Headers.Authorization;
        var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(new
            {
                token = "installation-token",
                expires_at = DateTimeOffset.UtcNow.AddMinutes(50)
            })
        };
        return Task.FromResult(response);
    }
}

internal sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public string? LastRequestBody { get; private set; }
    public Uri? LastRequestUri { get; private set; }
    public AuthenticationHeaderValue? LastAuthorization { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequestUri = request.RequestUri;
        LastAuthorization = request.Headers.Authorization;
        LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return respond(request);
    }
}

internal sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}

internal sealed class StaticGitHubTokenProvider : IGitHubTokenProvider
{
    public Task<string?> GetBearerTokenAsync(GitHubRepositoryRef repository, CancellationToken ct) => Task.FromResult<string?>("test-token");
}

internal sealed class PassthroughConfigurationSecretProtector : IConfigurationSecretProtector
{
    public bool IsProtected(string? value) => false;

    public string Protect(string value) => value;

    public string? Unprotect(string? value) => value;
}

internal sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

internal sealed class MutableOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    private T _value = value;

    public T CurrentValue => _value;
    public T Get(string? name) => _value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
    public void Set(T value) => _value = value;
}

internal sealed class FixedClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; } = utcNow;
}

internal sealed class MutableClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; private set; } = utcNow;

    public void Advance(TimeSpan duration) => UtcNow = UtcNow.Add(duration);
}

public enum FakeAppServerOutcome
{
    Success,
    DrawerItemPublishFailure,
    Fail,
    Hold,
    TimeoutAfterTurnStarted,
    TimeoutAfterTurnStartedWithoutTerminal,
    SubmitReviewDraft,
    SubmitSyntheticBadAnchorReviewDraft,
    SubmitRetryAnchorReviewDraft,
    SubmitCleanReviewDraft,
    SubmitSummaryOnlyReviewDraft,
    SubmitSummaryOnlyReviewDraftThenFail,
    SubmitSummaryOnlyReviewDraftThenDisconnect,
    SubmitInvalidReviewDraft,
    SubmitMissingSuggestionOldTextReviewDraft,
    SubmitSuggestionTextNotFoundReviewDraft,
    SubmitAmbiguousSuggestionTextReviewDraft,
    SubmitMismatchedSuggestionCountReviewDraft,
    SubmitMultiLineReviewDraft,
    SubmitContextLineReviewDraft,
    SubmitNoOpReviewDraft,
    SubmitImplementationDraft,
    SubmitFollowUpDraft,
    SubmitDiscussionReply,
    Cancel,
    Disconnect
}

internal sealed class FakeDotCraftProcessManager : IDotCraftAppServerProcessManager
{
    private static readonly DotCraftAppServerEndpoint Endpoint = new("ws://127.0.0.1:9100/ws", "test");
    private readonly bool _connected;
    private readonly DotCraftAppServerEndpoint? _endpoint;
    private readonly string _message;
    public List<string> EnsureWorkspacePaths { get; } = [];
    public List<string> ProbeWorkspacePaths { get; } = [];

    public FakeDotCraftProcessManager()
        : this(true, Endpoint, "DotCraft AppServer is reachable.")
    {
    }

    public FakeDotCraftProcessManager(bool connected, DotCraftAppServerEndpoint? endpoint, string? message = null)
    {
        _connected = connected;
        _endpoint = endpoint;
        _message = message ?? (connected ? "DotCraft AppServer is reachable." : "DotCraft AppServer is not reachable.");
    }

    public Task<DotCraftAppServerEndpoint> EnsureAvailableAsync(string workspacePath, CancellationToken ct)
    {
        EnsureWorkspacePaths.Add(workspacePath);
        return Task.FromResult(_endpoint ?? Endpoint);
    }

    public Task<DotCraftAppServerProbeResult> ProbeAsync(string workspacePath, CancellationToken ct)
    {
        ProbeWorkspacePaths.Add(workspacePath);
        return Task.FromResult(new DotCraftAppServerProbeResult(_endpoint, _connected, _connected ? "ok" : "unreachable", _message));
    }
}

internal sealed class FakeWorktreeManager : IWorktreeManager
{
    public List<WorktreePrepareRequest> PrepareRequests { get; } = [];
    public List<WorktreeCleanupRequest> CleanupRequests { get; } = [];
    public WorktreeException? Failure { get; set; }
    public string? BaseShaOverride { get; set; }

    public Task<WorktreePrepareResult> PrepareAsync(WorktreePrepareRequest request, CancellationToken ct)
    {
        PrepareRequests.Add(request);
        if (Failure is not null)
        {
            throw Failure;
        }

        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{request.Source}:{request.ExternalId}:{request.ItemId}")))[..10].ToLowerInvariant();
        var worktreePath = Path.Combine(request.BaseWorkspacePath, ".craft", "oratorio", "worktrees", key);
        var branch = $"oratorio/run/{key}";
        var baseRef = string.IsNullOrWhiteSpace(request.HeadSha)
            ? string.IsNullOrWhiteSpace(request.SourceBranch) ? "HEAD" : request.SourceBranch
            : request.HeadSha;
        return Task.FromResult(new WorktreePrepareResult(
            request.BaseWorkspacePath,
            worktreePath,
            branch,
            baseRef,
            BaseShaOverride ?? (string.IsNullOrWhiteSpace(request.HeadSha) ? "test-base-sha" : request.HeadSha),
            Path.Combine(request.BaseWorkspacePath, ".craft", "oratorio", "worktrees")));
    }

    public Task CleanupAsync(WorktreeCleanupRequest request, CancellationToken ct)
    {
        CleanupRequests.Add(request);
        return Task.CompletedTask;
    }
}

internal sealed class FakeGitDeliveryClient : IGitDeliveryClient
{
    public List<string> ChangedFiles { get; } = ["src/Implementation.cs"];
    public List<string> CommitMessages { get; } = [];
    public List<GitDeliveryPush> Pushes { get; } = [];
    public bool EmptyDiff { get; set; }
    public bool FailPush { get; set; }

    public Task<IReadOnlyList<string>> GetChangedFilesAsync(string worktreePath, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(EmptyDiff ? [] : ChangedFiles);

    public Task<string> CommitAllAsync(string worktreePath, string message, CancellationToken ct)
    {
        CommitMessages.Add(message);
        return Task.FromResult("commit-sha-123");
    }

    public Task PushBranchAsync(string worktreePath, SourceProjectKey project, string branchName, CancellationToken ct)
    {
        if (FailPush)
        {
            throw new InvalidOperationException("Synthetic push failure.");
        }

        Pushes.Add(new GitDeliveryPush(project, branchName));
        return Task.CompletedTask;
    }
}

internal sealed record GitDeliveryPush(SourceProjectKey Project, string BranchName)
{
    public GitHubRepositoryRef Repository
    {
        get
        {
            var parts = Project.ProjectPath.Split('/', 2);
            return new GitHubRepositoryRef(parts[0], parts.Length > 1 ? parts[1] : "");
        }
    }
}

internal sealed record FakeAppServerThreadResumeRequest(
    string ThreadId,
    IReadOnlyList<RuntimeDynamicToolDeclaration>? DynamicTools,
    IReadOnlyDictionary<string, AppServerRuntimeAdditionalContextEntry>? RuntimeAdditionalContext);

internal static class RuntimeDynamicToolTestExtensions
{
    public static bool ContainsTool(
        this IEnumerable<RuntimeDynamicToolDeclaration> declarations,
        string @namespace,
        string localName) =>
        declarations.OfType<RuntimeDynamicToolNamespace>()
            .Any(group =>
                group.Name == @namespace &&
                group.Tools.OfType<RuntimeDynamicToolFunction>().Any(tool => tool.Name == localName));
}

internal sealed class FakeAppServerClientFactory(FakeAppServerOutcome outcome) : IDotCraftAppServerClientFactory
{
    public FakeAppServerOutcome Outcome { get; set; } = outcome;
    public Queue<FakeAppServerOutcome> ScriptedOutcomes { get; } = new();
    public string? FollowUpDraftRepository { get; set; } = "example-owner/oratorio";
    public string? FollowUpDraftBranch { get; set; } = "main";
    public AppServerThreadStartRequest? LastThreadStartRequest { get; private set; }
    public List<AppServerThreadStartRequest> ThreadStartRequests { get; } = [];
    public List<FakeAppServerThreadResumeRequest> ThreadResumeRequests { get; } = [];
    public int ThreadResumeAttemptCount { get; private set; }
    public int StartThreadCount { get; private set; }
    public int ConnectCount { get; private set; }
    public List<string> StartedThreadIds { get; } = [];
    public List<string> TurnThreadIds { get; } = [];
    public List<string> TurnPrompts { get; } = [];
    public List<(string ThreadId, string TurnId)> InterruptedTurns { get; } = [];
    public bool UseMismatchedToolThreadId { get; set; }
    public bool FailThreadResume { get; set; }
    public bool SupportsDynamicToolRebind { get; set; } = true;
    public bool SupportsRuntimeAdditionalContext { get; set; } = true;
    public DynamicToolResult? LastToolResult { get; set; }
    public List<DynamicToolResult> ToolResults { get; } = [];
    private int _turnCount;

    public Task<IDotCraftAppServerClient> ConnectAsync(string appServerUrl, CancellationToken ct, string? token = null)
    {
        ConnectCount++;
        var connectionOutcome = ScriptedOutcomes.Count > 0 ? ScriptedOutcomes.Dequeue() : Outcome;
        return Task.FromResult<IDotCraftAppServerClient>(new FakeAppServerClient(
            connectionOutcome,
            request =>
            {
                LastThreadStartRequest = request;
                ThreadStartRequests.Add(request);
                StartThreadCount++;
                var threadId = $"thread-test-{StartThreadCount}";
                StartedThreadIds.Add(threadId);
                return threadId;
            },
            resumeRequest =>
            {
                ThreadResumeAttemptCount++;
                if (FailThreadResume)
                {
                    throw new InvalidOperationException("Synthetic thread resume rejection.");
                }

                ThreadResumeRequests.Add(resumeRequest);
            },
            threadId =>
            {
                TurnThreadIds.Add(threadId);
                _turnCount++;
                return $"turn-test-{_turnCount}";
            },
            prompt => TurnPrompts.Add(prompt),
            (threadId, turnId) => InterruptedTurns.Add((threadId, turnId)),
            () => SupportsDynamicToolRebind,
            () => SupportsRuntimeAdditionalContext,
            () => UseMismatchedToolThreadId,
            () => FollowUpDraftRepository,
            () => FollowUpDraftBranch,
            result =>
            {
                LastToolResult = result;
                ToolResults.Add(result);
            }));
    }
}

internal sealed class FakeAppServerClient(
    FakeAppServerOutcome outcome,
    Func<AppServerThreadStartRequest, string> startThread,
    Action<FakeAppServerThreadResumeRequest> resumeThread,
    Func<string, string> startTurn,
    Action<string> captureTurnPrompt,
    Action<string, string> interruptTurn,
    Func<bool> supportsDynamicToolRebind,
    Func<bool> supportsRuntimeAdditionalContext,
    Func<bool> useMismatchedToolThreadId,
    Func<string?> getFollowUpDraftRepository,
    Func<string?> getFollowUpDraftBranch,
    Action<DynamicToolResult> captureToolResult) : IDotCraftAppServerClient
{
    private readonly Channel<DotCraftRunEvent> _notifications = Channel.CreateUnbounded<DotCraftRunEvent>();
    private Func<DynamicToolCall, CancellationToken, Task<DynamicToolResult>>? _dynamicToolHandler;
    private FakeAppServerThreadResumeRequest? _lastResumeRequest;

    public bool SupportsDynamicToolRebind => supportsDynamicToolRebind();
    public bool SupportsRuntimeAdditionalContext => supportsRuntimeAdditionalContext();
    public global::DotCraft.Sdk.AppBinding.DotCraftAppBindingClient AppBindings =>
        throw new NotSupportedException("The general AppServer test fake does not exercise App Binding.");

    public void SetDynamicToolHandler(Func<DynamicToolCall, CancellationToken, Task<DynamicToolResult>> handler) =>
        _dynamicToolHandler = handler;

    public Task<string> StartThreadAsync(AppServerThreadStartRequest request, CancellationToken ct)
    {
        var threadId = startThread(request);
        _notifications.Writer.TryWrite(Notification("thread/started", new { threadId }));
        return Task.FromResult(threadId);
    }

    public Task ResumeThreadAsync(
        string threadId,
        IReadOnlyList<RuntimeDynamicToolDeclaration>? dynamicTools,
        IReadOnlyDictionary<string, AppServerRuntimeAdditionalContextEntry>? runtimeAdditionalContext,
        CancellationToken ct)
    {
        _lastResumeRequest = new FakeAppServerThreadResumeRequest(threadId, dynamicTools, runtimeAdditionalContext);
        resumeThread(_lastResumeRequest);
        _notifications.Writer.TryWrite(Notification("thread/resumed", new { threadId }));
        return Task.CompletedTask;
    }

    public Task SubscribeThreadAsync(string threadId, CancellationToken ct) => Task.CompletedTask;

    public Task<string?> StartTurnAsync(string threadId, string prompt, CancellationToken ct)
    {
        captureTurnPrompt(prompt);
        var turnId = startTurn(threadId);
        _notifications.Writer.TryWrite(Notification("turn/started", new { threadId, turnId }));
        if (outcome == FakeAppServerOutcome.Success)
        {
            _notifications.Writer.TryWrite(Notification("item/agentMessage/delta", new { delta = "DotCraft analysis complete. " }));
            _notifications.Writer.TryWrite(Notification("turn/completed", new { threadId, turnId, summary = "DotCraft analysis complete." }));
            _notifications.Writer.TryComplete();
        }
        else if (outcome == FakeAppServerOutcome.DrawerItemPublishFailure)
        {
            _notifications.Writer.TryWrite(DisposedNotification("item/started"));
            _notifications.Writer.TryWrite(Notification("item/agentMessage/delta", new { itemId = "item-after-drawer-failure", threadId, turnId, delta = "DotCraft analysis complete. " }));
            _notifications.Writer.TryWrite(Notification("turn/completed", new { threadId, turnId, summary = "DotCraft analysis complete." }));
            _notifications.Writer.TryComplete();
        }
        else if (outcome is FakeAppServerOutcome.SubmitReviewDraft or FakeAppServerOutcome.SubmitSyntheticBadAnchorReviewDraft or FakeAppServerOutcome.SubmitRetryAnchorReviewDraft or FakeAppServerOutcome.SubmitCleanReviewDraft or FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft or FakeAppServerOutcome.SubmitSummaryOnlyReviewDraftThenFail or FakeAppServerOutcome.SubmitSummaryOnlyReviewDraftThenDisconnect or FakeAppServerOutcome.SubmitInvalidReviewDraft or FakeAppServerOutcome.SubmitMissingSuggestionOldTextReviewDraft or FakeAppServerOutcome.SubmitSuggestionTextNotFoundReviewDraft or FakeAppServerOutcome.SubmitAmbiguousSuggestionTextReviewDraft or FakeAppServerOutcome.SubmitMismatchedSuggestionCountReviewDraft or FakeAppServerOutcome.SubmitMultiLineReviewDraft or FakeAppServerOutcome.SubmitContextLineReviewDraft or FakeAppServerOutcome.SubmitNoOpReviewDraft)
        {
            _ = Task.Run(async () =>
            {
                var isSummaryOnly = outcome is FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft or FakeAppServerOutcome.SubmitSummaryOnlyReviewDraftThenFail or FakeAppServerOutcome.SubmitSummaryOnlyReviewDraftThenDisconnect;
                if (_dynamicToolHandler is not null)
                {
                    if (outcome == FakeAppServerOutcome.SubmitRetryAnchorReviewDraft)
                    {
                        var toolThreadId = useMismatchedToolThreadId() ? "thread-mismatch" : threadId;
                        var firstArguments = JsonSerializer.SerializeToElement(new
                        {
                            summary = new
                            {
                                majorCount = 1,
                                minorCount = 0,
                                suggestionCount = 0,
                                body = "Verbose review summary from DotCraft."
                            },
                            comments = new object[]
                            {
                                new
                                {
                                    severity = "YELLOW",
                                    title = "Synthetic anchor drift",
                                    body = "This finding is initially anchored to a full-file line that is not commentable in the diff.",
                                    path = "src/Example/SyntheticFileSearcher.cs",
                                    kind = "commentOnly",
                                    line = 129,
                                    side = "right",
                                    reason = "requiresLargerChange"
                                }
                            }
                        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                        var firstResult = await _dynamicToolHandler(CreateCall(toolThreadId, turnId, "call-review-1", "oratorio_run", "SubmitReviewDraft", firstArguments), CancellationToken.None);
                        captureToolResult(firstResult);

                        var retryArguments = JsonSerializer.SerializeToElement(new
                        {
                            summary = new
                            {
                                majorCount = 1,
                                minorCount = 0,
                                suggestionCount = 0,
                                body = "Verbose retry review summary from DotCraft."
                            },
                            comments = new object[]
                            {
                                new
                                {
                                    severity = "YELLOW",
                                    title = "Synthetic anchor drift",
                                    body = "This finding was re-anchored to a commentable diff line.",
                                    path = "src/Example/SyntheticFileSearcher.cs",
                                    kind = "commentOnly",
                                    line = 255,
                                    side = "right",
                                    reason = "requiresLargerChange"
                                }
                            }
                        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                        var retryResult = await _dynamicToolHandler(CreateCall(toolThreadId, turnId, "call-review-2", "oratorio_run", "SubmitReviewDraft", retryArguments), CancellationToken.None);
                        captureToolResult(retryResult);
                    }
                    else
                    {
                        object[] comments = outcome switch
                        {
                            FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft or FakeAppServerOutcome.SubmitSummaryOnlyReviewDraftThenFail or FakeAppServerOutcome.SubmitSummaryOnlyReviewDraftThenDisconnect => [],
                            FakeAppServerOutcome.SubmitInvalidReviewDraft => new object[]
                            {
                            new
                            {
                                severity = "YELLOW",
                                title = "Missing contract field",
                                body = "This inline finding intentionally lacks both suggestion and commentOnly.",
                                path = "src/Auth/JwtMiddleware.cs"
                            }
                            },
                            FakeAppServerOutcome.SubmitMissingSuggestionOldTextReviewDraft => new object[]
                            {
                            new
                            {
                                severity = "RED",
                                title = "Missing suggestion old text",
                                body = "This payload intentionally omits oldText for kind suggestion.",
                                path = "src/Auth/RefreshTokenStore.cs",
                                kind = "suggestion",
                                newText = "        return token;"
                            }
                            },
                            FakeAppServerOutcome.SubmitSuggestionTextNotFoundReviewDraft => new object[]
                            {
                            new
                            {
                                severity = "RED",
                                title = "Missing suggestion text",
                                body = "This suggestion intentionally references text that is not in the right-side diff.",
                                path = "src/Auth/RefreshTokenStore.cs",
                                kind = "suggestion",
                                oldText = "        return missing;",
                                newText = "        return token;"
                            }
                            },
                            FakeAppServerOutcome.SubmitAmbiguousSuggestionTextReviewDraft => new object[]
                            {
                            new
                            {
                                severity = "RED",
                                title = "Ambiguous suggestion text",
                                body = "This suggestion intentionally references a repeated right-side snippet.",
                                path = "src/Auth/RefreshTokenStore.cs",
                                kind = "suggestion",
                                oldText = "        return refreshed;",
                                newText = "        return token;"
                            }
                            },
                            FakeAppServerOutcome.SubmitMultiLineReviewDraft => new object[]
                            {
                            new
                            {
                                severity = "RED",
                                title = "Refresh order is reversed",
                                body = "The refreshed value should be returned before falling back to the original token.",
                                path = "src/Auth/RefreshTokenStore.cs",
                                kind = "suggestion",
                                oldText = "        return token;\n        return refreshed;",
                                newText = "        return refreshed;\n        return token;"
                            }
                            },
                            FakeAppServerOutcome.SubmitContextLineReviewDraft => new object[]
                            {
                            new
                            {
                                severity = "RED",
                                title = "Context line needs explicit handling",
                                body = "The replacement targets an unchanged diff context line.",
                                path = "src/Auth/RefreshTokenStore.cs",
                                kind = "suggestion",
                                oldText = "line89",
                                newText = "line89 fixed"
                            }
                            },
                            FakeAppServerOutcome.SubmitNoOpReviewDraft => new object[]
                            {
                            new
                            {
                                severity = "YELLOW",
                                title = "No-op replacement",
                                body = "This suggestion intentionally matches the current diff text.",
                                path = "src/Auth/RefreshTokenStore.cs",
                                kind = "suggestion",
                                oldText = "        return refreshed;",
                                newText = "        return refreshed;"
                            }
                            },
                            FakeAppServerOutcome.SubmitSyntheticBadAnchorReviewDraft => new object[]
                            {
                            new
                            {
                                severity = "YELLOW",
                                title = "Synthetic anchor drift",
                                body = "This finding is anchored to a full-file line that is not commentable in the diff.",
                                path = "src/Example/SyntheticFileSearcher.cs",
                                kind = "commentOnly",
                                line = 129,
                                side = "right",
                                reason = "requiresLargerChange"
                            }
                            },
                            FakeAppServerOutcome.SubmitCleanReviewDraft or FakeAppServerOutcome.SubmitMismatchedSuggestionCountReviewDraft => new object[]
                            {
                            new
                            {
                                severity = "RED",
                                title = "Missing refresh guard",
                                body = "The refresh path can return the stale token.",
                                path = "src/Auth/RefreshTokenStore.cs",
                                kind = "suggestion",
                                oldText = "        return refreshed;",
                                newText = "        return token;"
                            },
                            new
                            {
                                severity = "YELLOW",
                                title = "Validate middleware setup",
                                body = "The middleware should validate before invoking the next step.",
                                path = "src/Auth/JwtMiddleware.cs",
                                kind = "commentOnly",
                                line = 22,
                                side = "right",
                                reason = "needsHumanDecision"
                            }
                            },
                            _ => new object[]
                            {
                            new
                            {
                                severity = "RED",
                                title = "Missing refresh guard",
                                body = "The refresh path can return the stale token.",
                                path = "src/Auth/RefreshTokenStore.cs",
                                kind = "suggestion",
                                oldText = "        return refreshed;",
                                newText = "        return token;"
                            },
                            new
                            {
                                severity = "YELLOW",
                                title = "Validate middleware setup",
                                body = "The middleware should validate before invoking the next step.",
                                path = "src/Auth/JwtMiddleware.cs",
                                kind = "commentOnly",
                                line = 22,
                                side = "right",
                                reason = "needsHumanDecision"
                            },
                            new
                            {
                                severity = "YELLOW",
                                title = "Missing file",
                                body = "This path is not in the PR diff.",
                                path = "missing/File.cs",
                                kind = "commentOnly",
                                line = 5,
                                side = "right",
                                reason = "cannotAnchorSafely"
                            },
                            new
                            {
                                severity = "RED",
                                title = "Bad comment anchor",
                                body = "This line is not commentable on the right side.",
                                path = "src/Auth/RefreshTokenStore.cs",
                                kind = "commentOnly",
                                line = 999,
                                side = "right",
                                reason = "cannotAnchorSafely"
                            }
                            }
                        };
                        var arguments = JsonSerializer.SerializeToElement(new
                        {
                            summary = new
                            {
                                majorCount = isSummaryOnly ? 0 : 1,
                                minorCount = isSummaryOnly ? 0 : 1,
                                suggestionCount = outcome switch
                                {
                                    FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft or FakeAppServerOutcome.SubmitSummaryOnlyReviewDraftThenFail or FakeAppServerOutcome.SubmitSummaryOnlyReviewDraftThenDisconnect => 0,
                                    FakeAppServerOutcome.SubmitMismatchedSuggestionCountReviewDraft => 4,
                                    _ => 1
                                },
                                body = isSummaryOnly
                                    ? "Verbose clean review summary from DotCraft."
                                    : "Verbose review summary from DotCraft."
                            },
                            comments
                        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                        var toolThreadId = useMismatchedToolThreadId() ? "thread-mismatch" : threadId;
                        var result = await _dynamicToolHandler(CreateCall(toolThreadId, turnId, "call-review-1", "oratorio_run", "SubmitReviewDraft", arguments), CancellationToken.None);
                        captureToolResult(result);
                    }
                }

                var completion = isSummaryOnly
                    ? "DotCraft analysis complete."
                    : "DotCraft review draft submitted.";
                _notifications.Writer.TryWrite(Notification("item/agentMessage/delta", new { delta = completion + " " }));
                if (outcome == FakeAppServerOutcome.SubmitSummaryOnlyReviewDraftThenFail)
                {
                    _notifications.Writer.TryWrite(Notification("turn/failed", new { threadId, turnId, error = new { message = "Synthetic failure after draft submission." } }));
                }
                else if (outcome != FakeAppServerOutcome.SubmitSummaryOnlyReviewDraftThenDisconnect)
                {
                    _notifications.Writer.TryWrite(Notification("turn/completed", new { threadId, turnId, summary = completion }));
                }
                _notifications.Writer.TryComplete();
            });
        }
        else if (outcome == FakeAppServerOutcome.SubmitImplementationDraft)
        {
            _ = Task.Run(async () =>
            {
                if (_dynamicToolHandler is not null)
                {
                    var arguments = JsonSerializer.SerializeToElement(new
                    {
                        summary = "Implemented the requested issue workflow.",
                        tests = new[] { "dotnet test passed" },
                        risks = Array.Empty<string>(),
                        changedFiles = new[] { "src/Implementation.cs" },
                        proposedCommitMessage = "Implement issue workflow",
                        proposedPrTitle = "Implement issue workflow",
                        proposedPrBody = "Implements the requested workflow."
                    }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    var toolThreadId = useMismatchedToolThreadId() ? "thread-mismatch" : threadId;
                    var result = await _dynamicToolHandler(CreateCall(toolThreadId, turnId, "call-implementation-1", "oratorio_run", "SubmitImplementationDraft", arguments), CancellationToken.None);
                    captureToolResult(result);
                }

                _notifications.Writer.TryWrite(Notification("item/agentMessage/delta", new { delta = "DotCraft implementation draft submitted. " }));
                _notifications.Writer.TryWrite(Notification("turn/completed", new { threadId, turnId, summary = "DotCraft implementation draft submitted." }));
                _notifications.Writer.TryComplete();
            });
        }
        else if (outcome == FakeAppServerOutcome.SubmitFollowUpDraft)
        {
            _ = Task.Run(async () =>
            {
                if (_dynamicToolHandler is not null)
                {
                    var reviewArguments = JsonSerializer.SerializeToElement(new
                    {
                        summary = new
                        {
                            majorCount = 0,
                            minorCount = 0,
                            suggestionCount = 0,
                            body = "Verbose clean review summary from DotCraft."
                        },
                        comments = Array.Empty<object>()
                    }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    var toolThreadId = useMismatchedToolThreadId() ? "thread-mismatch" : threadId;
                    var reviewResult = await _dynamicToolHandler(CreateCall(toolThreadId, turnId, "call-review-1", "oratorio_run", "SubmitReviewDraft", reviewArguments), CancellationToken.None);
                    captureToolResult(reviewResult);

                    var arguments = JsonSerializer.SerializeToElement(new
                    {
                        proposals = new object[]
                        {
                            new
                            {
                                title = "Split out migration cleanup",
                                body = "The migration cleanup is useful but should be handled separately from this review.",
                                rationale = "It touches a different risk area.",
                                repository = getFollowUpDraftRepository(),
                                assignee = "maintainer",
                                branch = getFollowUpDraftBranch(),
                                labels = new[] { "follow-up", "backend" }
                            }
                        }
                    }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    var result = await _dynamicToolHandler(CreateCall(toolThreadId, turnId, "call-follow-up-1", "oratorio_run", "SubmitFollowUpDraft", arguments), CancellationToken.None);
                    captureToolResult(result);
                }

                _notifications.Writer.TryWrite(Notification("item/agentMessage/delta", new { delta = "DotCraft follow-up draft submitted. " }));
                _notifications.Writer.TryWrite(Notification("turn/completed", new { threadId, turnId, summary = "DotCraft follow-up draft submitted." }));
                _notifications.Writer.TryComplete();
            });
        }
        else if (outcome == FakeAppServerOutcome.SubmitDiscussionReply)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(50);
                if (_dynamicToolHandler is not null)
                {
                    var arguments = JsonSerializer.SerializeToElement(new
                    {
                        discussionTurnId = ExtractDiscussionTurnId(prompt, _lastResumeRequest?.RuntimeAdditionalContext),
                        body = "Agent answer from DotCraft."
                    }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    var toolThreadId = useMismatchedToolThreadId() ? "thread-mismatch" : threadId;
                    var result = await _dynamicToolHandler(CreateCall(toolThreadId, turnId, "call-discussion-1", "oratorio_run", "SubmitDiscussionReply", arguments), CancellationToken.None);
                    captureToolResult(result);
                }

                _notifications.Writer.TryWrite(Notification("item/agentMessage/delta", new { delta = "DotCraft discussion reply submitted. " }));
                _notifications.Writer.TryWrite(Notification("turn/completed", new { threadId, turnId, summary = "DotCraft discussion reply submitted." }));
                _notifications.Writer.TryComplete();
            });
        }
        else if (outcome == FakeAppServerOutcome.Fail)
        {
            _notifications.Writer.TryWrite(Notification("turn/failed", new { threadId, turnId, error = new { message = "Synthetic DotCraft failure." } }));
            _notifications.Writer.TryComplete();
        }
        else if (outcome == FakeAppServerOutcome.Cancel)
        {
            _notifications.Writer.TryWrite(Notification("turn/cancelled", new { threadId, turnId }));
            _notifications.Writer.TryComplete();
        }
        else if (outcome == FakeAppServerOutcome.Disconnect)
        {
            _notifications.Writer.TryComplete();
        }

        return Task.FromResult<string?>(turnId);
    }

    public Task<string?> StartTurnAsync(string threadId, IReadOnlyList<TurnInputPartDto> input, string? modelId, CancellationToken ct) =>
        StartTurnAsync(threadId, input.FirstOrDefault()?.Text ?? "", ct);

    public Task<string?> EnqueueTurnAsync(string threadId, IReadOnlyList<TurnInputPartDto> input, CancellationToken ct) =>
        Task.FromResult<string?>("queued-test-1");

    public Task InterruptTurnAsync(string threadId, string turnId, CancellationToken ct)
    {
        interruptTurn(threadId, turnId);
        if (outcome != FakeAppServerOutcome.TimeoutAfterTurnStartedWithoutTerminal)
        {
            _notifications.Writer.TryWrite(Notification("turn/cancelled", new { threadId, turnId }));
        }

        return Task.CompletedTask;
    }

    public Task<AppServerThreadReadResult> ReadThreadAsync(string threadId, CancellationToken ct) =>
        Task.FromResult(new AppServerThreadReadResult(threadId, []));

    public Task<IReadOnlyList<ModelInfoDto>> ListModelsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ModelInfoDto>>([new("fake-model", "Fake Model", "test")]);

    public IAsyncEnumerable<DotCraftRunEvent> ReadEventsAsync(CancellationToken ct) =>
        outcome is FakeAppServerOutcome.TimeoutAfterTurnStarted or FakeAppServerOutcome.TimeoutAfterTurnStartedWithoutTerminal
            ? ReadEventsThenCancelAsync(ct)
            : _notifications.Reader.ReadAllAsync(ct);

    public ValueTask DisposeAsync()
    {
        if (outcome == FakeAppServerOutcome.Hold)
        {
            _notifications.Writer.TryComplete();
        }

        return ValueTask.CompletedTask;
    }

    private static DotCraftRunEvent Notification(string method, object parameters)
    {
        var json = JsonSerializer.SerializeToElement(parameters, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var raw = new AppServerNotification(method, json);
        var threadId = String(json, "threadId") ?? "";
        var turnId = String(json, "turnId");
        if (method.StartsWith("turn/", StringComparison.Ordinal))
        {
            var turn = new SessionTurn
            {
                Id = turnId ?? "turn-test",
                ThreadId = threadId,
                Status = method["turn/".Length..],
                StartedAt = DateTimeOffset.UnixEpoch,
                Error = Error(json)
            };
            var value = new TurnNotification { Turn = turn, Error = Error(json) };
            return new DotCraftRunEvent<TurnNotification>(method, threadId, turn.Id, value, raw);
        }

        if (method.Contains("/delta", StringComparison.OrdinalIgnoreCase)
            || method.EndsWith("Delta", StringComparison.OrdinalIgnoreCase))
        {
            var value = new ItemDeltaNotification
            {
                ThreadId = threadId,
                TurnId = turnId,
                ItemId = String(json, "itemId"),
                DeltaKind = String(json, "deltaKind"),
                Delta = String(json, "delta") ?? ""
            };
            return new DotCraftRunEvent<ItemDeltaNotification>(method, threadId, turnId, value, raw);
        }

        return new DotCraftRunEvent(method, threadId, turnId, raw);
    }

    private static DynamicToolCall CreateCall(
        string threadId,
        string turnId,
        string callId,
        string? toolNamespace,
        string tool,
        JsonElement arguments) =>
        new()
        {
            ThreadId = threadId,
            TurnId = turnId,
            CallId = callId,
            Namespace = toolNamespace,
            Tool = tool,
            Arguments = arguments
        };

    private async IAsyncEnumerable<DotCraftRunEvent> ReadEventsThenCancelAsync([EnumeratorCancellation] CancellationToken ct)
    {
        while (_notifications.Reader.TryRead(out var notification))
        {
            yield return notification;

            if (notification.Type is "turn/completed" or "turn/failed" or "turn/cancelled")
            {
                yield break;
            }
        }

        await Task.Yield();
        throw new OperationCanceledException(ct);
    }

    private static string ExtractDiscussionTurnId(
        string prompt,
        IReadOnlyDictionary<string, AppServerRuntimeAdditionalContextEntry>? runtimeAdditionalContext)
    {
        const string marker = "discussionTurnId `";
        var context = runtimeAdditionalContext is not null &&
            runtimeAdditionalContext.TryGetValue("oratorio.discussionTurn", out var entry)
            ? entry.Value
            : null;
        var sources = string.IsNullOrWhiteSpace(context)
            ? new[] { prompt }
            : new[] { context!, prompt };
        foreach (var source in sources)
        {
            var start = source.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
            {
                continue;
            }

            start += marker.Length;
            var end = source.IndexOf('`', start);
            return end > start ? source[start..end] : "missing-discussion-turn";
        }

        return "missing-discussion-turn";
    }

    private static DotCraftRunEvent DisposedNotification(string method)
    {
        var document = JsonDocument.Parse("""{"text":"unavailable"}""");
        var payload = document.RootElement;
        document.Dispose();
        var raw = new AppServerNotification(method, JsonSerializer.SerializeToElement(new { }));
        var parameters = new ItemNotification
        {
            ThreadId = "thread-disposed",
            TurnId = "turn-disposed",
            Item = new SessionItem
            {
                Id = "item-disposed",
                TurnId = "turn-disposed",
                Type = "agentMessage",
                Status = "completed",
                CreatedAt = DateTimeOffset.UnixEpoch,
                PayloadKind = "agentMessage",
                Payload = payload
            }
        };
        return new DotCraftRunEvent<ItemNotification>(method, parameters.ThreadId, parameters.TurnId, parameters, raw);
    }

    private static string? String(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string? Error(JsonElement value)
    {
        if (String(value, "error") is { } direct)
            return direct;
        if (value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty("error", out var error)
            && error.ValueKind == JsonValueKind.Object)
            return String(error, "message");
        return null;
    }
}

file static class TestHelpers
{
    public static string Signature(string body, string secret) =>
        "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    public static string ExportPrivateKeyPem(RSA rsa)
    {
        var builder = new StringBuilder();
        builder.AppendLine("-----BEGIN PRIVATE KEY-----");
        builder.AppendLine(Convert.ToBase64String(rsa.ExportPkcs8PrivateKey(), Base64FormattingOptions.InsertLineBreaks));
        builder.AppendLine("-----END PRIVATE KEY-----");
        return builder.ToString();
    }
}
