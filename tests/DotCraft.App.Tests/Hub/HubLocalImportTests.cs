using DotCraft.Hub;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.SessionImport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DotCraft.Tests.Hub;

public sealed class HubLocalImportTests : IDisposable
{
    private readonly string _profile = Path.Combine(Path.GetTempPath(), "DotCraftLocalImport_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PreviewDoesNotRegisterProjectsAndAConfirmedBatchKeepsOtherResultsWhenOneProjectFails()
    {
        var paths = HubPaths.Resolve(_profile);
        var registry = new ProjectRegistry(paths);
        var existing = Project("existing");
        var incoming = Project("incoming");
        var omitted = Project("omitted");
        registry.Open(existing);
        var items = new[] {
            Chat("existing", existing, "project"),
            Chat("incoming", incoming, "newProject"),
            Chat("omitted", omitted, "newProject"),
            Chat("ordinary", paths.DefaultChatWorkspacePath, "chats")
        };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var imports = Create(paths, registry, items, async (path, request, _, progress, ct) =>
        {
            if (path == existing)
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(ct);
                throw new IOException("Unavailable project runtime");
            }

            var selected = Assert.Single(request.Items);
            progress(1);
            return Completed(selected);
        });

        await imports.DetectAsync(default);
        Assert.Equal([existing], registry.List().Select(project => project.Path));
        Assert.False(Directory.Exists(Path.Combine(incoming, ".craft")));
        Assert.False(Directory.Exists(paths.DefaultChatWorkspacePath));

        var request = Request("existing", "incoming", "ordinary");
        var run = imports.Run(request);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(run, imports.GetState().Running?.ImportId);
            Assert.Equal("import_busy", Assert.Throws<HubProtocolException>(() => imports.Run(request)).Code);
        }
        finally
        {
            release.TrySetResult();
        }
        await imports.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));

        var reloaded = Create(paths, registry, [], (_, _, _, _, _) => throw new InvalidOperationException());
        var batch = Assert.Single(reloaded.GetState().Imports);
        Assert.Equal(run, batch.ImportId);
        var outcomes = batch.Outcomes.ToDictionary(outcome => outcome.SourceId);
        Assert.Equal("failed", outcomes["existing"].Status);
        Assert.Equal((incoming, "thread_incoming"), (outcomes["incoming"].ProjectPath, outcomes["incoming"].ThreadId));
        Assert.Equal(paths.DefaultChatWorkspacePath, outcomes["ordinary"].ProjectPath);
        Assert.Contains(registry.List(), project => project.Path == incoming);
        Assert.DoesNotContain(registry.List(), project => project.Path == omitted);
        Assert.False(Directory.Exists(Path.Combine(omitted, ".craft")));
    }

    [Fact]
    public async Task SyncUsesRegisteredDestinationsAndRespectsOptOutAndNewProjectBoundaries()
    {
        var paths = HubPaths.Resolve(_profile);
        var registry = new ProjectRegistry(paths);
        var ready = Project("ready");
        var optedOut = Project("opt-out");
        var incoming = Project("incoming");
        var stopped = Project("stopped");
        registry.Open(ready);
        registry.Open(optedOut);
        registry.Open(stopped);
        var craft = Directory.CreateDirectory(Path.Combine(optedOut, ".craft"));
        File.WriteAllText(Path.Combine(craft.FullName, "config.json"), """{"AgentImport":{"SyncEnabled":false}}""");
        var items = new[]
        {
            Chat("ready", ready, "project"), Chat("opt-out", optedOut, "project"),
            Chat("incoming", incoming, "newProject"), Chat("stopped", stopped, "project")
        };
        var called = new List<(string Path, bool StartIfMissing)>();
        var imports = Create(paths, registry, items, (path, request, startIfMissing, _, _) =>
        {
            called.Add((path, startIfMissing));
            return Task.FromResult(Completed(Assert.Single(request.Items)));
        }, ready, optedOut);

        imports.SetSettings(new ImportSettingsSetParams
        {
            SyncEnabled = Optional<bool>.FromValue(true),
            Sources = Optional<IReadOnlyList<string>>.FromValue(["codex"]),
            Selection = Optional<ImportSelection>.FromValue(new() { Sessions = true })
        });
        await imports.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal((ready, false), Assert.Single(called));
        Assert.DoesNotContain(registry.List(), project => project.Path == incoming);
        Assert.False(Directory.Exists(Path.Combine(incoming, ".craft")));
        var state = imports.GetState();
        Assert.Equal("sync", Assert.Single(state.Imports).Trigger);
        Assert.True(state.Settings.LastSyncAt.IsSet);
    }

    [Fact]
    public async Task AChangedFingerprintDoesNotRegisterTheProjectOrStartItsAppServer()
    {
        var paths = HubPaths.Resolve(_profile);
        var registry = new ProjectRegistry(paths);
        var incoming = Project("incoming");
        var imports = Create(paths, registry, [Chat("incoming", incoming, "newProject")],
            (_, _, _, _, _) => throw new InvalidOperationException("A stale destination should not be started."));

        imports.Run(Request("incoming") with { Items = [new("incoming", "outdated")] });
        await imports.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("import_source_changed", Assert.Single(Assert.Single(imports.GetState().Imports).Outcomes).ErrorCode);
        Assert.Empty(registry.List());
        Assert.False(Directory.Exists(Path.Combine(incoming, ".craft")));
    }

    private static HubLocalImport Create(HubPaths paths, ProjectRegistry registry, LocalImportItem[] items,
        Func<string, ImportRunParams, bool, Action<int>, CancellationToken, Task<ImportCompletedNotification>> run,
        params string[] runningWorkspaces) => new(paths, registry, new HubEventBus(), run,
            () => runningWorkspaces.ToHashSet(ManagedAppServerRegistry.WorkspaceComparer), NullLogger.Instance, default,
            (_, _) => Task.FromResult<IReadOnlyList<LocalImportSource>>([new("codex", true, items, items.Length)]));

    private static HubLocalImportRequest Request(params string[] keys) =>
        new("codex", keys.Select(key => new HubLocalImportReference(key, "fingerprint")).ToArray(),
            new() { Sessions = true }, new() { Sessions = true });

    private static LocalImportItem Chat(string id, string path, string destination) =>
        new(id, new ImportCandidate
        {
            Source = "codex", SourceId = id, SourcePath = "session.jsonl", Title = id, Cwd = path,
            UpdatedAt = DateTimeOffset.UtcNow, TurnCount = 1, State = "new", Fingerprint = "fingerprint"
        }, path, Path.GetFileName(path), destination);

    private static ImportCompletedNotification Completed(ImportItemReference selected) => new()
    {
        ImportId = "import_" + selected.SourceId, Trigger = "manual",
        StartedAt = DateTimeOffset.UtcNow, CompletedAt = DateTimeOffset.UtcNow,
        Outcomes = [new ImportOutcome
        {
            Source = selected.Source, SourceId = selected.SourceId, Status = "imported",
            ThreadId = Optional<string>.FromValue("thread_" + selected.SourceId)
        }]
    };

    private string Project(string name) => Directory.CreateDirectory(Path.Combine(_profile, name)).FullName;

    public void Dispose()
    {
        if (Directory.Exists(_profile))
            Directory.Delete(_profile, recursive: true);
    }
}
