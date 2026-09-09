using System.Runtime.CompilerServices;
using DotCraft.Automations;
using DotCraft.Automations.Protocol;
using DotCraft.Channels;
using DotCraft.Sessions;
using DotCraft.Workspaces;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Microsoft.Extensions.Logging;
namespace DotCraft.Tests.Tools;
public sealed class AutomationLifecycleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "automation_lifecycle_" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private AutomationService Service() => new(new AutomationsConfig { PollingInterval = TimeSpan.FromHours(1) },
        new DotCraftPaths(_root, Path.Combine(_root, ".craft"), null), NullLogger<AutomationService>.Instance);
    private static AutomationInput Input() => new() { Name = "Check", Prompt = "Inspect", WorkspaceMode = "project", Schedule = new() { Kind = "every", EveryMs = 60000 } };
    [Fact]
    public async Task Create_RejectsClientOwnedCompletedStatus()
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() => Service().CreateAsync(Input() with { Status = "completed" }));
        Assert.Equal("automation.invalidStatus", error.Message);
    }
    [Fact]
    public async Task Update_RejectsStaleVersion_AndPersistsOrigin()
    {
        var service = Service();
        var origin = new AutomationOrigin { Channel = "qq", UserId = "u", GroupId = "g", DeliveryTarget = "group:g" };
        var first = await service.CreateAsync(Input(), origin);
        var updated = await service.UpdateAsync(first.Id, first.Version, first with { Name = "Changed", Status = "paused" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(first.Id, first.Version, Input()));
        var loaded = await Service().ReadAsync(first.Id);
        Assert.Equal(updated, loaded);
        Assert.Equal(origin, loaded.Origin);
        Assert.Null(loaded.NextRunAt);
    }
    [Fact]
    public async Task Update_WeeklyPromptEditAfterJsonRoundTrip_PreservesMissedOccurrence()
    {
        var service = Service();
        var original = await service.CreateAsync(Input() with
        {
            Schedule = new() { Kind = "weekly", Hour = 9, Minute = 0, TimeZone = "UTC", Days = [1, 3] }
        });
        var missed = original with { NextRunAt = DateTimeOffset.UtcNow.AddDays(-7) };
        var store = new AutomationStore(Path.Combine(_root, ".craft", "automations"));
        await store.SaveAsync(missed, default);
        var restored = Service();
        var draft = System.Text.Json.JsonSerializer.Deserialize<AutomationInput>(
            System.Text.Json.JsonSerializer.Serialize(missed, AutomationStore.Json), AutomationStore.Json)!;
        Assert.NotSame(missed.Schedule.Days, draft.Schedule.Days);
        var updated = await restored.UpdateAsync(missed.Id, missed.Version, draft with { Prompt = "Inspect important changes" });
        Assert.Equal(missed.NextRunAt, updated.NextRunAt);
        var reordered = await restored.UpdateAsync(updated.Id, updated.Version, updated with
        {
            Schedule = updated.Schedule with { Days = [3, 1, 1] }, Prompt = "Inspect again"
        });
        Assert.Equal(missed.NextRunAt, reordered.NextRunAt);
        var rescheduled = await restored.UpdateAsync(reordered.Id, reordered.Version, reordered with
        {
            Schedule = reordered.Schedule with { Hour = 10 }
        });
        Assert.True(rescheduled.NextRunAt > DateTimeOffset.UtcNow);
    }
    [Fact]
    public async Task IndependentRuns_CreateDifferentThreads_KeepPausedState_AndDeliveryFailureDoesNotReplay()
    {
        var service = Service(); var client = new Sessions(); service.SetSessionClient(client);
        service.DeliverAsync = (_, _, _) => throw new InvalidOperationException("delivery unavailable");
        await service.StartAsync();
        try
        {
            var definition = await service.CreateAsync(Input() with { Status = "paused" });
            var one = await Finish(service, await service.RunAsync(definition.Id));
            var two = await Finish(service, await service.RunAsync(definition.Id));
            Assert.NotEqual(one.ThreadId, two.ThreadId);
            Assert.Equal("succeeded", one.Status); Assert.Equal("failed", one.DeliveryStatus);
            Assert.Equal("turn-1", one.TurnId);
            Assert.Equal("paused", (await service.ReadAsync(definition.Id)).Status);
            Assert.Equal(2, client.Submissions);
            Assert.Equal(2, (await service.ListRunsAsync(definition.Id)).Count);
            await service.DeleteAsync(definition.Id);
            Assert.Equal(2, (await service.ListRunsAsync(definition.Id)).Count);
        }
        finally { await service.StopAsync(); }
    }
    [Fact]
    public async Task Run_StaysExclusiveUntilTerminalObserverCompletes()
    {
        var service = Service(); var client = new Sessions(); service.SetSessionClient(client);
        var observed = new[] { new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        var release = new[] { new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        await service.StartAsync();
        try
        {
            var definition = await service.CreateAsync(Input() with { Status = "paused" });
            var notifications = 0;
            service.Updated += async (_, _, _) =>
            {
                var index = Interlocked.Increment(ref notifications) - 1;
                if (index < observed.Length)
                {
                    observed[index].TrySetResult();
                    await release[index].Task;
                }
            };
            var first = await service.RunAsync(definition.Id);
            await observed[0].Task.WaitAsync(TimeSpan.FromSeconds(3));
            var rejected = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(definition.Id));
            Assert.Equal("automation.alreadyRunning", rejected.Message);
            Assert.Single(await service.ListRunsAsync(definition.Id));
            release[0].TrySetResult();
            AutomationRun? second = null;
            for (var attempt = 0; attempt < 100 && second == null; attempt++)
            {
                try { second = await service.RunAsync(definition.Id); }
                catch (InvalidOperationException error) when (error.Message == "automation.alreadyRunning")
                { await Task.Delay(10); }
            }
            Assert.NotNull(second);
            Assert.NotEqual(first.Id, second.Id);
            await observed[1].Task.WaitAsync(TimeSpan.FromSeconds(3));
            var stillRejected = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(definition.Id));
            Assert.Equal("automation.alreadyRunning", stillRejected.Message);
            Assert.Equal(2, (await service.ListRunsAsync(definition.Id)).Count);
            Assert.Equal(2, client.Submissions);
        }
        finally
        {
            foreach (var signal in release) signal.TrySetResult();
            await service.StopAsync();
        }
    }
    [Fact]
    public async Task ImportantPolicy_SkipsUnchangedSuccess_AndRestoresChannelOrigin()
    {
        var service = Service(); var client = new Sessions(); service.SetSessionClient(client);
        var delivered = 0;
        service.DeliverAsync = (_, _, _) => { delivered++; return Task.CompletedTask; };
        client.DuringTurn = thread => {
            Assert.Equal("group:g", ChannelSessionScope.Current?.DefaultDeliveryTarget);
            service.ReportOutcome(thread, "turn-1", "Unchanged", false, "Already checked revision A");
        };
        await service.StartAsync();
        try
        {
            var definition = await service.CreateAsync(Input() with { Status = "paused", NotificationPolicy = "important" },
                new() { Channel = "qq", UserId = "u", GroupId = "g", DeliveryTarget = "group:g" });
            var run = await Finish(service, await service.RunAsync(definition.Id));
            Assert.Equal("succeeded", run.Status); Assert.Equal("skipped", run.DeliveryStatus); Assert.Equal(0, delivered);
            Assert.Equal("Already checked revision A", await File.ReadAllTextAsync(Path.Combine(_root, ".craft", "automations", definition.Id, "memory.md")));
        }
        finally { await service.StopAsync(); }
    }
    [Fact]
    public async Task PersistedMemory_IsBoundedBeforeEnteringTheRunPrompt()
    {
        var service = Service(); var client = new Sessions(); service.SetSessionClient(client);
        var definition = await service.CreateAsync(Input() with { Status = "paused" });
        var memoryPath = Path.Combine(_root, ".craft", "automations", definition.Id, "memory.md");
        await File.WriteAllTextAsync(memoryPath, new string('x', AutomationService.MaxMemoryChars) + "must-not-reach-context");
        await service.StartAsync();
        try
        {
            await Finish(service, await service.RunAsync(definition.Id));
            Assert.NotNull(client.LastMessage);
            Assert.DoesNotContain("must-not-reach-context", client.LastMessage);
        }
        finally { await service.StopAsync(); }
    }
    [Fact]
    public async Task OneShotFailure_CompletesDefinition_WithoutRetries_AndAllowsManualRun()
    {
        var service = Service(); var client = new Sessions { Fail = true }; service.SetSessionClient(client);
        var definition = await service.CreateAsync(Input() with { Schedule = new() { Kind = "at", At = DateTimeOffset.UtcNow.AddSeconds(-1) } });
        await service.StartAsync();
        try
        {
            for (var i = 0; i < 100 && (await service.ListRunsAsync(definition.Id)).Count == 0; i++) await Task.Delay(10);
            var run = await Finish(service, Assert.Single(await service.ListRunsAsync(definition.Id)));
            Assert.Equal("failed", run.Status);
            await service.PollAsync();
            var completed = await service.ReadAsync(definition.Id);
            Assert.Equal("completed", completed.Status);
            Assert.Single(await service.ListRunsAsync(definition.Id));
            var updateError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.UpdateAsync(completed.Id, completed.Version, completed with { Name = "Changed" }));
            Assert.Equal("automation.completedReadOnly", updateError.Message);

            client.Fail = false;
            var manual = await Finish(service, await service.RunAsync(definition.Id));
            Assert.Equal("succeeded", manual.Status);
            Assert.Equal("completed", (await service.ReadAsync(definition.Id)).Status);
            Assert.Equal(2, (await service.ListRunsAsync(definition.Id)).Count);
        }
        finally { await service.StopAsync(); }
    }
    [Fact]
    public async Task Restart_RecoversClaimBeforeScheduleWrite_WithoutReplayingOccurrence()
    {
        var service = Service();
        var definition = await service.CreateAsync(Input() with { Schedule = new() { Kind = "at", At = DateTimeOffset.UtcNow.AddMinutes(-1) } });
        var store = new AutomationStore(Path.Combine(_root, ".craft", "automations"));
        await store.SaveRunAsync(new() { Id = "claimed", AutomationId = definition.Id, DefinitionVersion = definition.Version,
            CreatedAt = DateTimeOffset.UtcNow, ScheduledAt = definition.NextRunAt, Status = "queued" }, default);
        var restarted = Service(); var client = new Sessions(); restarted.SetSessionClient(client);
        await restarted.StartAsync();
        try
        {
            await restarted.PollAsync();
            Assert.Equal("interrupted", Assert.Single(await restarted.ListRunsAsync(definition.Id)).Status);
            Assert.Equal("completed", (await restarted.ReadAsync(definition.Id)).Status);
            Assert.Equal(0, client.Submissions);
        }
        finally { await restarted.StopAsync(); }
    }
    [Fact]
    public async Task Restart_PreservesSuccessfulExecution_WhenDeliveryWasInterrupted()
    {
        var service = Service();
        var definition = await service.CreateAsync(Input() with { Status = "paused" });
        var store = new AutomationStore(Path.Combine(_root, ".craft", "automations"));
        await store.SaveRunAsync(new() { Id = "delivering", AutomationId = definition.Id, DefinitionVersion = definition.Version,
            CreatedAt = DateTimeOffset.UtcNow, CompletedAt = DateTimeOffset.UtcNow, Status = "succeeded", Summary = "Done", DeliveryStatus = "pending" }, default);
        var restarted = Service(); var client = new Sessions(); restarted.SetSessionClient(client);
        var deliveries = 0;
        restarted.DeliverAsync = (_, _, _) => { deliveries++; return Task.CompletedTask; };
        await restarted.StartAsync();
        try
        {
            var run = Assert.Single(await restarted.ListRunsAsync(definition.Id));
            Assert.Equal("succeeded", run.Status);
            Assert.Equal("failed", run.DeliveryStatus);
            Assert.Equal("automation.deliveryInterrupted", run.DeliveryError);
            Assert.Equal(0, deliveries); Assert.Equal(0, client.Submissions);
        }
        finally { await restarted.StopAsync(); }
    }
    [Fact]
    public async Task ExplicitWorktreeFailure_DoesNotFallBackToProject()
    {
        var service = Service(); var client = new Sessions(); service.SetSessionClient(client);
        await service.StartAsync();
        try
        {
            var definition = await service.CreateAsync(Input() with { Status = "paused", WorkspaceMode = "worktree" });
            var run = await Finish(service, await service.RunAsync(definition.Id));
            Assert.Equal("failed", run.Status); Assert.Equal(0, client.Submissions);
            Assert.NotNull(run.ThreadId);
            Assert.Contains("worktree", run.Error);
        }
        finally { await service.StopAsync(); }
    }
    [Fact]
    public void CalendarSchedules_HandleWeekdaysAndDst()
    {
        var weekly = new AutomationSchedule { Kind = "weekly", Hour = 9, Minute = 0, TimeZone = "UTC", Days = [1] };
        Assert.Equal(DateTimeOffset.Parse("2026-09-14T09:00:00Z"), weekly.Next(DateTimeOffset.Parse("2026-09-08T10:00:00Z")));
        var daily = new AutomationSchedule { Kind = "daily", Hour = 2, Minute = 30, TimeZone = "America/New_York" };
        Assert.Equal(DateTimeOffset.Parse("2026-03-08T07:00:00Z"), daily.Next(DateTimeOffset.Parse("2026-03-08T05:00:00Z")));
        var fold = daily with { Hour = 1 };
        Assert.Equal(DateTimeOffset.Parse("2026-11-02T06:30:00Z"), fold.Next(DateTimeOffset.Parse("2026-11-01T05:40:00Z")));
        Assert.Throws<ArgumentException>(() => (daily with { TimeZone = "missing/zone" }).Validate());
    }
    [Fact]
    public void IntervalSchedule_AdvancesFromPlannedTime()
    {
        var schedule = new AutomationSchedule { Kind = "every", EveryMs = 60000 };
        var planned = DateTimeOffset.Parse("2026-09-08T10:00:00Z");
        Assert.Equal(planned.AddMinutes(4), schedule.Next(planned.AddMinutes(3).AddSeconds(10), planned));
    }
    private static async Task<AutomationRun> Finish(AutomationService service, AutomationRun run)
    {
        for (var i = 0; i < 200; i++)
        {
            var current = (await service.ListRunsAsync(run.AutomationId)).SingleOrDefault(r => r.Id == run.Id);
            if (current?.CompletedAt != null && current.DeliveryStatus != "pending") { await Task.Delay(20); return current; }
            await Task.Delay(10);
        }
        throw new TimeoutException("Run did not finish: " + System.Text.Json.JsonSerializer.Serialize(await service.ListRunsAsync(run.AutomationId)));
    }
    private sealed class Sessions : IAutomationSessionClient
    {
        public string DataPath => "unused";
        public int Submissions;
        public bool Fail;
        public Action<string>? DuringTurn;
        public string? LastMessage;
        public Task<string> CreateThreadAsync(string channelName, string userId, ThreadConfiguration config, CancellationToken ct, string? displayName = null) => Task.FromResult(userId);
        public Task<ThreadWorktreeInfo> EnsureRunWorktreeAsync(string threadId, string taskId, CancellationToken ct) => throw new InvalidOperationException("worktree failed");
        public Task<SessionThread?> TryGetThreadAsync(string threadId, CancellationToken ct) => Task.FromResult<SessionThread?>(new() { Id = threadId });
        public async IAsyncEnumerable<SessionEvent> SubmitTurnAsync(string threadId, string message, [EnumeratorCancellation] CancellationToken ct,
            TurnTriggerInfo? trigger = null, Func<CancellationToken, Task>? beforeAdmission = null)
        {
            if (beforeAdmission != null) await beforeAdmission(ct);
            Submissions++; LastMessage = message; Assert.Equal("automation", trigger?.Kind);
            yield return new() { EventType = SessionEventType.TurnStarted, ThreadId = threadId, TurnId = "turn-1" };
            await Task.Yield(); DuringTurn?.Invoke(threadId);
            yield return new() { EventType = Fail ? SessionEventType.TurnFailed : SessionEventType.TurnCompleted, ThreadId = threadId, TurnId = "turn-1", Payload = new SessionTurn { Id = "turn-1" } };
        }
    }
}




