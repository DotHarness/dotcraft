using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Dreams;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Dreams;

public sealed class DreamsToolProfileTests : IDisposable
{
    private const string ThreadId = "thread_dream";

    private readonly DreamsTestWorkspace _workspace = new();
    private readonly DreamsRunRegistry _registry;
    private readonly string _baseline;
    private readonly string _candidate;

    public DreamsToolProfileTests()
    {
        _registry = new DreamsRunRegistry(_workspace.Threads);
        _baseline = _workspace.Artifacts.BaselineDirectory("dream_run");
        _candidate = _workspace.Artifacts.CandidateDirectory("dream_run");
        foreach (var directory in new[] { _baseline, _candidate })
        {
            DreamsTestWorkspace.WriteFile(directory, "deploy.md", "Deploy on Fridays.");
            DreamsTestWorkspace.WriteFile(directory, "MEMORY.md", "- [Deploy](deploy.md) — deploy day");
        }
    }

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task CandidateWrites_AreLimitedToTopLevelMarkdownFiles()
    {
        var snapshot = await CreateSnapshotAsync();

        await InvokeAsync(snapshot, "WriteFile", new() { ["path"] = "release.md", ["content"] = "Release on Mondays." });
        await InvokeAsync(snapshot, "WriteFile", new() { ["path"] = Path.Combine(_candidate, "MEMORY.md"), ["content"] = "- [Release](release.md) — day" });
        await InvokeAsync(snapshot, "EditFile", new() { ["path"] = "deploy.md", ["oldText"] = "Fridays", ["newText"] = "Thursdays" });

        Assert.Equal("Release on Mondays.", await File.ReadAllTextAsync(Path.Combine(_candidate, "release.md")));
        Assert.Equal("Deploy on Thursdays.", await File.ReadAllTextAsync(Path.Combine(_candidate, "deploy.md")));
        foreach (var path in new[]
                 {
                     "notes.txt",
                     Path.Combine("nested", "deep.md"),
                     Path.Combine("..", "escape.md"),
                     Path.Combine(_baseline, "deploy.md"),
                     Path.Combine(_workspace.WorkspacePath, "README.md"),
                     Path.Combine(_workspace.WorkspaceStore, "direct.md")
                 })
        {
            await InvokeAsync(snapshot, "WriteFile", new() { ["path"] = path, ["content"] = "refused" }, success: false);
        }

        await InvokeAsync(snapshot, "EditFile", new() { ["path"] = Path.Combine(_baseline, "deploy.md"), ["oldText"] = "Fridays", ["newText"] = "never" }, success: false);
        Assert.Equal("Deploy on Fridays.", await File.ReadAllTextAsync(Path.Combine(_baseline, "deploy.md")));
        Assert.False(Directory.Exists(Path.Combine(_candidate, "nested")));
        Assert.False(File.Exists(Path.Combine(_candidate, "notes.txt")));
        Assert.False(File.Exists(Path.Combine(_workspace.WorkspaceStore, "direct.md")));
    }

    [Fact]
    public async Task ReadToolsReachBaselineAndWorkspace_ButDeleteRemovesOnlyCandidateMemoryFiles()
    {
        DreamsTestWorkspace.WriteFile(_workspace.WorkspacePath, "README.md", "Workspace readme.");
        var snapshot = await CreateSnapshotAsync();
        Assert.Contains("Deploy on Fridays.", await InvokeAsync(snapshot, "ReadFile", new() { ["path"] = Path.Combine(_baseline, "deploy.md") }), StringComparison.Ordinal);
        Assert.Contains("Workspace readme.", await InvokeAsync(snapshot, "ReadFile", new() { ["path"] = Path.Combine(_workspace.WorkspacePath, "README.md") }), StringComparison.Ordinal);

        await InvokeAsync(snapshot, "DeleteMemoryFile", new() { ["name"] = "deploy.md" });
        await InvokeAsync(snapshot, "DeleteMemoryFile", new() { ["name"] = "MEMORY.md" }, success: false);
        await InvokeAsync(snapshot, "DeleteMemoryFile", new() { ["name"] = "missing.md" }, success: false);
        await InvokeAsync(snapshot, "DeleteMemoryFile", new() { ["name"] = Path.Combine(_baseline, "deploy.md") }, success: false);

        Assert.False(File.Exists(Path.Combine(_candidate, "deploy.md")));
        Assert.True(File.Exists(Path.Combine(_candidate, "MEMORY.md")));
        Assert.True(File.Exists(Path.Combine(_baseline, "deploy.md")));
    }

    [Fact]
    public async Task EvidenceTools_SearchAndReadOnlyManifestThreadsWithinTheirBudgets()
    {
        await _workspace.SaveThreadAsync("thread_one", userText: "We moved deploys to Thursday afternoons.");
        await _workspace.SaveThreadAsync("thread_other", userText: "Thursday is also mentioned here.");
        var snapshot = await CreateSnapshotAsync("thread_one");

        var found = await InvokeAsync(snapshot, "SearchTranscripts", new() { ["query"] = "thursday" });
        Assert.Contains("thread_one", found, StringComparison.Ordinal);
        Assert.Contains("deploys to Thursday", found, StringComparison.Ordinal);
        Assert.DoesNotContain("thread_other", found, StringComparison.Ordinal);

        var read = await InvokeAsync(snapshot, "ReadTranscript", new() { ["threadId"] = "thread_one" });
        Assert.Contains("USER: We moved deploys to Thursday afternoons.", read, StringComparison.Ordinal);
        Assert.Contains("ASSISTANT: Noted.", read, StringComparison.Ordinal);

        Assert.StartsWith("Error", await InvokeAsync(snapshot, "ReadTranscript", new() { ["threadId"] = "thread_other" }), StringComparison.Ordinal);
        Assert.StartsWith("Error", await InvokeAsync(snapshot, "SearchTranscripts", new() { ["query"] = "x", ["threadIds"] = new JsonArray("thread_other") }), StringComparison.Ordinal);

        for (var i = 1; i < 20; i++)
            await InvokeAsync(snapshot, "SearchTranscripts", new() { ["query"] = "nothing-matches" });
        Assert.StartsWith("Error", await InvokeAsync(snapshot, "SearchTranscripts", new() { ["query"] = "thursday" }), StringComparison.Ordinal);

        Assert.True(_registry.TryGet(ThreadId, out var run));
        Assert.Equal(["thread_one"], run.Diagnostics.EvidenceThreadIds);
        Assert.Equal(20, run.Diagnostics.EvidenceSearchCount);
        Assert.Equal(1, run.Diagnostics.EvidenceReadCount);
    }

    private async ValueTask<EffectiveToolSnapshot> CreateSnapshotAsync(params string[] threadIds)
    {
        _registry.Register(ThreadId, new DreamsRunInput(
            "dream_run",
            DreamsConstants.TriggerManual,
            _workspace.WorkspacePath,
            _baseline,
            _candidate,
            "manifest",
            threadIds.Select(id => new DreamsThreadInput(id, null, "desktop", ThreadStatus.Active, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)).ToArray(),
            null));
        return await new EffectiveToolSnapshotBuilder().BuildAsync(
            [new DreamsToolSource(_registry, new AppConfig(), new PathBlacklist([]))],
            new ToolPlanningContext(ThreadId, null, _workspace.WorkspacePath, _workspace.CraftPath, "agent", DreamsConstants.ToolProfileName, [], 1));
    }

    private static async Task<string> InvokeAsync(
        EffectiveToolSnapshot snapshot,
        string name,
        JsonObject arguments,
        bool success = true)
    {
        var result = await new ToolDispatcher().DispatchAsync(
            snapshot,
            new ToolName(null, name),
            arguments,
            new ToolInvocationRequest(ThreadId, null, $"call_{Guid.NewGuid():N}", ToolInvocationAudience.Model));
        Assert.True(success == result.Success, $"{name} {arguments.ToJsonString()}: {result.Content}");
        return result.Content ?? string.Empty;
    }
}
