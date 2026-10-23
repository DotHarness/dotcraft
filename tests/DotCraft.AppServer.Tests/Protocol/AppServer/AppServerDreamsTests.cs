using System.Text.Json;
using DotCraft.AppServer;
using DotCraft.Configuration;
using DotCraft.Dreams;
using DotCraft.Tests.Dreams;
using Xunit;
using Methods = DotCraft.Protocol.AppServer.AppServerMethodNames;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerDreamsTests : IDisposable
{
    private readonly DreamsTestWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task DreamsStatus_ReportsCapabilityConfigurationAndNoRunBeforeTheFirst()
    {
        _workspace.Config.Dreams.Interval = TimeSpan.FromHours(12);
        _workspace.Config.Dreams.AutoApply = true;
        _workspace.Config.Dreams.MinSessionsSinceLastRun = 4;
        await using var service = _workspace.CreateService(new FakeDreamsRunner());
        using var harness = CreateHarness(service);
        using (var init = await harness.InitializeAsync())
            Assert.True(init.RootElement.GetProperty("result").GetProperty("capabilities").GetProperty("dreams").GetBoolean());

        var result = await RequestAsync(harness, Methods.DreamsStatus, new { });

        Assert.True(result.GetProperty("enabled").GetBoolean());
        Assert.Equal("12:00:00", result.GetProperty("interval").GetString());
        Assert.True(result.GetProperty("autoApply").GetBoolean());
        Assert.Equal(4, result.GetProperty("minSessionsSinceLastRun").GetInt32());
        Assert.False(result.GetProperty("running").GetBoolean());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("lastRun").ValueKind);
        Assert.False(result.TryGetProperty("threadLookbackCount", out _));

        await harness.ExecuteRequestAsync(harness.BuildRequest(Methods.DreamsCreate, new { scope = "nightly-report" }));
        AppServerTestHarness.AssertIsErrorResponse(await harness.Transport.ReadNextSentAsync(), AppServerErrors.InvalidParamsCode);
    }

    [Fact]
    public async Task DreamsRun_StartsOneBackgroundRunAndDoesNotDuplicateTheActiveRun()
    {
        _workspace.WriteMemory("deploy", "Deploy on Fridays.");
        var runner = new FakeDreamsRunner { BlockUntilReleased = true };
        await using var service = _workspace.CreateService(runner);
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();

        var first = await RequestAsync(harness, Methods.DreamsRun, new { });
        await runner.WaitUntilCalledAsync();
        var second = await RequestAsync(harness, Methods.DreamsRun, new { });

        Assert.True(first.GetProperty("running").GetBoolean());
        Assert.True(second.GetProperty("running").GetBoolean());
        Assert.Equal(DreamsConstants.TriggerManual, first.GetProperty("lastRun").GetProperty("trigger").GetString());
        Assert.Single(runner.Inputs);

        runner.Release();
        await _workspace.WaitForRunAsync(static run => run.Status == DreamsRunStatuses.Succeeded);
        var lastRun = (await RequestAsync(harness, Methods.DreamsStatus, new { })).GetProperty("lastRun");
        Assert.Equal("thread_dream_fake", lastRun.GetProperty("threadId").GetString());
        Assert.Equal("turn_dream_fake", lastRun.GetProperty("turnId").GetString());
        Assert.Equal(2, lastRun.GetProperty("evidenceSearchCount").GetInt32());
        Assert.Equal(1, lastRun.GetProperty("evidenceReadCount").GetInt32());
        Assert.Equal("thread_one", lastRun.GetProperty("evidenceThreadIds")[0].GetString());
        Assert.Equal("Consolidated the store.", lastRun.GetProperty("summary").GetString());
        Assert.Equal(DreamsReviewStatuses.Applied, lastRun.GetProperty("reviewStatus").GetString());
        Assert.False(lastRun.TryGetProperty("scope", out _));
    }

    [Fact]
    public async Task DreamsRun_WhenDisabled_ReturnsStatusWithoutStartingARun()
    {
        _workspace.Config.Dreams.Enabled = false;
        var runner = new FakeDreamsRunner();
        await using var service = _workspace.CreateService(runner);
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();

        var result = await RequestAsync(harness, Methods.DreamsRun, new { });

        Assert.False(result.GetProperty("running").GetBoolean());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("lastRun").ValueKind);
        Assert.Empty(runner.Inputs);
    }

    [Fact]
    public async Task DreamsLifecycle_CreateGetApplyAndArchive()
    {
        _workspace.WriteMemory("deploy", "Deploy on Fridays.");
        await _workspace.SaveThreadAsync("thread_one");
        var runner = new FakeDreamsRunner
        {
            Edit = input => File.WriteAllText(Path.Combine(input.CandidateDirectory, "deploy.md"), "Deploy on Thursdays.")
        };
        var monitor = new AppConfigMonitor(_workspace.Config);
        var changes = new List<AppConfigChangedEventArgs>();
        monitor.Changed += (_, args) => changes.Add(args);
        await using var service = _workspace.CreateService(runner, monitor);
        using var harness = CreateHarness(service, monitor);
        await harness.InitializeAsync();

        var created = await RequestAsync(harness, Methods.DreamsCreate, new
        {
            threadIds = new[] { "thread_one" },
            instructions = "Focus on deploys."
        });
        var runId = created.GetProperty("run").GetProperty("id").GetString()!;
        Assert.Equal(DreamsRunStatuses.Running, created.GetProperty("run").GetProperty("status").GetString());
        await _workspace.WaitForRunAsync(run => run.Id == runId && !run.IsRunning);

        var details = await RequestAsync(harness, Methods.DreamsGet, new { runId });
        var run = details.GetProperty("run");
        Assert.Equal(DreamsReviewStatuses.Pending, run.GetProperty("reviewStatus").GetString());
        Assert.Equal("deploy.md", run.GetProperty("changes").GetProperty("updated")[0].GetString());
        Assert.False(run.GetProperty("changes").GetProperty("indexChanged").GetBoolean());
        var file = Assert.Single(details.GetProperty("files").EnumerateArray());
        Assert.Equal("deploy.md", file.GetProperty("name").GetString());
        Assert.Equal("updated", file.GetProperty("change").GetString());
        Assert.Contains("Deploy on Fridays.", file.GetProperty("baseline").GetString(), StringComparison.Ordinal);
        Assert.Equal("Deploy on Thursdays.", file.GetProperty("candidate").GetString());

        var listed = await RequestAsync(harness, Methods.DreamsList, new { });
        Assert.Contains(listed.GetProperty("runs").EnumerateArray(), item => item.GetProperty("id").GetString() == runId);

        var applied = await RequestAsync(harness, Methods.DreamsApply, new { runId });
        Assert.Equal(DreamsReviewStatuses.Applied, applied.GetProperty("run").GetProperty("reviewStatus").GetString());
        Assert.Equal("Deploy on Thursdays.", await File.ReadAllTextAsync(Path.Combine(_workspace.WorkspaceStore, "deploy.md")));
        Assert.Contains(changes, change => change.Regions.Contains(ConfigChangeRegions.Memory));

        var archived = await RequestAsync(harness, Methods.DreamsArchive, new { runId });
        Assert.Equal(DreamsReviewStatuses.Archived, archived.GetProperty("run").GetProperty("reviewStatus").GetString());
        Assert.Empty((await RequestAsync(harness, Methods.DreamsList, new { })).GetProperty("runs").EnumerateArray());
        Assert.Single((await RequestAsync(harness, Methods.DreamsList, new { includeArchived = true })).GetProperty("runs").EnumerateArray());
    }

    [Fact]
    public async Task DreamsApply_WhenTheStoreChanged_ReturnsStoreChangedAndKeepsTheRunPending()
    {
        _workspace.WriteMemory("deploy", "Deploy on Fridays.");
        var runner = new FakeDreamsRunner
        {
            Edit = input => File.WriteAllText(Path.Combine(input.CandidateDirectory, "deploy.md"), "Deploy on Thursdays.")
        };
        await using var service = _workspace.CreateService(runner);
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();
        var runId = (await RequestAsync(harness, Methods.DreamsCreate, new { })).GetProperty("run").GetProperty("id").GetString()!;
        await _workspace.WaitForRunAsync(run => run.Id == runId && !run.IsRunning);
        File.WriteAllText(Path.Combine(_workspace.WorkspaceStore, "deploy.md"), "Edited by the user.");

        await harness.ExecuteRequestAsync(harness.BuildRequest(Methods.DreamsApply, new { runId }));
        var response = await harness.Transport.ReadNextSentAsync();

        AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.DreamsStoreChangedCode);
        Assert.Equal("storeChanged", response.RootElement.GetProperty("error").GetProperty("data").GetProperty("code").GetString());
        var run = (await RequestAsync(harness, Methods.DreamsGet, new { runId })).GetProperty("run");
        Assert.Equal(DreamsReviewStatuses.Pending, run.GetProperty("reviewStatus").GetString());
        Assert.Equal("storeChanged", run.GetProperty("errorType").GetString());
        Assert.Equal("Edited by the user.", await File.ReadAllTextAsync(Path.Combine(_workspace.WorkspaceStore, "deploy.md")));
    }

    [Fact]
    public async Task ConfigBatchWrite_DreamsFields_UpdateTheMonitor()
    {
        await File.WriteAllTextAsync(Path.Combine(_workspace.CraftPath, "config.json"), """{ "Dreams": { "Enabled": true } }""");
        var monitor = new AppConfigMonitor(_workspace.Config);
        await using var service = _workspace.CreateService(new FakeDreamsRunner());
        using var harness = CreateHarness(service, monitor);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            Methods.ConfigBatchWrite,
            System.Text.Json.Nodes.JsonNode.Parse(
                """
                {
                  "edits": [
                    { "keyPath": "Dreams.Interval", "value": "12:00:00", "mergeStrategy": "replace" },
                    { "keyPath": "Dreams.MinSessionsSinceLastRun", "value": 5, "mergeStrategy": "replace" },
                    { "keyPath": "Dreams.AutoApply", "value": true, "mergeStrategy": "replace" }
                  ]
                }
                """)));

        AppServerTestHarness.AssertIsSuccessResponse(await harness.Transport.ReadNextSentAsync());
        Assert.Equal(TimeSpan.FromHours(12), monitor.Current.Dreams.Interval);
        Assert.Equal(5, monitor.Current.Dreams.MinSessionsSinceLastRun);
        Assert.True(monitor.Current.Dreams.AutoApply);

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            Methods.ConfigValueWrite,
            new { keyPath = "Dreams.MinSessionsSinceLastRun", value = 0, mergeStrategy = "replace" }));
        AppServerTestHarness.AssertConfigWriteError(await harness.Transport.ReadNextSentAsync(), "configValidationError");
        Assert.Equal(5, monitor.Current.Dreams.MinSessionsSinceLastRun);
    }

    [Fact]
    public async Task ConfigValueWrite_EnablingDreams_StartsTheScheduler()
    {
        _workspace.Config.Dreams.Enabled = false;
        for (var i = 0; i < 3; i++)
            await _workspace.SaveThreadAsync($"thread_{i}");
        await using var service = _workspace.CreateService(new FakeDreamsRunner());
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            Methods.ConfigValueWrite,
            new { keyPath = "Dreams.Enabled", value = true, mergeStrategy = "replace" }));

        AppServerTestHarness.AssertIsSuccessResponse(await harness.Transport.ReadNextSentAsync());
        var run = await _workspace.WaitForRunAsync(static run => !run.IsRunning);
        Assert.Equal(DreamsConstants.TriggerScheduled, run.Trigger);
        Assert.NotNull(service.NextRunAt);
    }

    private AppServerTestHarness CreateHarness(DreamsService service, IAppConfigMonitor? monitor = null) =>
        new(
            workspaceCraftPath: _workspace.CraftPath,
            appConfigMonitor: monitor ?? new AppConfigMonitor(_workspace.Config),
            dreamsService: service);

    private static async Task<JsonElement> RequestAsync(AppServerTestHarness harness, string method, object parameters)
    {
        await harness.ExecuteRequestAsync(harness.BuildRequest(method, parameters));
        var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        return response.RootElement.GetProperty("result").Clone();
    }
}
