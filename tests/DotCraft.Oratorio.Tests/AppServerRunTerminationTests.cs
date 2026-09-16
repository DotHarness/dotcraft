using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.Integrations;
using DotCraft.Oratorio.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotCraft.Oratorio.Tests;

public sealed class AppServerRunTerminationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public async Task StalledRunInterruptsBeforeRetryStarts()
    {
        var clock = new MutableClock(DateTimeOffset.Parse("2026-09-16T09:00:00Z"));
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.Hold);
        fakeAppServer.ScriptedOutcomes.Enqueue(FakeAppServerOutcome.Hold);
        fakeAppServer.ScriptedOutcomes.Enqueue(FakeAppServerOutcome.Success);
        await using var app = CreateApp(fakeAppServer, clock, maxRunAttempts: 2, stallTimeoutSeconds: 5);
        var client = app.CreateClient();

        await CreateItemAsync(client, "task:test-appserver-stalled-interrupt");
        await PostAsync<ItemDetailResponse>(
            client,
            "/api/v1/items/local/task:test-appserver-stalled-interrupt/dispatch",
            new DispatchRequest("appServer", "Interrupt a stalled DotCraft run before retrying.", null, null));

        await WaitForItemAsync(client, "task:test-appserver-stalled-interrupt", x => x.Item.State == ItemState.Running);
        clock.Advance(TimeSpan.FromSeconds(6));
        await WaitForItemAsync(client, "task:test-appserver-stalled-interrupt", x => x.Runs.Count == 2);
        clock.Advance(TimeSpan.FromSeconds(2));

        var completed = await WaitForItemAsync(
            client,
            "task:test-appserver-stalled-interrupt",
            x => x.Item.State == ItemState.AwaitingReview && x.Runs.Count == 2);
        var runs = completed.Runs.OrderBy(x => x.Attempt).ToArray();
        Assert.Equal(RunStatus.TimedOut, runs[0].Status);
        Assert.Equal("appServerStalled", runs[0].ErrorCode);
        Assert.Equal(RunStatus.Succeeded, runs[1].Status);
        Assert.Contains(fakeAppServer.InterruptedTurns, x => x.ThreadId == "thread-test-1" && x.TurnId == "turn-test-1");
    }

    [Fact]
    public async Task StalledRunWithoutTerminalConfirmationDoesNotRetry()
    {
        var clock = new MutableClock(DateTimeOffset.Parse("2026-09-16T09:00:00Z"));
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.HoldInterruptWithoutTerminal);
        await using var app = CreateApp(fakeAppServer, clock, maxRunAttempts: 2, stallTimeoutSeconds: 5);
        var client = app.CreateClient();

        await CreateItemAsync(client, "task:test-appserver-stalled-unconfirmed");
        await PostAsync<ItemDetailResponse>(
            client,
            "/api/v1/items/local/task:test-appserver-stalled-unconfirmed/dispatch",
            new DispatchRequest("appServer", "Do not retry an unconfirmed stalled turn.", null, null));

        await WaitForItemAsync(client, "task:test-appserver-stalled-unconfirmed", x => x.Item.State == ItemState.Running);
        clock.Advance(TimeSpan.FromSeconds(6));

        var failed = await WaitForItemAsync(client, "task:test-appserver-stalled-unconfirmed", x => x.Item.State == ItemState.Failed);
        var run = Assert.Single(failed.Runs);
        Assert.Equal(RunStatus.TimedOut, run.Status);
        Assert.Equal("appServerTerminationUnconfirmed", run.ErrorCode);
        Assert.Single(fakeAppServer.TurnPrompts);
        Assert.Single(fakeAppServer.InterruptedTurns);
    }

    [Fact]
    public async Task RestartRecoveryInterruptsPersistedTurnBeforeRetry()
    {
        var databasePath = NewDatabasePath();
        var originalAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.Hold);
        await using (var originalApp = CreateApp(originalAppServer, maxRunAttempts: 2, databasePath: databasePath))
        {
            var originalClient = originalApp.CreateClient();
            await DispatchHeldRunAsync(originalClient, "task:test-appserver-restart-recovery");
        }

        var recoveryAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.Success);
        await using var recoveredApp = CreateApp(recoveryAppServer, maxRunAttempts: 2, databasePath: databasePath);
        var recoveredClient = recoveredApp.CreateClient();

        var completed = await WaitForItemAsync(
            recoveredClient,
            "task:test-appserver-restart-recovery",
            x => x.Item.State == ItemState.AwaitingReview && x.Runs.Count == 2);
        var runs = completed.Runs.OrderBy(x => x.Attempt).ToArray();
        Assert.Equal("appServerRunnerInterrupted", runs[0].ErrorCode);
        Assert.Equal(RunStatus.Succeeded, runs[1].Status);
        Assert.Contains(recoveryAppServer.InterruptedTurns, x => x.ThreadId == "thread-test-1" && x.TurnId == "turn-test-1");
    }

    [Fact]
    public async Task RestartRecoveryWithoutTerminalConfirmationDoesNotRetry()
    {
        var databasePath = NewDatabasePath();
        var originalAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.Hold);
        await using (var originalApp = CreateApp(originalAppServer, maxRunAttempts: 2, databasePath: databasePath))
        {
            var originalClient = originalApp.CreateClient();
            await DispatchHeldRunAsync(originalClient, "task:test-appserver-restart-unconfirmed");
        }

        var recoveryAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.HoldInterruptWithoutTerminal);
        await using var recoveredApp = CreateApp(recoveryAppServer, maxRunAttempts: 2, databasePath: databasePath);
        var recoveredClient = recoveredApp.CreateClient();

        var failed = await WaitForItemAsync(
            recoveredClient,
            "task:test-appserver-restart-unconfirmed",
            x => x.Item.State == ItemState.Failed);
        var run = Assert.Single(failed.Runs);
        Assert.Equal("appServerTerminationUnconfirmed", run.ErrorCode);
        Assert.Empty(recoveryAppServer.TurnPrompts);
        Assert.Single(recoveryAppServer.InterruptedTurns);
    }

    private static TestOratorioApp CreateApp(
        FakeAppServerClientFactory fakeAppServer,
        MutableClock? clock = null,
        int maxRunAttempts = 1,
        int? stallTimeoutSeconds = null,
        string? databasePath = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Oratorio:DotCraft:MaxRunAttempts"] = maxRunAttempts.ToString()
        };
        if (stallTimeoutSeconds is not null)
        {
            settings["Oratorio:DotCraft:StallTimeoutSeconds"] = stallTimeoutSeconds.Value.ToString();
        }

        return new TestOratorioApp(
            services =>
            {
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
                if (clock is not null)
                {
                    services.RemoveAll<IClock>();
                    services.AddSingleton<IClock>(clock);
                }
            },
            settings,
            databasePath);
    }

    private static async Task DispatchHeldRunAsync(HttpClient client, string externalId)
    {
        await CreateItemAsync(client, externalId);
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/local/{externalId}/dispatch",
            new DispatchRequest("appServer", "Recover this run after an Oratorio restart.", null, null));
        await WaitForItemAsync(client, externalId, x => x.Item.State == ItemState.Running);
    }

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
        response.EnsureSuccessStatusCode();
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string path, object request)
    {
        var response = await client.PostAsJsonAsync(path, request, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions)
            ?? throw new InvalidOperationException($"Expected {typeof(T).Name} response.");
    }

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

    private static string NewDatabasePath() =>
        Path.Combine(Path.GetTempPath(), "oratorio-tests", $"{Guid.NewGuid():n}.db");
}
