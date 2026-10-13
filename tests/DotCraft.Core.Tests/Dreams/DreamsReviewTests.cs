using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Dreams;
using DotCraft.Memory;
using Xunit;

namespace DotCraft.Tests.Dreams;

public sealed class DreamsReviewTests : IDisposable
{
    private readonly DreamsTestWorkspace _workspace = new();
    private readonly FakeDreamsRunner _runner = new();

    public void Dispose() => _workspace.Dispose();

    private string Store => _workspace.WorkspaceStore;

    [Fact]
    public async Task Run_WritesTheManifestBaselineAndCandidate()
    {
        _workspace.WriteMemory("deploy", "Deploy on Fridays.", type: "feedback");
        await _workspace.SaveThreadAsync("thread_one");
        await using var service = _workspace.CreateService(_runner);

        var run = await RunAsync(service);

        var input = Assert.Single(_runner.Inputs);
        var manifest = await File.ReadAllTextAsync(run.InputManifestPath!);
        Assert.Contains("deploy.md", manifest, StringComparison.Ordinal);
        Assert.Contains("Deploy on Fridays.", manifest, StringComparison.Ordinal);
        Assert.Contains("[feedback]", manifest, StringComparison.Ordinal);
        Assert.Contains("thread_one", manifest, StringComparison.Ordinal);
        Assert.Equal(manifest, input.Manifest);
        Assert.Equal(["MEMORY.md", "deploy.md"], FileNames(input.BaselineDirectory));
        Assert.Equal(["MEMORY.md", "deploy.md"], FileNames(input.CandidateDirectory));
        Assert.Equal(await File.ReadAllTextAsync(Path.Combine(Store, "deploy.md")), await File.ReadAllTextAsync(Path.Combine(input.BaselineDirectory, "deploy.md")));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(run.InputManifestPath)!, "input.json")));
        Assert.Equal("thread_one", json.RootElement.GetProperty("threads")[0].GetProperty("ThreadId").GetString());
    }

    [Fact]
    public async Task SucceededRunWithChanges_StaysPendingUntilApplied()
    {
        WriteStore();
        var monitor = new AppConfigMonitor(_workspace.Config);
        var changes = new List<AppConfigChangedEventArgs>();
        monitor.Changed += (_, args) => changes.Add(args);
        _runner.Edit = EditCandidate;
        await using var service = _workspace.CreateService(_runner, monitor);

        var run = await RunAsync(service);

        Assert.Equal(DreamsReviewStatuses.Pending, run.ReviewStatus);
        Assert.Equal("Consolidated the store.", run.Summary);
        Assert.Equal(["added.md"], run.Changes!.Added);
        Assert.Equal(["kept.md"], run.Changes.Updated);
        Assert.Equal(["stale.md"], run.Changes.Deleted);
        Assert.True(run.Changes.IndexChanged);
        Assert.Equal(["MEMORY.md", "kept.md", "stale.md"], FileNames(Store));

        var applied = service.ApplyRun(run.Id)!;

        Assert.Equal(DreamsReviewStatuses.Applied, applied.ReviewStatus);
        Assert.False(applied.AutoApplied);
        Assert.Equal(["MEMORY.md", "added.md", "kept.md"], FileNames(Store));
        Assert.Equal("kept, merged", await File.ReadAllTextAsync(Path.Combine(Store, "kept.md")));
        Assert.True(File.Exists(Path.Combine(Store, ConsolidationLock.FileName)));
        Assert.Contains(changes, change => change.Regions.Contains(ConfigChangeRegions.Memory));
    }

    [Fact]
    public async Task Apply_WhenTheStoreChangedAfterTheRunStarted_KeepsTheRunPending()
    {
        WriteStore();
        _runner.Edit = EditCandidate;
        await using var service = _workspace.CreateService(_runner);
        var run = await RunAsync(service);
        File.WriteAllText(Path.Combine(Store, "kept.md"), "edited by the user");

        Assert.Throws<DreamsStoreChangedException>(() => service.ApplyRun(run.Id));

        var pending = service.LoadRun(run.Id)!;
        Assert.Equal(DreamsReviewStatuses.Pending, pending.ReviewStatus);
        Assert.Equal(DreamsConstants.StoreChangedErrorType, pending.ErrorType);
        Assert.Equal("edited by the user", await File.ReadAllTextAsync(Path.Combine(Store, "kept.md")));
        Assert.True(File.Exists(Path.Combine(Store, "stale.md")));
    }

    [Fact]
    public async Task RunWithoutChanges_IsAppliedWithNothingToChange()
    {
        WriteStore();
        await using var service = _workspace.CreateService(_runner);

        var run = await RunAsync(service);

        Assert.Equal(DreamsRunStatuses.Succeeded, run.Status);
        Assert.Equal(DreamsReviewStatuses.Applied, run.ReviewStatus);
        Assert.False(run.AutoApplied);
        Assert.False(run.Changes!.Any);
        Assert.Throws<InvalidOperationException>(() => service.ApplyRun(run.Id));
    }

    [Fact]
    public async Task AutoApply_AppliesNewRunsButNotRunsAlreadyPending()
    {
        WriteStore();
        _runner.Edit = EditCandidate;
        await using var service = _workspace.CreateService(_runner);
        var pending = await RunAsync(service);

        _workspace.Config.Dreams.AutoApply = true;
        _runner.Edit = input => File.WriteAllText(Path.Combine(input.CandidateDirectory, "kept.md"), "kept, automatically");
        var autoApplied = await RunAsync(service);

        Assert.Equal(DreamsReviewStatuses.Pending, service.LoadRun(pending.Id)!.ReviewStatus);
        Assert.Equal(DreamsReviewStatuses.Applied, autoApplied.ReviewStatus);
        Assert.True(autoApplied.AutoApplied);
        Assert.Equal("kept, automatically", await File.ReadAllTextAsync(Path.Combine(Store, "kept.md")));
        Assert.Throws<DreamsStoreChangedException>(() => service.ApplyRun(pending.Id));
    }

    [Fact]
    public async Task AutoApply_WhenTheStoreChangedDuringTheRun_LeavesTheRunPending()
    {
        WriteStore();
        _workspace.Config.Dreams.AutoApply = true;
        _runner.Edit = input =>
        {
            EditCandidate(input);
            File.WriteAllText(Path.Combine(Store, "concurrent.md"), "saved while the run worked");
        };
        await using var service = _workspace.CreateService(_runner);

        var run = await RunAsync(service);

        Assert.Equal(DreamsReviewStatuses.Pending, run.ReviewStatus);
        Assert.False(run.AutoApplied);
        Assert.Equal(DreamsConstants.StoreChangedErrorType, run.ErrorType);
        Assert.Equal(["MEMORY.md", "concurrent.md", "kept.md", "stale.md"], FileNames(Store));
    }

    [Fact]
    public async Task Restore_WritesTheBaselineBackWhileTheStoreMatchesTheCandidate()
    {
        WriteStore();
        _runner.Edit = EditCandidate;
        await using var service = _workspace.CreateService(_runner);
        var first = await RunAsync(service);
        service.ApplyRun(first.Id);

        var restored = service.RestoreRun(first.Id)!;

        Assert.Equal(DreamsReviewStatuses.Discarded, restored.ReviewStatus);
        Assert.Equal(["MEMORY.md", "kept.md", "stale.md"], FileNames(Store));
        Assert.Equal("kept", await File.ReadAllTextAsync(Path.Combine(Store, "kept.md")));

        var second = await RunAsync(service);
        service.ApplyRun(second.Id);
        File.WriteAllText(Path.Combine(Store, "added.md"), "edited after apply");

        Assert.Throws<DreamsStoreChangedException>(() => service.RestoreRun(second.Id));
        Assert.Equal(DreamsReviewStatuses.Applied, service.LoadRun(second.Id)!.ReviewStatus);
        Assert.Equal("edited after apply", await File.ReadAllTextAsync(Path.Combine(Store, "added.md")));
    }

    [Fact]
    public async Task GetRunReview_ListsEachChangedFileWithBaselineAndCandidate()
    {
        WriteStore();
        _runner.Edit = EditCandidate;
        await using var service = _workspace.CreateService(_runner);
        var run = await RunAsync(service);

        var review = service.GetRunReview(run.Id)!;

        Assert.Equal(["added.md", "kept.md", "stale.md", "MEMORY.md"], review.Files.Select(static file => file.Name));
        var added = review.Files[0];
        Assert.Equal(("added", null, "new fact"), (added.Change, added.Baseline, added.Candidate));
        var deleted = review.Files[2];
        Assert.Equal(("deleted", "stale", null), (deleted.Change, deleted.Baseline, deleted.Candidate));
        Assert.Equal(("updated", "kept", "kept, merged"), (review.Files[1].Change, review.Files[1].Baseline, review.Files[1].Candidate));
    }

    [Fact]
    public async Task DiscardAndArchive_LeaveTheStoreUnchanged()
    {
        WriteStore();
        _runner.Edit = EditCandidate;
        await using var service = _workspace.CreateService(_runner);
        var run = await RunAsync(service);

        Assert.Equal(DreamsReviewStatuses.Discarded, service.DiscardRun(run.Id)!.ReviewStatus);
        Assert.Throws<InvalidOperationException>(() => service.ApplyRun(run.Id));
        Assert.Equal(DreamsReviewStatuses.Archived, service.ArchiveRun(run.Id)!.ReviewStatus);

        Assert.Empty(service.ListRuns());
        Assert.Single(service.ListRuns(includeArchived: true));
        Assert.Equal(["MEMORY.md", "kept.md", "stale.md"], FileNames(Store));
    }

    [Fact]
    public async Task DeleteRuns_RemovesRunRecordsButNeverTheStore()
    {
        WriteStore();
        _runner.Edit = EditCandidate;
        await using var service = _workspace.CreateService(_runner);
        var first = await RunAsync(service);
        service.ApplyRun(first.Id);
        var second = await RunAsync(service);

        Assert.Equal(first.Id, service.DeleteRun(first.Id)!.Id);
        Assert.Equal(second.Id, service.LoadLatestState()!.Id);
        Assert.Equal([second.Id], (await service.DeleteAllRunsAsync()).Select(static run => run.Id));

        Assert.Null(service.LoadLatestState());
        Assert.Empty(service.ListRuns(includeArchived: true));
        Assert.Equal(["MEMORY.md", "added.md", "kept.md"], FileNames(Store));
    }

    [Fact]
    public async Task Reset_CancelsTheActiveRunThenClearsStoresAndRuns()
    {
        WriteStore();
        var runner = new FakeDreamsRunner { BlockUntilReleased = true };
        await using var service = _workspace.CreateService(runner);
        await service.CreateRunAsync(new DreamsRunRequest());
        await runner.WaitUntilCalledAsync();

        await service.ResetAsync(() => MemoryScopes.ClearAll(new MemoryStore(_workspace.CraftPath)));

        Assert.True(runner.Canceled);
        Assert.False(service.IsRunning);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Store));
        Assert.True(Directory.Exists(_workspace.Artifacts.DreamsDirectoryPath));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_workspace.Artifacts.DreamsDirectoryPath));
    }

    [Fact]
    public async Task Cancel_EndsTheRunCanceledAndRestoresTheLockTime()
    {
        WriteStore();
        var runner = new FakeDreamsRunner { BlockUntilReleased = true };
        await using var service = _workspace.CreateService(runner);
        var run = await service.CreateRunAsync(new DreamsRunRequest());
        await runner.WaitUntilCalledAsync();

        await service.CancelRunAsync(run!.Id);
        var canceled = await _workspace.WaitForRunAsync(state => state.Id == run.Id && !state.IsRunning);

        Assert.Equal(DreamsRunStatuses.Canceled, canceled.Status);
        Assert.Null(ConsolidationLock.LastConsolidatedAt(Store));
        Assert.Equal(["MEMORY.md", "kept.md", "stale.md"], FileNames(Store));
    }

    private async Task<DreamsRunState> RunAsync(DreamsService service)
    {
        var run = await service.CreateRunAsync(new DreamsRunRequest());
        Assert.NotNull(run);
        return await _workspace.WaitForRunAsync(state => state.Id == run.Id && !state.IsRunning);
    }

    private void WriteStore()
    {
        DreamsTestWorkspace.WriteFile(Store, "kept.md", "kept");
        DreamsTestWorkspace.WriteFile(Store, "stale.md", "stale");
        DreamsTestWorkspace.WriteFile(Store, "MEMORY.md", "- [Kept](kept.md) — kept\n- [Stale](stale.md) — stale\n");
    }

    private static void EditCandidate(DreamsRunInput input)
    {
        File.WriteAllText(Path.Combine(input.CandidateDirectory, "kept.md"), "kept, merged");
        File.WriteAllText(Path.Combine(input.CandidateDirectory, "added.md"), "new fact");
        File.Delete(Path.Combine(input.CandidateDirectory, "stale.md"));
        File.WriteAllText(Path.Combine(input.CandidateDirectory, "MEMORY.md"), "- [Kept](kept.md) — kept\n- [Added](added.md) — new\n");
    }

    private static string[] FileNames(string directory) =>
        Directory.EnumerateFiles(directory, "*.md")
            .Select(static path => Path.GetFileName(path))
            .Order(StringComparer.Ordinal)
            .ToArray();
}
