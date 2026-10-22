using DotCraft.Agents;
using DotCraft.Dreams;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Dreams;

public sealed class DreamsSchedulingTests : IDisposable
{
    private readonly DreamsTestWorkspace _workspace = new();
    private readonly FakeDreamsRunner _runner = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task ScheduledCheck_RunsAStoreOnceEnoughThreadsCompletedTurnsSinceItsLastConsolidation()
    {
        var lastConsolidated = DateTimeOffset.UtcNow.AddDays(-2);
        DreamsTestWorkspace.SetLastConsolidated(_workspace.WorkspaceStore, lastConsolidated);
        await _workspace.SaveThreadAsync("thread_before", lastConsolidated.AddHours(-1));
        await _workspace.SaveThreadAsync("thread_one");
        await _workspace.SaveThreadAsync("thread_two");
        await using var service = _workspace.CreateService(_runner);

        Assert.Empty(await service.RunScheduledCheckAsync());
        Assert.Empty(_workspace.Runs(includeArchived: true));
        Assert.Equal(lastConsolidated.UtcDateTime, ConsolidationLock.LastConsolidatedAt(_workspace.WorkspaceStore)!.Value.UtcDateTime, TimeSpan.FromSeconds(1));

        await _workspace.SaveThreadAsync("thread_three");
        var run = Assert.Single(await service.RunScheduledCheckAsync());

        Assert.Equal(DreamsRunStatuses.Succeeded, run.Status);
        Assert.Equal(DreamsConstants.TriggerScheduled, run.Trigger);
        Assert.Null(run.Scope);
        Assert.Equal(3, run.CandidateThreadCount);
        Assert.Equal(["thread_three", "thread_two", "thread_one"], Assert.Single(_runner.Inputs).Threads.Select(static t => t.ThreadId));
        Assert.True(ConsolidationLock.LastConsolidatedAt(_workspace.WorkspaceStore) > lastConsolidated.AddDays(1));

        Assert.Empty(await service.RunScheduledCheckAsync());
    }

    [Fact]
    public async Task ScheduledCheck_CountsOnlyEligibleThreadsThatUseTheStore()
    {
        await _workspace.SaveThreadAsync("thread_one");
        await _workspace.SaveThreadAsync("thread_two");
        await _workspace.SaveThreadAsync("internal", configure: thread => thread.Metadata[ThreadVisibility.InternalMetadataKey] = "dreams");
        await _workspace.SaveThreadAsync("subagent", configure: thread => thread.OriginChannel = SubAgentThreadOrigin.ChannelName);
        await _workspace.SaveThreadAsync("client_history", configure: thread => thread.HistoryMode = HistoryMode.Client);
        await _workspace.SaveThreadAsync("memory_off", configure: thread => thread.Configuration!.MemoryEnabled = false);
        await _workspace.SaveThreadAsync("other_store", scope: "agent-other");
        await using var service = _workspace.CreateService(_runner);

        Assert.Empty(await service.RunScheduledCheckAsync());

        await _workspace.SaveThreadAsync("thread_archived", configure: thread => thread.Status = ThreadStatus.Archived);
        Assert.Single(await service.RunScheduledCheckAsync());
        Assert.Equal(
            ["thread_archived", "thread_one", "thread_two"],
            Assert.Single(_runner.Inputs).Threads.Select(static t => t.ThreadId).Order());
    }

    [Fact]
    public async Task ScheduledCheck_ConsolidatesOwnProfileStoresButNotAutomationStores()
    {
        new AgentProfileStore(_workspace.CraftPath).Upsert("Night Shift", AgentProfileSources.Workspace, OwnProfile("Night Shift"));
        var scope = AgentProfileName.MemoryScope("Night Shift");
        for (var i = 0; i < 3; i++)
        {
            await _workspace.SaveThreadAsync($"profile_{i}", scope: scope);
            await _workspace.SaveThreadAsync($"automation_{i}", scope: "nightly-report");
        }

        _workspace.WriteMemory("report-format", "Use tables.", _workspace.StoreOf("nightly-report"));
        await using var service = _workspace.CreateService(_runner);

        var run = Assert.Single(await service.RunScheduledCheckAsync());

        Assert.Equal(scope, run.Scope);
        Assert.NotNull(ConsolidationLock.LastConsolidatedAt(_workspace.StoreOf(scope)));
        Assert.Null(ConsolidationLock.LastConsolidatedAt(_workspace.StoreOf("nightly-report")));
    }

    [Fact]
    public async Task ScheduledCheck_WhileAnotherProcessHoldsTheLock_CreatesNoRun()
    {
        _workspace.Config.Dreams.Interval = TimeSpan.FromMinutes(1);
        DreamsTestWorkspace.SetLastConsolidated(_workspace.WorkspaceStore, DateTimeOffset.UtcNow.AddMinutes(-10));
        for (var i = 0; i < 3; i++)
            await _workspace.SaveThreadAsync($"thread_{i}");
        await using var service = _workspace.CreateService(_runner);
        await using var otherProcess = new FileStream(
            Path.Combine(_workspace.WorkspaceStore, ConsolidationLock.FileName), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.Empty(await service.RunScheduledCheckAsync());
        Assert.Empty(_runner.Inputs);
    }

    [Fact]
    public async Task FailedRun_RestoresTheLockTimeAndLeavesTheStoreUnchanged()
    {
        var lastConsolidated = DateTimeOffset.UtcNow.AddDays(-2);
        DreamsTestWorkspace.SetLastConsolidated(_workspace.WorkspaceStore, lastConsolidated);
        var memory = _workspace.WriteMemory("deploy", "Deploy on Fridays.");
        var original = await File.ReadAllTextAsync(memory);
        for (var i = 0; i < 3; i++)
            await _workspace.SaveThreadAsync($"thread_{i}");
        _runner.Edit = input => File.WriteAllText(Path.Combine(input.CandidateDirectory, "deploy.md"), "rewritten");
        _runner.Result = _ => new DreamsGenerationResult(false, null, "model unavailable", "turn_failed");
        await using var service = _workspace.CreateService(_runner);

        var run = Assert.Single(await service.RunScheduledCheckAsync());

        Assert.Equal(DreamsRunStatuses.Failed, run.Status);
        Assert.Equal("turn_failed", run.ErrorType);
        Assert.Equal(original, await File.ReadAllTextAsync(memory));
        Assert.Equal(lastConsolidated.UtcDateTime, ConsolidationLock.LastConsolidatedAt(_workspace.WorkspaceStore)!.Value.UtcDateTime, TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData("notes.txt", "not markdown")]
    [InlineData("Bad Name.md", "spaces")]
    [InlineData("MEMORY.md", null)]
    public async Task InvalidCandidate_FailsTheRunAndLeavesTheStoreUnchanged(string name, string? content)
    {
        _workspace.WriteMemory("deploy", "Deploy on Fridays.");
        var index = await File.ReadAllTextAsync(Path.Combine(_workspace.WorkspaceStore, "MEMORY.md"));
        _runner.Edit = input => File.WriteAllText(
            Path.Combine(input.CandidateDirectory, name),
            content ?? string.Join('\n', Enumerable.Range(0, 201).Select(i => $"- [M{i}](m{i}.md) — line")));
        await using var service = _workspace.CreateService(_runner);

        var run = await service.CreateRunAsync(new DreamsRunRequest());
        var finished = await _workspace.WaitForRunAsync(state => state.Id == run!.Id && !state.IsRunning);

        Assert.Equal(DreamsRunStatuses.Failed, finished.Status);
        Assert.Equal("invalid_candidate", finished.ErrorType);
        Assert.Equal(index, await File.ReadAllTextAsync(Path.Combine(_workspace.WorkspaceStore, "MEMORY.md")));
        if (content != null)
            Assert.False(File.Exists(Path.Combine(_workspace.WorkspaceStore, name)));
        Assert.Null(ConsolidationLock.LastConsolidatedAt(_workspace.WorkspaceStore));
    }

    [Fact]
    public async Task ManualRun_TargetsStoresWithMemoryFilesOrEligibleThreadsOneAtATime()
    {
        var profiles = new AgentProfileStore(_workspace.CraftPath);
        profiles.Upsert("Scout", AgentProfileSources.Workspace, OwnProfile("Scout"));
        profiles.Upsert("Idle", AgentProfileSources.Workspace, OwnProfile("Idle"));
        var scoutScope = AgentProfileName.MemoryScope("Scout");
        _workspace.WriteMemory("deploy", "Deploy on Fridays.");
        await _workspace.SaveThreadAsync("scout_thread", scope: scoutScope);
        await using var service = _workspace.CreateService(_runner);

        var first = await service.RequestRunAsync();
        Assert.NotNull(first);
        Assert.Equal(DreamsRunStatuses.Running, first.Status);
        await _workspace.WaitForRunAsync(state => state.Scope == scoutScope && !state.IsRunning);
        var runs = _workspace.Runs();

        Assert.Equal(2, runs.Count);
        Assert.All(runs, run => Assert.Equal(DreamsRunStatuses.Succeeded, run.Status));
        Assert.Equal([null, scoutScope], runs.OrderBy(static run => run.StartedAt).Select(static run => run.Scope));
        Assert.All(runs, run => Assert.Equal(DreamsConstants.TriggerManual, run.Trigger));
    }

    [Fact]
    public async Task ManualRun_WithNothingToConsolidate_IsSkippedWithoutModelWork()
    {
        await using var service = _workspace.CreateService(_runner);

        var run = await service.CreateRunAsync(new DreamsRunRequest());
        var finished = await _workspace.WaitForRunAsync(state => state.Id == run!.Id && !state.IsRunning);

        Assert.Equal(DreamsRunStatuses.Skipped, finished.Status);
        Assert.Empty(_runner.Inputs);
        Assert.Null(ConsolidationLock.LastConsolidatedAt(_workspace.WorkspaceStore));
    }

    [Fact]
    public async Task ManualRun_BypassesThresholdsButNotTheSwitches()
    {
        DreamsTestWorkspace.SetLastConsolidated(_workspace.WorkspaceStore, DateTimeOffset.UtcNow.AddMinutes(-90));
        await _workspace.SaveThreadAsync("thread_one");
        await using var service = _workspace.CreateService(_runner);

        var run = await service.CreateRunAsync(new DreamsRunRequest(ThreadIds: ["thread_one"], Instructions: "Focus on deploys."));
        var finished = await _workspace.WaitForRunAsync(state => state.Id == run!.Id && !state.IsRunning);
        Assert.Equal(DreamsRunStatuses.Succeeded, finished.Status);
        Assert.Equal("Focus on deploys.", Assert.Single(_runner.Inputs).Instructions);

        _workspace.Config.Memory.Enabled = false;
        Assert.Null(await service.RequestRunAsync());
        _workspace.Config.Memory.Enabled = true;
        _workspace.Config.Dreams.Enabled = false;
        Assert.Null(await service.CreateRunAsync(new DreamsRunRequest()));
        Assert.Single(_workspace.Runs());
    }

    [Fact]
    public async Task CreateRun_RejectsAScopeThatIsNotAConsolidatableStore()
    {
        await using var service = _workspace.CreateService(_runner);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateRunAsync(new DreamsRunRequest(Scope: "nightly-report")));
    }

    private static string OwnProfile(string name) =>
        $"""
---
name: {name}
description: Profile {name}
memory: own
---

Instructions for {name}.
""";
}
